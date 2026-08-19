using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Retail25.Application.Abstractions;
using Retail25.Application.Terminals;
using Retail25.Application.UnitTests.Masters;
using Xunit;

namespace Retail25.Application.UnitTests.Rfid;

/// <summary>
/// Enrolling a machine.
/// <para>
/// Every agent currently shares one secret, which on an estate of 252 is one secret on 252 machines:
/// unrotatable for a single till, and one compromised PC compromising all of them. Enrolment is what
/// makes the thing an installer carries worthless — expiring, single-use, and exchanged for the real
/// credential over TLS at first start.
/// </para>
/// </summary>
public sealed class DeviceEnrolmentTests
{
    private sealed class FixedClock : IDateTime
    {
        public DateTimeOffset Now { get; set; } = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

        public DateOnly Today() => DateOnly.FromDateTime(Now.UtcDateTime);
    }

    private static (MastersTestHarness Harness, DeviceEnrolmentHandlers Handlers, FixedClock Clock) Fixture(
        MastersTestHarness harness)
    {
        var clock = new FixedClock();

        var credentials = Substitute.For<IAgentCredentialProvider>();
        credentials.ServerUrl.Returns("https://pos.example.test/backend");
        credentials.AgentSecret.Returns("the-durable-secret");

        return (harness, new DeviceEnrolmentHandlers(harness.Db, clock, credentials), clock);
    }

    [Fact]
    public async Task Generating_a_package_creates_the_machine_and_returns_a_code_once()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (_, handlers, _) = Fixture(harness);

        var result = await handlers.Handle(new GenerateAgentEnrolmentCommand(1, "pc-007", "Back office"), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.DeviceKey.Should().Be("PC-007", "the key is an identity and is normalised");
        result.Value.EnrolmentCode.Should().HaveLength(64, "256 bits as hex");
        result.Value.ServerUrl.Should().Be("https://pos.example.test/backend");

        (await harness.Db.Devices.CountAsync()).Should().Be(1);
    }

    /// <summary>
    /// The code is never stored, only its hash. A registry holding live codes is a list of keys to
    /// every till in the estate.
    /// </summary>
    [Fact]
    public async Task The_code_itself_is_not_stored()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (_, handlers, _) = Fixture(harness);

        var generated = await handlers.Handle(new GenerateAgentEnrolmentCommand(1, "PC-001"), default);

        var stored = await harness.Db.DeviceEnrolments.SingleAsync();

        stored.TokenHash.Should().NotBe(generated.Value.EnrolmentCode);
        stored.TokenHash.Should().HaveLength(64, "SHA-256 as hex");
    }

    [Fact]
    public async Task Redeeming_returns_the_durable_secret_and_records_the_machine()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (_, handlers, _) = Fixture(harness);

        var generated = await handlers.Handle(new GenerateAgentEnrolmentCommand(1, "PC-001"), default);

        var redeemed = await handlers.Handle(
            new RedeemAgentEnrolmentCommand(generated.Value.EnrolmentCode, "TILL-A", "Windows", "1.2.3"),
            default);

        redeemed.IsSuccess.Should().BeTrue();
        redeemed.Value.AgentSecret.Should().Be("the-durable-secret");
        redeemed.Value.DeviceKey.Should().Be("PC-001");

        var device = await harness.Db.Devices.SingleAsync();
        device.Hostname.Should().Be("TILL-A");
        device.AgentVersion.Should().Be("1.2.3");
    }

    /// <summary>Single use. A code that worked twice would enrol a second machine as the first.</summary>
    [Fact]
    public async Task A_code_cannot_be_redeemed_twice()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (_, handlers, _) = Fixture(harness);

        var generated = await handlers.Handle(new GenerateAgentEnrolmentCommand(1, "PC-001"), default);

        await handlers.Handle(new RedeemAgentEnrolmentCommand(generated.Value.EnrolmentCode, "A", null, null), default);

        var second = await handlers.Handle(
            new RedeemAgentEnrolmentCommand(generated.Value.EnrolmentCode, "B", null, null),
            default);

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("enrolment.already_redeemed");
    }

    /// <summary>
    /// Expiry is what makes a code left in an inbox worthless. Reported distinctly from
    /// already-used, because they mean opposite things to whoever is stood at the machine.
    /// </summary>
    [Fact]
    public async Task An_expired_code_is_refused_and_says_so()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (_, handlers, clock) = Fixture(harness);

        var generated = await handlers.Handle(new GenerateAgentEnrolmentCommand(1, "PC-001"), default);

        clock.Now = clock.Now.Add(DeviceEnrolmentHandlers.ValidFor).AddMinutes(1);

        var result = await handlers.Handle(
            new RedeemAgentEnrolmentCommand(generated.Value.EnrolmentCode, null, null, null),
            default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("enrolment.expired");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-real-code")]
    public async Task An_unrecognised_code_is_refused(string code)
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (_, handlers, _) = Fixture(harness);

        await handlers.Handle(new GenerateAgentEnrolmentCommand(1, "PC-001"), default);

        var result = await handlers.Handle(new RedeemAgentEnrolmentCommand(code, null, null, null), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("enrolment.not_found");
    }

    /// <summary>
    /// Two codes for the same machine are both valid until one is used — an installer who generated
    /// a code yesterday and again today should not find the newer one dead.
    /// </summary>
    [Fact]
    public async Task Generating_a_second_code_does_not_invalidate_the_machine()
    {
        using var harness = await MastersTestHarness.CreateAsync();
        var (_, handlers, _) = Fixture(harness);

        var first = await handlers.Handle(new GenerateAgentEnrolmentCommand(1, "PC-001"), default);
        var second = await handlers.Handle(new GenerateAgentEnrolmentCommand(1, "PC-001"), default);

        first.Value.EnrolmentCode.Should().NotBe(second.Value.EnrolmentCode);
        (await harness.Db.Devices.CountAsync()).Should().Be(1, "the same machine, not two");

        var redeemed = await handlers.Handle(
            new RedeemAgentEnrolmentCommand(second.Value.EnrolmentCode, null, null, null),
            default);

        redeemed.IsSuccess.Should().BeTrue();
    }
}

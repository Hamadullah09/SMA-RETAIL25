using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Retail25.TerminalAgent;
using Retail25.TerminalAgent.Server;
using AgentRfid = Retail25.TerminalAgent.Rfid;
using Xunit;

namespace Retail25.TerminalAgent.UnitTests;

/// <summary>
/// The machine tells the server it exists.
/// <para>
/// This is the call the whole per-antenna model rests on, and for a while nothing made it. The server
/// learns a machine from its first check-in; without one there is no Device row, so the configuration
/// endpoint answers <c>device.not_found</c> and the agent falls back to the single station profile
/// and inventories antenna one. A live till did exactly that for days, with a connected reader, a
/// green status strip and tags arriving — all of them from one antenna.
/// </para>
/// <para>
/// Nothing about that was visible from the outside, which is why it is pinned here rather than left
/// to an end-to-end test: the symptom of the missing call is a system that looks like it works.
/// </para>
/// </summary>
public sealed class DeviceCheckInTests
{
    private sealed class Recording : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;

        public Recording(HttpStatusCode status = HttpStatusCode.OK) => _status = status;

        public string? Path { get; private set; }

        public string? Body { get; private set; }

        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Path = request.RequestUri?.AbsolutePath;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_status);
        }
    }

    private sealed class OneClient : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public OneClient(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name)
            => new(_handler, disposeHandler: false) { BaseAddress = new Uri("https://shop.example/backend/") };
    }

    private static DeviceCheckIn Build(HttpMessageHandler handler, string deviceKey = "TILL-07")
    {
        var options = Options.Create(new AgentOptions { DeviceKey = deviceKey, LocationId = 4 });

        // A real supervisor with no sessions: it reports an empty reader list, which is the correct
        // thing for a machine that has not connected to anything yet and must still register.
        var readers = new AgentRfid.RfidReaderService(
            new EmptyProvider(),
            new AgentRfid.ProfileStore(),
            new AgentRfid.TagBuffer(),
            options,
            new AgentRfid.ReaderDiscovery(NullLogger<AgentRfid.ReaderDiscovery>.Instance),
            new AgentRfid.DeviceConfigurationStore(),
            NullLogger<AgentRfid.RfidReaderService>.Instance);

        return new DeviceCheckIn(new OneClient(handler), readers, options, NullLogger<DeviceCheckIn>.Instance);
    }

    private sealed class EmptyProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [Fact]
    public async Task Checks_in_at_the_devices_status_endpoint_with_this_machines_key()
    {
        var handler = new Recording();

        var checkedIn = await Build(handler).CheckInAsync(default);

        checkedIn.Should().BeTrue();
        handler.Path.Should().Be("/backend/api/v1/terminals/devices/status");

        using var body = JsonDocument.Parse(handler.Body!);

        // Upper-cased, because that is what ResolvedDeviceKey does and what the server stores. A
        // check-in under a differently-cased key registers a second machine that nobody configured.
        body.RootElement.GetProperty("deviceKey").GetString().Should().Be("TILL-07");
        body.RootElement.GetProperty("locationId").GetInt64().Should().Be(4);
        body.RootElement.TryGetProperty("readers", out _).Should().BeTrue();
    }

    /// <summary>
    /// A refusal is reported and does not throw.
    /// <para>
    /// It runs on the heartbeat, so an exception here would take the beat down with it and turn "the
    /// server does not know this machine" into "this till has stopped reporting at all".
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_refused_check_in_is_survivable()
    {
        var handler = new Recording(HttpStatusCode.BadRequest);

        var checkedIn = await Build(handler).CheckInAsync(default);

        checkedIn.Should().BeFalse();
        handler.Calls.Should().Be(1);
    }
}

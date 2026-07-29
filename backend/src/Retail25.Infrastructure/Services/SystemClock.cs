using System.Globalization;
using System.Security.Cryptography;
using Retail25.Application.Abstractions;

namespace Retail25.Infrastructure.Services;

/// <summary>The real clock. Tests substitute a fixed one so pricing and tax stay deterministic.</summary>
public sealed class SystemClock : IDateTime
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}

/// <summary>
/// The payment adapter that ships first (decision Q1). It approves, returns a plausible auth code and
/// a masked card, and declines on a reserved amount so the decline path is exercisable.
/// <para>
/// The point of shipping this rather than waiting for a processor is that the entire payment UX —
/// split tender, signature copies, void, refund — is testable and demonstrable before the vendor is
/// chosen. No vendor name appears in Domain or Application, so swapping this out is a registration
/// change.
/// </para>
/// </summary>
public sealed class SimulatorPaymentGateway : IPaymentGateway
{
    /// <summary>Charge this amount to see a decline. Used by tests and by staff training.</summary>
    public const decimal DeclineTriggerAmount = 6.66m;

    public string Provider => "Simulator";

    public Task<PaymentResult> AuthorizeAsync(decimal amount, string currencyCode, string? cardToken, CancellationToken ct = default)
    {
        if (amount == DeclineTriggerAmount)
        {
            return Task.FromResult(new PaymentResult(
                PaymentResultStatus.Declined, null, null, null, "Declined by issuer (simulated)."));
        }

        return Task.FromResult(new PaymentResult(
            PaymentResultStatus.Approved,
            NewReference("AUTH"),
            cardToken is { Length: >= 4 } ? cardToken[^4..] : "4242",
            NewReference("SIM"),
            null));
    }

    public Task<PaymentResult> CaptureAsync(string authCode, decimal amount, string currencyCode, CancellationToken ct = default)
        => Task.FromResult(new PaymentResult(PaymentResultStatus.Approved, authCode, null, NewReference("CAP"), null));

    public Task<RefundResult> RefundAsync(string originalAuthCode, decimal amount, string currencyCode, CancellationToken ct = default)
        => Task.FromResult(new RefundResult(PaymentResultStatus.Approved, NewReference("REF"), null));

    public Task<PaymentResult> VoidAsync(string authCode, CancellationToken ct = default)
        => Task.FromResult(new PaymentResult(PaymentResultStatus.Approved, authCode, null, NewReference("VOID"), null));

    private static string NewReference(string prefix)
        => prefix + RandomNumberGenerator.GetInt32(100000, 999999).ToString(CultureInfo.InvariantCulture);
}

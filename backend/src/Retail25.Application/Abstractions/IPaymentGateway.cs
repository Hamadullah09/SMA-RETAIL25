namespace Retail25.Application.Abstractions;

public enum PaymentResultStatus
{
    Approved = 0,
    Declined = 1,
    Error = 2,
    Timeout = 3,
}

public sealed record PaymentResult(
    PaymentResultStatus Status,
    string? AuthCode,
    string? CardLast4,
    string? GatewayReference,
    string? ErrorMessage);

public sealed record RefundResult(
    PaymentResultStatus Status,
    string? GatewayReference,
    string? ErrorMessage);

/// <summary>
/// Port for payment processor integration (doc 07 Q1). A simulator adapter ships first;
/// real processor adapters are config-selected, never referenced in Domain or Application.
/// </summary>
public interface IPaymentGateway
{
    string Provider { get; }

    Task<PaymentResult> AuthorizeAsync(decimal amount, string currencyCode, string? cardToken, CancellationToken ct = default);

    Task<PaymentResult> CaptureAsync(string authCode, decimal amount, string currencyCode, CancellationToken ct = default);

    Task<RefundResult> RefundAsync(string originalAuthCode, decimal amount, string currencyCode, CancellationToken ct = default);

    Task<PaymentResult> VoidAsync(string authCode, CancellationToken ct = default);
}

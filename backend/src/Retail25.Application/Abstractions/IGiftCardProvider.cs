namespace Retail25.Application.Abstractions;

public sealed record GiftCardBalanceResult(bool Success, decimal Balance, string? ErrorMessage);

/// <summary>
/// Port for gift card balance inquiry and redemption (guide p.106–107).
/// </summary>
public interface IGiftCardProvider
{
    Task<GiftCardBalanceResult> GetBalanceAsync(string cardNumber, CancellationToken ct = default);

    Task<bool> RedeemAsync(string cardNumber, decimal amount, CancellationToken ct = default);

    Task<bool> IssueAsync(string cardNumber, decimal initialBalance, CancellationToken ct = default);
}

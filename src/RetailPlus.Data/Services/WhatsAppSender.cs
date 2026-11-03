using System.Collections.Concurrent;

namespace RetailPlus.Data.Services;

/// <summary>One template message to send: the number (international form), the template's name and language, and its parameters.</summary>
public sealed record WhatsAppOutgoing(string To, string TemplateName, string Language, IReadOnlyList<string> Parameters);

/// <summary>What sending gave: WhatsApp's id of the message, or why it was not sent.</summary>
public sealed record WhatsAppSendResult(bool Ok, string? MessageId, string? Error)
{
    public static WhatsAppSendResult Sent(string messageId) => new(true, messageId, null);
    public static WhatsAppSendResult Refused(string error) => new(false, null, error);
}

/// <summary>
/// Sends WhatsApp template messages. The real one posts to Meta's Graph API and is switched on by settings
/// (<c>MetaCloudWhatsAppSender</c> in the server); everywhere else <see cref="RecordingWhatsAppSender"/> keeps what
/// would have been sent, so no real message ever leaves a test or a development machine.
/// </summary>
public interface IWhatsAppSender
{
    /// <summary>"meta" for the real API, "recording" for the stand-in.</summary>
    string Provider { get; }

    Task<WhatsAppSendResult> SendAsync(WhatsAppOutgoing message, CancellationToken cancellationToken = default);
}

/// <summary>
/// The stand-in sender: records every message and answers with a made-up message id. A number listed in
/// <see cref="Refuse"/> is refused, so tests can see a failed send.
/// </summary>
public sealed class RecordingWhatsAppSender : IWhatsAppSender
{
    private readonly ConcurrentQueue<(WhatsAppOutgoing Message, string Id)> _sent = new();

    public string Provider => "recording";

    /// <summary>Numbers to refuse (as the API refuses a number that is not on WhatsApp).</summary>
    public ConcurrentDictionary<string, string> Refuse { get; } = new(StringComparer.Ordinal);

    /// <summary>Everything "sent", oldest first, with the id it was given.</summary>
    public IReadOnlyList<(WhatsAppOutgoing Message, string Id)> Sent => [.. _sent];

    public Task<WhatsAppSendResult> SendAsync(WhatsAppOutgoing message, CancellationToken cancellationToken = default)
    {
        if (Refuse.TryGetValue(message.To, out var why)) return Task.FromResult(WhatsAppSendResult.Refused(why));
        var id = "wamid.recorded-" + Guid.NewGuid().ToString("N");
        _sent.Enqueue((message, id));
        return Task.FromResult(WhatsAppSendResult.Sent(id));
    }
}

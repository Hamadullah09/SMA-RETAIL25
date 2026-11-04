using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using RetailPlus.Core.Rules;
using RetailPlus.Data.Services;

namespace RetailPlus.Server;

/// <summary>
/// The "WhatsApp" section: the Meta Cloud API (off unless <see cref="Enabled"/> and a phone number id and token are
/// set), the webhook's verify token and app secret, and how fast campaigns are sent.
/// </summary>
public sealed class WhatsAppOptions
{
    public bool Enabled { get; set; }
    /// <summary>The WhatsApp Business phone number id messages are sent from.</summary>
    public string? PhoneNumberId { get; set; }
    public string? AccessToken { get; set; }
    /// <summary>The token Meta sends back when the webhook is set up.</summary>
    public string? VerifyToken { get; set; }
    /// <summary>The app secret that signs the webhook's notifications (X-Hub-Signature-256).</summary>
    public string? AppSecret { get; set; }
    public string GraphUrl { get; set; } = "https://graph.facebook.com";
    public string ApiVersion { get; set; } = "v21.0";
    /// <summary>At most this many campaign messages a minute.</summary>
    public int MessagesPerMinute { get; set; } = 30;
    /// <summary>How often the sender looks for messages that are due; 0 switches it off.</summary>
    public int WorkerIntervalSeconds { get; set; } = 20;

    /// <summary>True when the real API is configured and switched on.</summary>
    public bool UsesMeta => Enabled && !string.IsNullOrWhiteSpace(PhoneNumberId) && !string.IsNullOrWhiteSpace(AccessToken);
}

/// <summary>
/// Sends template messages through Meta's WhatsApp Cloud API: <c>POST {graph}/{version}/{phone number id}/messages</c>
/// with the access token. Used only when <see cref="WhatsAppOptions.UsesMeta"/>; tests and development never reach it.
/// </summary>
public sealed class MetaCloudWhatsAppSender(HttpClient http, IOptions<WhatsAppOptions> options) : IWhatsAppSender
{
    public string Provider => "meta";

    public async Task<WhatsAppSendResult> SendAsync(WhatsAppOutgoing message, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{o.GraphUrl.TrimEnd('/')}/{o.ApiVersion}/{o.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(Body(message)),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", o.AccessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (response.IsSuccessStatusCode && doc.RootElement.TryGetProperty("messages", out var messages) && messages.GetArrayLength() > 0
                && messages[0].TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } wamid)
                return WhatsAppSendResult.Sent(wamid);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var why))
                return WhatsAppSendResult.Refused($"WhatsApp: {why.GetString()}");
        }
        catch (JsonException)
        {
        }
        return WhatsAppSendResult.Refused($"WhatsApp answered {(int)response.StatusCode}.");
    }

    /// <summary>The request body of a template message with its body parameters.</summary>
    public static object Body(WhatsAppOutgoing m) => new
    {
        messaging_product = "whatsapp",
        to = WhatsAppNumbers.ForApi(m.To),
        type = "template",
        template = new
        {
            name = m.TemplateName,
            language = new { code = m.Language },
            components = m.Parameters.Count == 0 ? Array.Empty<object>()
                : [new { type = "body", parameters = m.Parameters.Select(p => new { type = "text", text = p }).ToArray() }],
        },
    };
}

/// <summary>
/// Sends the campaigns that are due, a few messages every <see cref="WhatsAppOptions.WorkerIntervalSeconds"/>, so no more
/// than <see cref="WhatsAppOptions.MessagesPerMinute"/> go out in a minute.
/// </summary>
public sealed class CampaignWorker(Data.Services.DataContext data, IWhatsAppSender sender, IOptions<WhatsAppOptions> options, ILogger<CampaignWorker> log)
    : BackgroundService
{
    /// <summary>The messages one round may send.</summary>
    public static int Budget(WhatsAppOptions o) => Math.Max(1, o.MessagesPerMinute * Math.Max(1, o.WorkerIntervalSeconds) / 60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (o.WorkerIntervalSeconds <= 0) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(o.WorkerIntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    int sent = await new CampaignService(data).SendDueAsync(sender, Budget(o), cancellationToken: stoppingToken);
                    if (sent > 0) ServerLog.CampaignMessagesSent(log, sent, sender.Provider);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    ServerLog.CampaignSendFailed(log, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}

/// <summary>The webhook's signature: <c>X-Hub-Signature-256: sha256=&lt;hex HMAC-SHA256 of the body with the app secret&gt;</c>.</summary>
public static class WebhookSignature
{
    public static string Sign(string appSecret, byte[] body) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body));

    public static bool IsValid(string? appSecret, byte[] body, string? header)
    {
        if (string.IsNullOrEmpty(appSecret) || string.IsNullOrEmpty(header)) return false;
        var expected = Encoding.ASCII.GetBytes(Sign(appSecret, body));
        var given = Encoding.ASCII.GetBytes(header.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }
}

/// <summary>Meta's status notification, as far as the server reads it.</summary>
public sealed class WebhookNotification
{
    [JsonPropertyName("entry")] public List<WebhookEntry> Entry { get; set; } = [];
}

public sealed class WebhookEntry
{
    [JsonPropertyName("changes")] public List<WebhookChange> Changes { get; set; } = [];
}

public sealed class WebhookChange
{
    [JsonPropertyName("value")] public WebhookValue? Value { get; set; }
}

public sealed class WebhookValue
{
    [JsonPropertyName("statuses")] public List<WebhookStatus> Statuses { get; set; } = [];
}

public sealed class WebhookStatus
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    /// <summary>Unix seconds, as a string.</summary>
    [JsonPropertyName("timestamp")] public string? Timestamp { get; set; }
    [JsonPropertyName("errors")] public List<WebhookError> Errors { get; set; } = [];
}

public sealed class WebhookError
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

public static class WhatsAppSetup
{
    /// <summary>
    /// The WhatsApp sender: Meta's Cloud API when the settings switch it on, else the recording stand-in (development and
    /// tests); and the worker that sends due campaigns.
    /// </summary>
    public static IServiceCollection AddWhatsApp(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("WhatsApp");
        services.Configure<WhatsAppOptions>(section);
        var options = section.Get<WhatsAppOptions>() ?? new WhatsAppOptions();
        if (options.UsesMeta)
        {
            services.AddHttpClient<IWhatsAppSender, MetaCloudWhatsAppSender>(client => client.Timeout = TimeSpan.FromSeconds(30));
        }
        else
        {
            services.AddSingleton<RecordingWhatsAppSender>();
            services.AddSingleton<IWhatsAppSender>(provider => provider.GetRequiredService<RecordingWhatsAppSender>());
        }
        services.AddHostedService<CampaignWorker>();
        return services;
    }
}

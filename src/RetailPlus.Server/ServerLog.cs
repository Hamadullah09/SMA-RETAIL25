namespace RetailPlus.Server;

/// <summary>The server's log messages.</summary>
internal static partial class ServerLog
{
    [LoggerMessage(1, LogLevel.Warning, "Super administrator {User} was created with the password {Password}. It is shown only this once; sign in and change it.")]
    public static partial void AdminMade(ILogger logger, string user, string password);

    [LoggerMessage(2, LogLevel.Information, "Super administrator {User} was created with the password from the configuration.")]
    public static partial void AdminConfigured(ILogger logger, string user);

    [LoggerMessage(3, LogLevel.Information, "Station {Station} of store {Store} joined from {Machine}.")]
    public static partial void StationJoined(ILogger logger, int station, string store, string? machine);

    [LoggerMessage(4, LogLevel.Warning, "Package {Batch} from station {Station} refused: {Error}")]
    public static partial void PackageRefused(ILogger logger, Guid batch, int station, string? error);

    [LoggerMessage(5, LogLevel.Information, "No web back office to serve (Server:WebRoot, or web/out above {ContentRoot}); serving the API only.")]
    public static partial void NoWebApp(ILogger logger, string contentRoot);

    [LoggerMessage(6, LogLevel.Information, "Serving the web back office from {WebRoot}.")]
    public static partial void WebApp(ILogger logger, string webRoot);

    [LoggerMessage(7, LogLevel.Warning, "No active user can manage every store. Set Bootstrap:AdminUserName to the user who should (an existing user name, or a new one) and restart the server.")]
    public static partial void NoSuperAdministrator(ILogger logger);

    [LoggerMessage(8, LogLevel.Warning, "Nobody could manage every store, so {User} (Bootstrap:AdminUserName) was made super administrator.")]
    public static partial void AdministratorPromoted(ILogger logger, string user);

    // Phase E4 (ids from 401).
    [LoggerMessage(401, LogLevel.Information, "{Count} campaign messages sent ({Provider}).")]
    public static partial void CampaignMessagesSent(ILogger logger, int count, string provider);

    [LoggerMessage(402, LogLevel.Error, "Sending campaign messages failed; the next round tries again.")]
    public static partial void CampaignSendFailed(ILogger logger, Exception exception);

    [LoggerMessage(403, LogLevel.Warning, "A WhatsApp webhook notification was refused: {Reason}")]
    public static partial void WebhookRefused(ILogger logger, string reason);

}

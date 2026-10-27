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

}

namespace RetailPlus.Server;

/// <summary>The server's log messages.</summary>
internal static partial class ServerLog
{
    [LoggerMessage(1, LogLevel.Warning, "First start: super administrator {User} was created with the password {Password}. It is shown only this once; sign in and change it.")]
    public static partial void AdminMade(ILogger logger, string user, string password);

    [LoggerMessage(2, LogLevel.Information, "First start: super administrator {User} was created with the password from the configuration.")]
    public static partial void AdminConfigured(ILogger logger, string user);

    [LoggerMessage(3, LogLevel.Information, "Station {Station} of store {Store} joined from {Machine}.")]
    public static partial void StationJoined(ILogger logger, int station, string store, string? machine);

    [LoggerMessage(4, LogLevel.Warning, "Package {Batch} from station {Station} refused: {Error}")]
    public static partial void PackageRefused(ILogger logger, Guid batch, int station, string? error);
}

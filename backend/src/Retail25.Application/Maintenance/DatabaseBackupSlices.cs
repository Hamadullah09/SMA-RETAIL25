using MediatR;
using Retail25.Application.Common;
using Retail25.Domain.Common;

namespace Retail25.Application.Maintenance;

/// <summary>A backup file on disk, as shown to the administrator.</summary>
public sealed record BackupFileDto(string FileName, long SizeBytes, DateTimeOffset CreatedAt);

/// <summary>
/// Takes, lists and restores whole-database backups.
/// <para>
/// The port lives here and the SQL Server BACKUP/RESTORE statements live in Infrastructure, because
/// which engine holds the data is not the till's business. The procedure itself is the one
/// documented in <c>docs/runbooks/restore.md</c>; this feature exists so that following it does not
/// require a query window.
/// </para>
/// </summary>
public interface IDatabaseBackupService
{
    /// <summary>
    /// Resolves a named backup to something the API can stream, refusing anything that is not one
    /// of ours. Implementations that keep their archives on another machine cannot answer this,
    /// which is itself the reason the portable format exists.
    /// </summary>
    Result<string> ResolvePath(string fileName);

    Task<Result<BackupFileDto>> BackupAsync(CancellationToken ct);

    Task<Result<IReadOnlyList<BackupFileDto>>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Verifies the named file and replaces the live database with it. Every open session is
    /// dropped while the restore runs; callers must treat this as taking the system down for the
    /// duration.
    /// </summary>
    Task<Result> RestoreAsync(string fileName, CancellationToken ct);
}

[RequiresPermission(PermissionKeys.System.Backup)]
public sealed record CreateDatabaseBackupCommand : IRequest<Result<BackupFileDto>>;

[RequiresPermission(PermissionKeys.System.Backup)]
public sealed record ListDatabaseBackupsQuery : IRequest<Result<IReadOnlyList<BackupFileDto>>>;

[RequiresPermission(PermissionKeys.System.Backup)]
public sealed record RestoreDatabaseBackupCommand(string FileName) : IRequest<Result>;

/// <summary>
/// Checks the caller may download this archive, and where it is. A query rather than a controller
/// check so the permission is enforced in the same place as every other one — a hidden button is
/// not security, and neither is a route nobody has linked to.
/// </summary>
[RequiresPermission(PermissionKeys.System.Backup)]
public sealed record AuthorizeBackupDownloadQuery(string FileName) : IRequest<Result<string>>;

public sealed class AuthorizeBackupDownloadHandler : IRequestHandler<AuthorizeBackupDownloadQuery, Result<string>>
{
    private readonly IDatabaseBackupService _backups;

    public AuthorizeBackupDownloadHandler(IDatabaseBackupService backups) => _backups = backups;

    public Task<Result<string>> Handle(AuthorizeBackupDownloadQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(_backups.ResolvePath(request.FileName));
    }
}

public sealed class CreateDatabaseBackupHandler : IRequestHandler<CreateDatabaseBackupCommand, Result<BackupFileDto>>
{
    private readonly IDatabaseBackupService _backups;

    public CreateDatabaseBackupHandler(IDatabaseBackupService backups) => _backups = backups;

    public Task<Result<BackupFileDto>> Handle(CreateDatabaseBackupCommand request, CancellationToken ct)
        => _backups.BackupAsync(ct);
}

public sealed class ListDatabaseBackupsHandler : IRequestHandler<ListDatabaseBackupsQuery, Result<IReadOnlyList<BackupFileDto>>>
{
    private readonly IDatabaseBackupService _backups;

    public ListDatabaseBackupsHandler(IDatabaseBackupService backups) => _backups = backups;

    public Task<Result<IReadOnlyList<BackupFileDto>>> Handle(ListDatabaseBackupsQuery request, CancellationToken ct)
        => _backups.ListAsync(ct);
}

public sealed class RestoreDatabaseBackupHandler : IRequestHandler<RestoreDatabaseBackupCommand, Result>
{
    private readonly IDatabaseBackupService _backups;

    public RestoreDatabaseBackupHandler(IDatabaseBackupService backups) => _backups = backups;

    public Task<Result> Handle(RestoreDatabaseBackupCommand request, CancellationToken ct)
        => _backups.RestoreAsync(request.FileName, ct);
}

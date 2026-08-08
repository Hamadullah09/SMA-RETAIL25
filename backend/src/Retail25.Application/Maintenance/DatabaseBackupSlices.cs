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

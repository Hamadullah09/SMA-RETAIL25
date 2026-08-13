using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail25.Application.Catalog;
using Retail25.Application.Maintenance;
using Retail25.Domain.Catalog;
using Retail25.Infrastructure.Persistence;
using Xunit;

namespace Retail25.IntegrationTests;

/// <summary>
/// The backup, opened.
/// <para>
/// A backup nobody has read back is a hope. The old path could not even be read back in principle
/// on this deployment: <c>BACKUP DATABASE … TO DISK</c> writes on the database server's filesystem,
/// which here is a different machine from the application, so the file was written somewhere the
/// app could not list, open or offer for download — and the directory it did look in was under a
/// user profile the shared app pool has no rights to. Two independent reasons for the same
/// symptom: an empty list and a failed backup.
/// </para>
/// <para>
/// So these assert the thing that matters rather than that a call returned success: that a product
/// created a moment ago is <b>inside the file</b>.
/// </para>
/// </summary>
[Collection(CommerceApiCollection.Name)]
public sealed class PortableBackupTests
{
    private readonly CommerceApiFixture _api;

    public PortableBackupTests(CommerceApiFixture api) => _api = api;

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..18].ToUpperInvariant();

    [RequiresDockerFact]
    public async Task A_backup_contains_the_row_that_was_just_written()
    {
        using var scope = _api.Scope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var backups = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();

        var location = await db.Locations.AsNoTracking().FirstAsync();
        _api.ActingUser.LocationId = location.Id;

        var stockCode = Unique("BKUP");

        await Ok(sender.Send(new CreateProductCommand(
            location.Id,
            new ProductGeneralSection(stockCode, "In the backup", null, ProductType.Standard, null, null, null, null),
            RegularPrice: 12.34m,
            Tax1Applies: false,
            Tax2Applies: false)));

        var taken = await backups.BackupAsync(default);
        taken.IsSuccess.Should().BeTrue(taken.IsFailure ? taken.Error.Code : string.Empty);
        taken.Value.SizeBytes.Should().BeGreaterThan(0);

        var path = backups.ResolvePath(taken.Value.FileName);
        path.IsSuccess.Should().BeTrue("the file has to be reachable, or it cannot be taken off the server");

        using var archive = ZipFile.OpenRead(path.Value);

        var manifest = archive.GetEntry("manifest.json");
        manifest.Should().NotBeNull("a restore has to know which schema it came from");

        using (var reader = new StreamReader(manifest!.Open()))
        {
            using var document = JsonDocument.Parse(await reader.ReadToEndAsync());
            document.RootElement.GetProperty("format").GetString().Should().Be("retail25.portable.v1");
            document.RootElement.GetProperty("totalRows").GetInt64().Should().BeGreaterThan(0);
            document.RootElement.GetProperty("schemaMigration").GetString().Should().NotBeNullOrEmpty();
        }

        var products = archive.GetEntry("tables/products.jsonl");
        products.Should().NotBeNull();

        using var lines = new StreamReader(products!.Open());
        var contents = await lines.ReadToEndAsync();

        contents.Should().Contain(stockCode, "the product written a moment ago must be in the file");
    }

    /// <summary>Only our own archives, and nothing that climbs out of the folder.</summary>
    [RequiresDockerFact]
    public async Task A_download_refuses_anything_that_is_not_a_backup()
    {
        using var scope = _api.Scope();
        var backups = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();

        foreach (var name in new[]
        {
            "../../../web.config",
            "..\\appsettings.json",
            "notes.txt",
            "",
        })
        {
            backups.ResolvePath(name).IsFailure.Should().BeTrue($"'{name}' is not a backup this took");
        }

        await Task.CompletedTask;
    }

    private static async Task<T> Ok<T>(Task<Retail25.Domain.Common.Result<T>> pending)
    {
        var result = await pending;
        result.IsSuccess.Should().BeTrue($"the step should succeed, but failed with '{result.Error.Code}'");
        return result.Value;
    }
}

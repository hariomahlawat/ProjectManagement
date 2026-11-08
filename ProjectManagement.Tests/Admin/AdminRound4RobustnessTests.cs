using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.Admin.Pages.Chat;
using ProjectManagement.Areas.DocumentRepository.Pages.Admin;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Features.Chat;
using ProjectManagement.Hubs;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Ingestion;
using ProjectManagement.Services.Chat;
using ProjectManagement.Tests.Fakes;
using Xunit;
using RestoredHiddenModel = ProjectManagement.Areas.DocumentRepository.Pages.Admin.RestoredHidden.IndexModel;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// Fourth cold review of Administration, PR 10 (docs/ux/admin-review-4-fixes.md): one IST
/// formatter (C6) and the PDF run summary's trim (minor). The repository purge (C4) is covered in
/// DocRepoTrashPurgeServiceTests.
/// </summary>
public sealed class AdminRound4RobustnessTests
{
    // 08:35 UTC is 14:05 IST.
    private static readonly DateTimeOffset Moment = new(2026, 11, 7, 8, 35, 0, TimeSpan.Zero);
    private const string Expected = "7 Nov 2026, 14:05 IST";

    private static AdminTimeService Time() => new(FakeClock.AtUtc(Moment));

    [Fact]
    public void FormatIst_WritesTheDayWithoutALeadingZero()
    {
        Assert.Equal(Expected, Time().FormatIst(Moment));
        Assert.Equal(Expected, Time().FormatIst(Moment.UtcDateTime));
    }

    [Fact]
    public void ChatStorage_UndoDeadline_UsesTheAdminFormatter_WithIst()
    {
        var page = new StorageModel(
            (ChatStorageService)RuntimeHelpers.GetUninitializedObject(typeof(ChatStorageService)),
            (ChatAttachmentService)RuntimeHelpers.GetUninitializedObject(typeof(ChatAttachmentService)),
            Unused<IHubContext<ChatHub, IChatClient>>(),
            Unused<IOptionsMonitor<ChatOptions>>(),
            Time());

        Assert.Equal(Expected, page.Local(Moment));
    }

    [Fact]
    public void MissingFiles_CheckedAt_UsesTheAdminFormatter()
    {
        var page = new MissingFilesModel(null!, null!, Time());
        typeof(MissingFilesModel).GetProperty(nameof(MissingFilesModel.CheckedUtc))!.SetValue(page, Moment);

        Assert.Equal(Expected, page.CheckedAtIst);
    }

    [Fact]
    public void RestoredHidden_UsesTheAdminFormatter()
    {
        var page = new RestoredHiddenModel(null!, Time());

        Assert.Equal(Expected, page.FormatIst(Moment));
    }

    [Theory]
    [InlineData("Areas/Admin/Pages/Chat/Storage.cshtml.cs")]
    [InlineData("Areas/DocumentRepository/Pages/Admin/MissingFiles.cshtml.cs")]
    [InlineData("Areas/DocumentRepository/Pages/Admin/RestoredHidden/Index.cshtml.cs")]
    public void ThePagesNoLongerFormatIstByHand(string source)
    {
        var text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), source));

        Assert.DoesNotContain("TimeZoneHelper", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IstClock", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"d MMM yyyy", text, StringComparison.Ordinal);
        Assert.Contains("_time.FormatIst(", text, StringComparison.Ordinal);
    }

    // The summary row was saved; only removing the oldest runs failed. RecordAsync used to throw,
    // so the coordinator logged "Could not save the PDF ingestion run summary" although it had.
    [Fact]
    public async Task PdfRunSummary_ATrimFailure_DoesNotReportTheSaveAsFailed()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var seedOptions = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using (var seed = new ApplicationDbContext(seedOptions))
        {
            await seed.Database.EnsureCreatedAsync();
            var history = new PdfIngestionRunHistory(seed);
            for (var i = 0; i < PdfIngestionRunHistory.Capacity; i++)
            {
                await history.RecordAsync(Run(Guid.NewGuid(), Moment.AddDays(-100 + i)));
            }
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new FailingTrimInterceptor())
            .Options;
        await using var db = new ApplicationDbContext(options);
        var runId = Guid.NewGuid();

        await new PdfIngestionRunHistory(db).RecordAsync(Run(runId, Moment));

        await using var check = new ApplicationDbContext(seedOptions);
        Assert.NotNull(await new PdfIngestionRunHistory(check).GetAsync(runId));
        Assert.Equal(PdfIngestionRunHistory.Capacity + 1, await check.PdfIngestionRuns.CountAsync());
        // The context is left clean, so the caller's next save does not retry the failed delete.
        Assert.DoesNotContain(db.ChangeTracker.Entries(), entry => entry.State == EntityState.Deleted);
    }

    private static PdfIngestionRunRecord Run(Guid id, DateTimeOffset completed) => new(
        id,
        completed.AddMinutes(-5),
        completed,
        "admin",
        "Completed",
        1, 1, 0, 0, 0,
        Array.Empty<PdfIngestionSourceSummary>(),
        Array.Empty<PdfIngestionFailure>(),
        null);

    private sealed class FailingTrimInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<PdfIngestionRun>().Any(e => e.State == EntityState.Deleted))
            {
                throw new DbUpdateException("The table is locked.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static T Unused<T>() where T : class => DispatchProxy.Create<T, UnusedProxy>();

    public class UnusedProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"{targetMethod?.Name} should not be called.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectManagement.csproj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

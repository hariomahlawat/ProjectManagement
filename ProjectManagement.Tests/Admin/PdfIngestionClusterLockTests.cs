using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Application.Ipr;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Admin.Ingestion;
using ProjectManagement.Services.DocRepo;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>
/// The in-process run gate only guarded one process, so two runs in different processes
/// (a rolling deployment, two instances) could ingest the same attachments twice.
/// </summary>
public sealed class PdfIngestionClusterLockTests
{
    [Fact]
    public async Task Run_IsRefusedWhileAnotherProcessHoldsTheLock_AndReleasesTheLocalGate()
    {
        var gate = new PdfIngestionRunGate();
        var audit = new CountingAudit();
        var coordinator = CreateCoordinator(gate, new HeldElsewhereLock(), audit);

        var result = await coordinator.RunAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("PdfIngestionAlreadyRunning", result.ErrorCode);
        Assert.Contains("another application server", result.UserMessage);
        Assert.Equal(0, audit.Count);
        Assert.False(gate.IsRunning);
    }

    // Runs only against a real PostgreSQL server (the database workflow provides one).
    [PdfIngestionPostgresFact]
    public async Task AdvisoryLock_IsExclusiveAcrossSessions_AndReleasedOnDispose()
    {
        var connection = Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")!;
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options;
        await using var firstDb = new ApplicationDbContext(options);
        await using var secondDb = new ApplicationDbContext(options);
        var first = new PdfIngestionClusterLock(firstDb);
        var second = new PdfIngestionClusterLock(secondDb);

        await using (var held = await first.TryAcquireAsync(CancellationToken.None))
        {
            Assert.NotNull(held);
            Assert.Null(await second.TryAcquireAsync(CancellationToken.None));
        }

        await using var afterRelease = await second.TryAcquireAsync(CancellationToken.None);
        Assert.NotNull(afterRelease);
    }

    private static PdfIngestionCoordinator CreateCoordinator(
        IPdfIngestionRunGate gate,
        IPdfIngestionClusterLock clusterLock,
        IAdminAuditService audit)
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"pdf-ingestion-{Guid.NewGuid():N}")
            .Options);
        var pathResolver = Unused<IUploadPathResolver>();

        return new PdfIngestionCoordinator(
            db,
            Unused<IDocRepoIngestionService>(),
            new IprAttachmentStorage(new TestUploadRootProvider(System.IO.Path.GetTempPath()), pathResolver, Options.Create(new IprAttachmentOptions())),
            pathResolver,
            Options.Create(new DocRepoOptions { EnableIngestion = true }),
            gate,
            clusterLock,
            audit,
            new PdfIngestionRunHistory(),
            new AdminTimeService(FakeClock.AtUtc(new DateTimeOffset(2026, 11, 6, 6, 0, 0, TimeSpan.Zero))),
            new HttpContextAccessor(),
            NullLogger<PdfIngestionCoordinator>.Instance);
    }

    private static T Unused<T>() where T : class => DispatchProxy.Create<T, UnusedProxy>();

    public class UnusedProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"{targetMethod?.Name} should not be called when the run is refused.");
    }

    private sealed class HeldElsewhereLock : IPdfIngestionClusterLock
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable?>(null);
    }

    private sealed class CountingAudit : IAdminAuditService
    {
        public int Count { get; private set; }

        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class PdfIngestionPostgresFactAttribute : FactAttribute
{
    public PdfIngestionPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PRISM_TEST_POSTGRES_CONNECTION")))
        {
            Skip = "Provide PRISM_TEST_POSTGRES_CONNECTION to run the PDF ingestion advisory-lock test.";
        }
    }
}

using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models.Scheduling;
using ProjectManagement.Services.Admin.Ingestion;
using Xunit;

namespace ProjectManagement.Tests.Admin;

public sealed class AdminCalendarAndIngestionFoundationTests
{
    [Fact]
    public void PdfIngestionRunGate_AllowsOnlyOneConcurrentLease()
    {
        var gate = new PdfIngestionRunGate();

        Assert.True(gate.TryEnter(out var firstLease));
        Assert.NotNull(firstLease);
        Assert.True(gate.IsRunning);
        Assert.False(gate.TryEnter(out var secondLease));
        Assert.Null(secondLease);

        firstLease!.Dispose();

        Assert.False(gate.IsRunning);
        Assert.True(gate.TryEnter(out var thirdLease));
        thirdLease!.Dispose();
    }

    [Fact]
    public void Holiday_RowVersion_IsRequiredConcurrencyToken()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        using var db = new ApplicationDbContext(options);
        var entityType = db.Model.FindEntityType(typeof(Holiday));
        var rowVersion = entityType?.FindProperty(nameof(Holiday.RowVersion));

        Assert.NotNull(rowVersion);
        Assert.True(rowVersion!.IsConcurrencyToken);
        Assert.False(rowVersion.IsNullable);
    }

    [Fact]
    public void PdfIngestionRunResult_AggregatesSourceCounters()
    {
        var sources = new[]
        {
            new PdfIngestionSourceSummary("FFC", 10, 3, 5, 1, 1),
            new PdfIngestionSourceSummary("IPR", 4, 2, 2, 0, 0)
        };

        var result = new PdfIngestionRunResult(
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            sources,
            Array.Empty<PdfIngestionFailure>(),
            "Partially completed");

        Assert.Equal(14, result.Discovered);
        Assert.Equal(5, result.IngestedOrLinked);
        Assert.Equal(7, result.AlreadyLinked);
        Assert.Equal(1, result.Missing);
        Assert.Equal(1, result.Failed);
    }
}

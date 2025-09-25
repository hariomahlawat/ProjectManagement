using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Data;

public static class StageFlowSeeder
{
    public static async Task SeedAsync(IServiceProvider sp)
    {
        const string version = "SDD-1.0";
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        if (await db.StageTemplates.AnyAsync(t => t.Version == version))
        {
            return;
        }

        var stages = new[]
        {
            new StageTemplate { Version = version, Code = "FEAS",  Name = "Feasibility Study",              Sequence = 10 },
            new StageTemplate { Version = version, Code = "IPA",   Name = "In-Principle Approval",          Sequence = 20 },
            new StageTemplate { Version = version, Code = "SOW",   Name = "Scope of Work Vetting",          Sequence = 30 },
            new StageTemplate { Version = version, Code = "AON",   Name = "Acceptance of Necessity",        Sequence = 40 },
            new StageTemplate { Version = version, Code = "BID",   Name = "Bid Upload",                     Sequence = 50 },
            new StageTemplate { Version = version, Code = "TEC",   Name = "Technical Evaluation Committee", Sequence = 60 },
            new StageTemplate { Version = version, Code = "BENCH", Name = "Benchmarking",                   Sequence = 65, ParallelGroup = "PRE_COB" },
            new StageTemplate { Version = version, Code = "COB",   Name = "Commercial Opening Board",       Sequence = 70 },
            new StageTemplate { Version = version, Code = "PNC",   Name = "Price Negotiation Committee",    Sequence = 80, Optional = true },
            new StageTemplate { Version = version, Code = "EAS",   Name = "Expenditure Angle Sanction",     Sequence = 90 },
            new StageTemplate { Version = version, Code = "SO",    Name = "Supply Order",                   Sequence = 100 },
            new StageTemplate { Version = version, Code = "DEV",   Name = "Development",                    Sequence = 110 },
            new StageTemplate { Version = version, Code = "AT",    Name = "Acceptance Testing",             Sequence = 120 },
            new StageTemplate { Version = version, Code = "PAY",   Name = "Payment",                        Sequence = 130 },
        };

        var deps = new[]
        {
            D("IPA", "FEAS"), D("SOW", "IPA"), D("AON", "SOW"), D("BID", "AON"),
            D("TEC", "BID"), D("BENCH", "BID"),
            D("COB", "TEC"), D("COB", "BENCH"),
            D("PNC", "COB"),
            D("EAS", "COB"),
            D("SO", "EAS"), D("DEV", "SO"), D("AT", "DEV"), D("PAY", "AT")
        };

        await db.StageTemplates.AddRangeAsync(stages);
        await db.StageDependencyTemplates.AddRangeAsync(deps);
        await db.SaveChangesAsync();

        StageDependencyTemplate D(string from, string on)
            => new() { Version = version, FromStageCode = from, DependsOnStageCode = on };
    }
}

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

        var templatesExist = await db.StageTemplates.AnyAsync(t => t.Version == version);
        var depsExist = await db.StageDependencyTemplates.AnyAsync(t => t.Version == version);

        if (templatesExist && depsExist)
        {
            return;
        }

        var stages = new[]
        {
            new StageTemplate { Version = version, Code = "FS",  Name = "Feasibility Study",              Sequence = 10 },
            new StageTemplate { Version = version, Code = "IPA",   Name = "In-Principle Approval",          Sequence = 20 },
            new StageTemplate { Version = version, Code = "SOW",   Name = "Scope of Work Vetting",          Sequence = 30 },
            new StageTemplate { Version = version, Code = "AON",   Name = "Acceptance of Necessity",        Sequence = 40 },
            new StageTemplate { Version = version, Code = "BID",   Name = "Bid Upload",                     Sequence = 50 },
            new StageTemplate { Version = version, Code = "TEC",   Name = "Technical Evaluation Committee", Sequence = 60 },
            new StageTemplate { Version = version, Code = "BM", Name = "Benchmarking",                   Sequence = 65, ParallelGroup = "PRE_COB" },
            new StageTemplate { Version = version, Code = "COB",   Name = "Commercial Opening Board",       Sequence = 70 },
            new StageTemplate { Version = version, Code = "PNC",   Name = "Price Negotiation Committee",    Sequence = 80, Optional = true },
            new StageTemplate { Version = version, Code = "EAS",   Name = "Expenditure Angle Sanction",     Sequence = 90 },
            new StageTemplate { Version = version, Code = "SO",    Name = "Supply Order",                   Sequence = 100 },
            new StageTemplate { Version = version, Code = "DEVP",   Name = "Development",                    Sequence = 110 },
            new StageTemplate { Version = version, Code = "ATC",    Name = "Acceptance Testing",             Sequence = 120 },
            new StageTemplate { Version = version, Code = "PAYMENT",   Name = "Payment",                        Sequence = 130 },
        };

        var deps = new[]
        {
            D("IPA", "FS"), D("SOW", "IPA"), D("AON", "SOW"), D("BID", "AON"),
            D("TEC", "BID"), D("BM", "BID"),
            D("COB", "TEC"), D("COB", "BM"),
            D("PNC", "COB"),
            D("EAS", "COB"), D("EAS", "PNC"),
            D("SO", "EAS"), D("DEVP", "SO"), D("ATP", "DEVP"), D("PAYMENT", "ATP")
        };

        var changesMade = false;

        if (!templatesExist)
        {
            await db.StageTemplates.AddRangeAsync(stages);
            changesMade = true;
        }

        if (!depsExist)
        {
            await db.StageDependencyTemplates.AddRangeAsync(deps);
            changesMade = true;
        }

        if (changesMade)
        {
            await db.SaveChangesAsync();
        }

        StageDependencyTemplate D(string from, string on)
            => new() { Version = version, FromStageCode = from, DependsOnStageCode = on };
    }
}

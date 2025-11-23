using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Models.Plans;
using ProjectManagement.Models.Stages;

namespace ProjectManagement.Data;

public static class StageFlowSeeder
{
    public static async Task SeedAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // SECTION: Stage Template Versions
        var changesMade = false;

        changesMade |= await SeedVersionAsync(db, PlanConstants.StageTemplateVersionV1, BuildV1Stages(), BuildV1Deps());
        changesMade |= await SeedVersionAsync(db, PlanConstants.StageTemplateVersionV2, BuildV2Stages(), BuildV2Deps());

        if (changesMade)
        {
            await db.SaveChangesAsync();
        }
    }

    private static async Task<bool> SeedVersionAsync(ApplicationDbContext db, string version, StageTemplate[] stages, StageDependencyTemplate[] deps)
    {
        // SECTION: Template Upsert
        var changesMade = false;

        var existingTemplates = await db.StageTemplates
            .Where(t => t.Version == version)
            .ToListAsync();

        foreach (var stage in stages)
        {
            var existing = existingTemplates
                .FirstOrDefault(t => string.Equals(t.Code, stage.Code, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                await db.StageTemplates.AddAsync(stage);
                changesMade = true;
                continue;
            }

            var needsUpdate = !string.Equals(existing.Name, stage.Name, StringComparison.Ordinal)
                || existing.Sequence != stage.Sequence
                || existing.Optional != stage.Optional
                || !string.Equals(existing.ParallelGroup, stage.ParallelGroup, StringComparison.Ordinal);

            if (needsUpdate)
            {
                existing.Name = stage.Name;
                existing.Sequence = stage.Sequence;
                existing.Optional = stage.Optional;
                existing.ParallelGroup = stage.ParallelGroup;
                changesMade = true;
            }
        }

        // SECTION: Dependency Upsert
        var existingDeps = await db.StageDependencyTemplates
            .Where(t => t.Version == version)
            .ToListAsync();

        var missingDeps = deps
            .Where(dep => !existingDeps.Any(existing =>
                string.Equals(existing.FromStageCode, dep.FromStageCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.DependsOnStageCode, dep.DependsOnStageCode, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (missingDeps.Length > 0)
        {
            await db.StageDependencyTemplates.AddRangeAsync(missingDeps);
            changesMade = true;
        }

        return changesMade;
    }

    private static StageTemplate[] BuildV1Stages()
    {
        var version = PlanConstants.StageTemplateVersionV1;

        return new[]
        {
            new StageTemplate { Version = version, Code = StageCodes.FS,  Name = "Feasibility Study",              Sequence = 10 },
            new StageTemplate { Version = version, Code = StageCodes.IPA,   Name = "In-Principle Approval",          Sequence = 20 },
            new StageTemplate { Version = version, Code = StageCodes.SOW,   Name = "Scope of Work Vetting",          Sequence = 30 },
            new StageTemplate { Version = version, Code = StageCodes.AON,   Name = "Acceptance of Necessity",        Sequence = 40 },
            new StageTemplate { Version = version, Code = StageCodes.BID,   Name = "Bid Upload",                     Sequence = 50 },
            new StageTemplate { Version = version, Code = StageCodes.TEC,   Name = "Technical Evaluation Committee", Sequence = 60 },
            new StageTemplate { Version = version, Code = StageCodes.BM, Name = "Benchmarking",                   Sequence = 65, ParallelGroup = "PRE_COB" },
            new StageTemplate { Version = version, Code = StageCodes.COB,   Name = "Commercial Opening Board",       Sequence = 70 },
            new StageTemplate { Version = version, Code = StageCodes.PNC,   Name = "Price Negotiation Committee",    Sequence = 80, Optional = true },
            new StageTemplate { Version = version, Code = StageCodes.EAS,   Name = "Expenditure Angle Sanction",     Sequence = 90 },
            new StageTemplate { Version = version, Code = StageCodes.SO,    Name = "Supply Order",                   Sequence = 100 },
            new StageTemplate { Version = version, Code = StageCodes.DEVP,   Name = "Development",                    Sequence = 110 },
            new StageTemplate { Version = version, Code = StageCodes.ATP,    Name = "Acceptance Testing",             Sequence = 120 },
            new StageTemplate { Version = version, Code = StageCodes.PAYMENT,   Name = "Payment",                        Sequence = 130 },
            new StageTemplate { Version = version, Code = StageCodes.TOT, Name = "Transfer of Technology", Sequence = 140, Optional = true },
        };
    }

    private static StageTemplate[] BuildV2Stages()
    {
        var version = PlanConstants.StageTemplateVersionV2;

        return new[]
        {
            new StageTemplate { Version = version, Code = StageCodes.FS,  Name = "Feasibility Study",        Sequence = 10 },
            new StageTemplate { Version = version, Code = StageCodes.SOW, Name = "Scope of Work Vetting",   Sequence = 20 },
            new StageTemplate { Version = version, Code = StageCodes.IPA, Name = "In-Principle Approval",  Sequence = 30 },
            new StageTemplate { Version = version, Code = StageCodes.AON, Name = "AoN / Sanction",         Sequence = 40 },
            new StageTemplate { Version = version, Code = StageCodes.BID, Name = "Bid Process",            Sequence = 50 },
            new StageTemplate { Version = version, Code = StageCodes.TEC, Name = "Technical Evaluation",   Sequence = 60 },
            new StageTemplate { Version = version, Code = StageCodes.BM,  Name = "Benchmarking",           Sequence = 70 },
            new StageTemplate { Version = version, Code = StageCodes.COB, Name = "Commercial Opening",     Sequence = 80 },
            new StageTemplate { Version = version, Code = StageCodes.PNC, Name = "Price Negotiation",      Sequence = 90, Optional = true },
            new StageTemplate { Version = version, Code = StageCodes.EAS, Name = "EAS / Approval",         Sequence = 100 },
            new StageTemplate { Version = version, Code = StageCodes.SO,  Name = "Supply Order",           Sequence = 110 },
            new StageTemplate { Version = version, Code = StageCodes.DEVP, Name = "Development",           Sequence = 115 },
            new StageTemplate { Version = version, Code = StageCodes.ATP, Name = "Acceptance Testing",     Sequence = 120 },
            new StageTemplate { Version = version, Code = StageCodes.PAYMENT, Name = "Payment",            Sequence = 130 },
            new StageTemplate { Version = version, Code = StageCodes.TOT, Name = "Transfer of Technology", Sequence = 140, Optional = true },
        };
    }

    private static StageDependencyTemplate[] BuildV1Deps()
    {
        var version = PlanConstants.StageTemplateVersionV1;

        return new[]
        {
            D(StageCodes.IPA, StageCodes.FS, version), D(StageCodes.SOW, StageCodes.IPA, version), D(StageCodes.AON, StageCodes.SOW, version), D(StageCodes.BID, StageCodes.AON, version),
            D(StageCodes.TEC, StageCodes.BID, version), D(StageCodes.BM, StageCodes.BID, version),
            D(StageCodes.COB, StageCodes.TEC, version), D(StageCodes.COB, StageCodes.BM, version),
            D(StageCodes.PNC, StageCodes.COB, version),
            D(StageCodes.EAS, StageCodes.COB, version), D(StageCodes.EAS, StageCodes.PNC, version),
            D(StageCodes.SO, StageCodes.EAS, version), D(StageCodes.DEVP, StageCodes.SO, version), D(StageCodes.ATP, StageCodes.DEVP, version), D(StageCodes.PAYMENT, StageCodes.ATP, version),
            D(StageCodes.TOT, StageCodes.PAYMENT, version)
        };
    }

    private static StageDependencyTemplate[] BuildV2Deps()
    {
        var version = PlanConstants.StageTemplateVersionV2;

        return new[]
        {
            D(StageCodes.SOW, StageCodes.FS, version),
            D(StageCodes.IPA, StageCodes.SOW, version),
            D(StageCodes.AON, StageCodes.IPA, version),
            D(StageCodes.BID, StageCodes.AON, version),
            D(StageCodes.TEC, StageCodes.BID, version),
            D(StageCodes.BM, StageCodes.BID, version),
            D(StageCodes.COB, StageCodes.TEC, version), D(StageCodes.COB, StageCodes.BM, version),
            D(StageCodes.PNC, StageCodes.COB, version),
            D(StageCodes.EAS, StageCodes.COB, version), D(StageCodes.EAS, StageCodes.PNC, version),
            D(StageCodes.SO, StageCodes.EAS, version),
            D(StageCodes.DEVP, StageCodes.SO, version),
            D(StageCodes.ATP, StageCodes.DEVP, version),
            D(StageCodes.PAYMENT, StageCodes.ATP, version),
            D(StageCodes.TOT, StageCodes.PAYMENT, version)
        };
    }

    private static StageDependencyTemplate D(string from, string on, string version)
        => new() { Version = version, FromStageCode = from, DependsOnStageCode = on };
}

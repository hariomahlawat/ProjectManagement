using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Models.Process;
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
        var processStagesExist = await db.ProcessStages.AnyAsync();
        var processEdgesExist = await db.ProcessStageEdges.AnyAsync();

        if (templatesExist && depsExist && processStagesExist && processEdgesExist)
        {
            return;
        }

        var stages = new[]
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
        };

        var deps = new[]
        {
            D(StageCodes.IPA, StageCodes.FS), D(StageCodes.SOW, StageCodes.IPA), D(StageCodes.AON, StageCodes.SOW), D(StageCodes.BID, StageCodes.AON),
            D(StageCodes.TEC, StageCodes.BID), D(StageCodes.BM, StageCodes.BID),
            D(StageCodes.COB, StageCodes.TEC), D(StageCodes.COB, StageCodes.BM),
            D(StageCodes.PNC, StageCodes.COB),
            D(StageCodes.EAS, StageCodes.COB), D(StageCodes.EAS, StageCodes.PNC),
            D(StageCodes.SO, StageCodes.EAS), D(StageCodes.DEVP, StageCodes.SO), D(StageCodes.ATP, StageCodes.DEVP), D(StageCodes.PAYMENT, StageCodes.ATP)
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

        if (!processStagesExist)
        {
            var procStages = new[]
            {
                new ProcessStage { Name = "Feasibility Study", Row = 0, Col = 0 },
                new ProcessStage { Name = "In-Principle Approval", Row = 0, Col = 1 },
                new ProcessStage { Name = "Scope of Work Vetting", Row = 0, Col = 2 },
                new ProcessStage { Name = "Acceptance of Necessity", Row = 0, Col = 3 },
                new ProcessStage { Name = "Bid Upload", Row = 1, Col = 0 },
                new ProcessStage { Name = "Technical Evaluation Committee", Row = 1, Col = 1 },
                new ProcessStage { Name = "Benchmarking", Row = 1, Col = 2 },
                new ProcessStage { Name = "Commercial Opening Board", Row = 1, Col = 3 },
                new ProcessStage { Name = "Price Negotiation Committee", Row = 2, Col = 1, IsOptional = true },
                new ProcessStage { Name = "Expenditure Angle Sanction", Row = 2, Col = 2 },
                new ProcessStage { Name = "Supply Order", Row = 3, Col = 1 },
                new ProcessStage { Name = "Development", Row = 3, Col = 2 },
                new ProcessStage { Name = "Acceptance Testing", Row = 4, Col = 1 },
                new ProcessStage { Name = "Payment", Row = 4, Col = 2 }
            };

            await db.ProcessStages.AddRangeAsync(procStages);
            changesMade = true;
            await db.SaveChangesAsync();
        }

        if (!processEdgesExist)
        {
            var stageLookup = await db.ProcessStages.AsNoTracking().ToDictionaryAsync(x => x.Name, x => x.Id);

            ProcessStageEdge Edge(string from, string to) => new()
            {
                FromStageId = stageLookup[from],
                ToStageId = stageLookup[to]
            };

            var procEdges = new[]
            {
                Edge("Feasibility Study", "In-Principle Approval"),
                Edge("In-Principle Approval", "Scope of Work Vetting"),
                Edge("Scope of Work Vetting", "Acceptance of Necessity"),
                Edge("Acceptance of Necessity", "Bid Upload"),
                Edge("Bid Upload", "Technical Evaluation Committee"),
                Edge("Bid Upload", "Benchmarking"),
                Edge("Technical Evaluation Committee", "Commercial Opening Board"),
                Edge("Benchmarking", "Commercial Opening Board"),
                Edge("Commercial Opening Board", "Price Negotiation Committee"),
                Edge("Commercial Opening Board", "Expenditure Angle Sanction"),
                Edge("Price Negotiation Committee", "Expenditure Angle Sanction"),
                Edge("Expenditure Angle Sanction", "Supply Order"),
                Edge("Supply Order", "Development"),
                Edge("Development", "Acceptance Testing"),
                Edge("Acceptance Testing", "Payment")
            };

            await db.ProcessStageEdges.AddRangeAsync(procEdges);
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Models.Execution;
using ProjectManagement.Models.Stages;
using ProjectManagement.Services;
using ProjectManagement.Services.Stages;

namespace ProjectManagement.Services.Projects
{
    public class ProjectFactsService
    {
        private readonly ApplicationDbContext _db;
        private readonly IClock _clock;
        private readonly IAuditService _audit;

        public ProjectFactsService(ApplicationDbContext db, IClock clock, IAuditService audit)
        {
            _db = db;
            _clock = clock;
            _audit = audit;
        }

        public async Task UpsertIpaCostAsync(int projectId, decimal ipaCost, string userId, CancellationToken ct = default)
        {
            var created = await UpsertMoneyFactAsync(
                _db.ProjectIpaFacts,
                projectId,
                userId,
                fact => fact.IpaCost = ipaCost,
                ct);

            await ClearBackfillFlagAsync(projectId, StageCodes.IPA, ct);

            await _audit.LogAsync(
                created ? "ProjectFacts.IpaCostCreated" : "ProjectFacts.IpaCostUpdated",
                userId: userId,
                data: new Dictionary<string, string?>
                {
                    ["ProjectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                    ["IpaCost"] = ipaCost.ToString(CultureInfo.InvariantCulture)
                });
        }

        public async Task UpsertSowSponsorsAsync(
            int projectId,
            string sponsoringUnit,
            string sponsoringLineDirectorate,
            string userId,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(sponsoringUnit);
            ArgumentNullException.ThrowIfNull(sponsoringLineDirectorate);

            var fact = await _db.ProjectSowFacts.SingleOrDefaultAsync(x => x.ProjectId == projectId, ct);
            var created = false;
            if (fact is null)
            {
                fact = new ProjectSowFact
                {
                    ProjectId = projectId,
                    SponsoringUnit = sponsoringUnit,
                    SponsoringLineDirectorate = sponsoringLineDirectorate,
                    CreatedByUserId = userId,
                    CreatedOnUtc = _clock.UtcNow.UtcDateTime
                };
                await _db.ProjectSowFacts.AddAsync(fact, ct);
                created = true;
            }
            else
            {
                fact.SponsoringUnit = sponsoringUnit;
                fact.SponsoringLineDirectorate = sponsoringLineDirectorate;
            }

            await _db.SaveChangesAsync(ct);

            await ClearBackfillFlagAsync(projectId, StageCodes.SOW, ct);

            await _audit.LogAsync(
                created ? "ProjectFacts.SowSponsorsCreated" : "ProjectFacts.SowSponsorsUpdated",
                userId: userId,
                data: new Dictionary<string, string?>
                {
                    ["ProjectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                    ["SponsoringUnit"] = sponsoringUnit,
                    ["SponsoringLineDirectorate"] = sponsoringLineDirectorate
                });
        }

        public async Task UpsertAonCostAsync(int projectId, decimal aonCost, string userId, CancellationToken ct = default)
        {
            var created = await UpsertMoneyFactAsync(
                _db.ProjectAonFacts,
                projectId,
                userId,
                fact => fact.AonCost = aonCost,
                ct);

            await ClearBackfillFlagAsync(projectId, StageCodes.AON, ct);

            await _audit.LogAsync(
                created ? "ProjectFacts.AonCostCreated" : "ProjectFacts.AonCostUpdated",
                userId: userId,
                data: new Dictionary<string, string?>
                {
                    ["ProjectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                    ["AonCost"] = aonCost.ToString(CultureInfo.InvariantCulture)
                });
        }

        public async Task UpsertBenchmarkCostAsync(int projectId, decimal benchmarkCost, string userId, CancellationToken ct = default)
        {
            var created = await UpsertMoneyFactAsync(
                _db.ProjectBenchmarkFacts,
                projectId,
                userId,
                fact => fact.BenchmarkCost = benchmarkCost,
                ct);

            await ClearBackfillFlagAsync(projectId, StageCodes.BM, ct);

            await _audit.LogAsync(
                created ? "ProjectFacts.BenchmarkCostCreated" : "ProjectFacts.BenchmarkCostUpdated",
                userId: userId,
                data: new Dictionary<string, string?>
                {
                    ["ProjectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                    ["BenchmarkCost"] = benchmarkCost.ToString(CultureInfo.InvariantCulture)
                });
        }

        public async Task UpsertL1CostAsync(int projectId, decimal l1Cost, string userId, CancellationToken ct = default)
        {
            var created = await UpsertMoneyFactAsync(
                _db.ProjectCommercialFacts,
                projectId,
                userId,
                fact => fact.L1Cost = l1Cost,
                ct);

            await ClearBackfillFlagAsync(projectId, StageCodes.COB, ct);

            await _audit.LogAsync(
                created ? "ProjectFacts.L1CostCreated" : "ProjectFacts.L1CostUpdated",
                userId: userId,
                data: new Dictionary<string, string?>
                {
                    ["ProjectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                    ["L1Cost"] = l1Cost.ToString(CultureInfo.InvariantCulture)
                });
        }

        public async Task UpsertPncCostAsync(int projectId, decimal pncCost, string userId, CancellationToken ct = default)
        {
            var created = await UpsertMoneyFactAsync(
                _db.ProjectPncFacts,
                projectId,
                userId,
                fact => fact.PncCost = pncCost,
                ct);

            await ClearBackfillFlagAsync(projectId, StageCodes.PNC, ct);

            await _audit.LogAsync(
                created ? "ProjectFacts.PncCostCreated" : "ProjectFacts.PncCostUpdated",
                userId: userId,
                data: new Dictionary<string, string?>
                {
                    ["ProjectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                    ["PncCost"] = pncCost.ToString(CultureInfo.InvariantCulture)
                });
        }

        public async Task UpsertSupplyOrderDateAsync(int projectId, DateOnly supplyOrderDate, string userId, CancellationToken ct = default)
        {
            var fact = await _db.ProjectSupplyOrderFacts.SingleOrDefaultAsync(x => x.ProjectId == projectId, ct);
            var created = false;
            if (fact is null)
            {
                fact = new ProjectSupplyOrderFact
                {
                    ProjectId = projectId,
                    SupplyOrderDate = supplyOrderDate,
                    CreatedByUserId = userId,
                    CreatedOnUtc = _clock.UtcNow.UtcDateTime
                };
                await _db.ProjectSupplyOrderFacts.AddAsync(fact, ct);
                created = true;
            }
            else
            {
                fact.SupplyOrderDate = supplyOrderDate;
            }

            await _db.SaveChangesAsync(ct);

            await ClearBackfillFlagAsync(projectId, StageCodes.SO, ct);

            await _audit.LogAsync(
                created ? "ProjectFacts.SupplyOrderDateCreated" : "ProjectFacts.SupplyOrderDateUpdated",
                userId: userId,
                data: new Dictionary<string, string?>
                {
                    ["ProjectId"] = projectId.ToString(CultureInfo.InvariantCulture),
                    ["SupplyOrderDate"] = supplyOrderDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                });
        }

        private async Task<bool> UpsertMoneyFactAsync<TFact>(DbSet<TFact> set, int projectId, string userId, Action<TFact> update, CancellationToken ct)
            where TFact : ProjectFactBase, new()
        {
            var fact = await set.SingleOrDefaultAsync(x => x.ProjectId == projectId, ct);
            var created = false;
            if (fact is null)
            {
                fact = new TFact
                {
                    ProjectId = projectId,
                    CreatedByUserId = userId,
                    CreatedOnUtc = _clock.UtcNow.UtcDateTime
                };
                update(fact);
                await set.AddAsync(fact, ct);
                created = true;
            }
            else
            {
                update(fact);
            }

            await _db.SaveChangesAsync(ct);
            return created;
        }

        private async Task ClearBackfillFlagAsync(int projectId, string stageCode, CancellationToken ct)
        {
            var stage = await _db.ProjectStages.SingleOrDefaultAsync(
                s => s.ProjectId == projectId && s.StageCode == stageCode,
                ct);

            if (stage is null || !stage.RequiresBackfill)
            {
                return;
            }

            if (stage.Status == StageStatus.Completed && StageBackfillRules.IsMissingRequiredDates(stage))
            {
                return;
            }

            stage.RequiresBackfill = false;
            await _db.SaveChangesAsync(ct);
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Services.Projects
{
    public class ProjectFactsService
    {
        private readonly ApplicationDbContext _db;
        private readonly IClock _clock;

        public ProjectFactsService(ApplicationDbContext db, IClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task UpsertIpaCostAsync(int projectId, decimal ipaCost, string userId, CancellationToken ct = default)
        {
            await UpsertMoneyFactAsync(
                _db.ProjectIpaFacts,
                projectId,
                userId,
                fact => fact.IpaCost = ipaCost,
                ct);
        }

        public async Task UpsertSowSponsorsAsync(int projectId, string sponsoringUnit, string sponsoringLineDirectorate, string userId, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(sponsoringUnit);
            ArgumentNullException.ThrowIfNull(sponsoringLineDirectorate);

            var fact = await _db.ProjectSowFacts.SingleOrDefaultAsync(x => x.ProjectId == projectId, ct);
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
            }
            else
            {
                fact.SponsoringUnit = sponsoringUnit;
                fact.SponsoringLineDirectorate = sponsoringLineDirectorate;
            }

            await _db.SaveChangesAsync(ct);
        }

        public async Task UpsertAonCostAsync(int projectId, decimal aonCost, string userId, CancellationToken ct = default)
        {
            await UpsertMoneyFactAsync(
                _db.ProjectAonFacts,
                projectId,
                userId,
                fact => fact.AonCost = aonCost,
                ct);
        }

        public async Task UpsertBenchmarkCostAsync(int projectId, decimal benchmarkCost, string userId, CancellationToken ct = default)
        {
            await UpsertMoneyFactAsync(
                _db.ProjectBenchmarkFacts,
                projectId,
                userId,
                fact => fact.BenchmarkCost = benchmarkCost,
                ct);
        }

        public async Task UpsertL1CostAsync(int projectId, decimal l1Cost, string userId, CancellationToken ct = default)
        {
            await UpsertMoneyFactAsync(
                _db.ProjectCommercialFacts,
                projectId,
                userId,
                fact => fact.L1Cost = l1Cost,
                ct);
        }

        public async Task UpsertPncCostAsync(int projectId, decimal pncCost, string userId, CancellationToken ct = default)
        {
            await UpsertMoneyFactAsync(
                _db.ProjectPncFacts,
                projectId,
                userId,
                fact => fact.PncCost = pncCost,
                ct);
        }

        public async Task UpsertSupplyOrderDateAsync(int projectId, DateOnly supplyOrderDate, string userId, CancellationToken ct = default)
        {
            var fact = await _db.ProjectSupplyOrderFacts.SingleOrDefaultAsync(x => x.ProjectId == projectId, ct);
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
            }
            else
            {
                fact.SupplyOrderDate = supplyOrderDate;
            }

            await _db.SaveChangesAsync(ct);
        }

        private async Task UpsertMoneyFactAsync<TFact>(DbSet<TFact> set, int projectId, string userId, Action<TFact> update, CancellationToken ct)
            where TFact : ProjectFactBase, new()
        {
            var fact = await set.SingleOrDefaultAsync(x => x.ProjectId == projectId, ct);
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
            }
            else
            {
                update(fact);
            }

            await _db.SaveChangesAsync(ct);
        }
    }
}

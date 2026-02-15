using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProliferationManageService
{
    private readonly ApplicationDbContext _db;
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public ProliferationManageService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<ProliferationListBootVm> GetListBootAsync(
        int? projectId,
        ProliferationSource? source,
        int? year,
        ProliferationRecordKind? kind,
        CancellationToken ct)
    {
        var projects = await GetCompletedProjectsAsync(ct);
        var currentYear = DateTime.UtcNow.Year;
        var defaults = NormalizeDefaults(projectId, source, year, kind);
        return new ProliferationListBootVm(projects, GetSourceOptions(), DefaultPageSize, currentYear, defaults);
    }

    public async Task<ProliferationEditorBootVm> GetEditorBootAsync(
        int? projectId,
        ProliferationSource? source,
        int? year,
        ProliferationRecordKind? kind,
        CancellationToken ct)
    {
        var projects = await GetCompletedProjectsAsync(ct);
        var currentYear = DateTime.UtcNow.Year;
        var defaults = NormalizeDefaults(projectId, source, year, kind);
        return new ProliferationEditorBootVm(projects, GetSourceOptions(), currentYear, defaults);
    }

    public async Task<ProliferationPreferenceOverridesBootVm> GetPreferenceOverridesBootAsync(
        int? projectId,
        ProliferationSource? source,
        int? year,
        ProliferationRecordKind? kind,
        CancellationToken ct)
    {
        var projects = await GetCompletedProjectsAsync(ct);
        var currentYear = DateTime.UtcNow.Year;
        var defaults = NormalizeDefaults(projectId, source, year, kind);
        return new ProliferationPreferenceOverridesBootVm(projects, GetSourceOptions(), currentYear, defaults);
    }

    private static ProliferationManageBootDefaults NormalizeDefaults(
        int? projectId,
        ProliferationSource? source,
        int? year,
        ProliferationRecordKind? kind)
    {
        var normalizedProjectId = projectId.HasValue && projectId.Value > 0 ? projectId : null;
        var normalizedSource = source.HasValue && Enum.IsDefined(typeof(ProliferationSource), source.Value)
            ? source
            : null;
        var normalizedYear = year is >= 2000 and <= 3000 ? year : null;
        var normalizedKind = kind is ProliferationRecordKind.Yearly or ProliferationRecordKind.Granular
            ? kind
            : null;

        return new ProliferationManageBootDefaults(
            normalizedProjectId,
            normalizedSource,
            normalizedYear,
            normalizedKind);
    }

    public async Task<PagedResult<ProliferationManageListItem>> GetListAsync(
        ProliferationManageListRequest request,
        CancellationToken ct)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? DefaultPageSize : Math.Min(request.PageSize, MaxPageSize);

        var projects = _db.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted && !p.IsArchived);

        var yearlyQuery = from y in _db.ProliferationYearlies.AsNoTracking()
                          join p in projects on y.ProjectId equals p.Id
                          select new ManageProjection
                          {
                              Id = y.Id,
                              Kind = ProliferationRecordKind.Yearly,
                              ProjectId = y.ProjectId,
                              ProjectName = p.Name,
                              ProjectCode = p.CaseFileNumber,
                              Source = y.Source,
                              UnitName = null,
                              Year = y.Year,
                              ProliferationDate = null,
                              Quantity = y.TotalQuantity,
                              ApprovalStatus = y.ApprovalStatus,
                              CreatedOnUtc = y.CreatedOnUtc,
                              LastUpdatedOnUtc = y.LastUpdatedOnUtc,
                              ApprovedOnUtc = y.ApprovedOnUtc
                          };

        var granularQuery = from g in _db.ProliferationGranularEntries.AsNoTracking()
                            join p in projects on g.ProjectId equals p.Id
                            select new ManageProjection
                            {
                                Id = g.Id,
                                Kind = ProliferationRecordKind.Granular,
                                ProjectId = g.ProjectId,
                                ProjectName = p.Name,
                                ProjectCode = p.CaseFileNumber,
                                Source = g.Source,
                                UnitName = g.UnitName,
                                Year = g.ProliferationDate.Year,
                                ProliferationDate = g.ProliferationDate,
                                Quantity = g.Quantity,
                                ApprovalStatus = g.ApprovalStatus,
                                CreatedOnUtc = g.CreatedOnUtc,
                                LastUpdatedOnUtc = g.LastUpdatedOnUtc,
                                ApprovedOnUtc = g.ApprovedOnUtc
                            };

        if (request.ProjectId.HasValue)
        {
            var projectId = request.ProjectId.Value;
            yearlyQuery = yearlyQuery.Where(x => x.ProjectId == projectId);
            granularQuery = granularQuery.Where(x => x.ProjectId == projectId);
        }

        if (request.Source.HasValue)
        {
            var source = request.Source.Value;
            yearlyQuery = yearlyQuery.Where(x => x.Source == source);
            granularQuery = granularQuery.Where(x => x.Source == source);
        }

        if (request.Year.HasValue)
        {
            var year = request.Year.Value;
            yearlyQuery = yearlyQuery.Where(x => x.Year == year);
            granularQuery = granularQuery.Where(x => x.Year == year);
        }

        // SECTION: Approval status filter
        if (request.ApprovalStatus.HasValue)
        {
            var status = request.ApprovalStatus.Value;
            yearlyQuery = yearlyQuery.Where(x => x.ApprovalStatus == status);
            granularQuery = granularQuery.Where(x => x.ApprovalStatus == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var trimmed = request.Search.Trim();
            var like = $"%{trimmed}%";
            yearlyQuery = yearlyQuery.Where(x =>
                EF.Functions.ILike(x.ProjectName, like) ||
                (x.ProjectCode != null && EF.Functions.ILike(x.ProjectCode, like)));
            granularQuery = granularQuery.Where(x =>
                EF.Functions.ILike(x.ProjectName, like) ||
                (x.ProjectCode != null && EF.Functions.ILike(x.ProjectCode, like)) ||
                (x.UnitName != null && EF.Functions.ILike(x.UnitName, like)));
        }

        IQueryable<ManageProjection> combined = request.Kind switch
        {
            ProliferationRecordKind.Yearly => yearlyQuery,
            ProliferationRecordKind.Granular => granularQuery,
            _ => yearlyQuery.Concat(granularQuery)
        };

        var total = await combined.CountAsync(ct);
        var skip = (page - 1) * pageSize;

        var results = await combined
            .OrderByDescending(x => x.ProliferationDate != null)
            .ThenByDescending(x => x.ProliferationDate)
            .ThenByDescending(x => x.Year)
            .ThenByDescending(x => x.LastUpdatedOnUtc)
            .ThenBy(x => x.ProjectName)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = results
            .Select(x => new ProliferationManageListItem(
                x.Id,
                x.Kind,
                x.ProjectId,
                x.ProjectName,
                x.ProjectCode,
                x.Source,
                x.Source.ToDisplayName(),
                x.UnitName,
                x.Year,
                x.ProliferationDate,
                x.Quantity,
                x.ApprovalStatus,
                x.CreatedOnUtc,
                x.LastUpdatedOnUtc,
                x.ApprovedOnUtc))
            .ToList();

        return new PagedResult<ProliferationManageListItem>(items, total, page, pageSize);
    }

    public async Task<ProliferationYearlyDetail?> GetYearlyAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.ProliferationYearlies.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (entity is null)
        {
            return null;
        }

        await EnsureRowVersionAsync(entity, ct);

        return new ProliferationYearlyDetail(
            entity.Id,
            entity.ProjectId,
            entity.Source,
            entity.Year,
            entity.TotalQuantity,
            entity.Remarks,
            entity.ApprovalStatus,
            entity.SubmittedByUserId,
            entity.ApprovedByUserId,
            entity.ApprovedOnUtc,
            entity.CreatedOnUtc,
            entity.LastUpdatedOnUtc,
            entity.RowVersion);
    }

    public async Task<ProliferationGranularDetail?> GetGranularAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.ProliferationGranularEntries.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (entity is null)
        {
            return null;
        }

        await EnsureRowVersionAsync(entity, ct);

        return new ProliferationGranularDetail(
            entity.Id,
            entity.ProjectId,
            entity.Source,
            entity.ProliferationDate,
            entity.UnitName,
            entity.Quantity,
            entity.Remarks,
            entity.ApprovalStatus,
            entity.SubmittedByUserId,
            entity.ApprovedByUserId,
            entity.ApprovedOnUtc,
            entity.CreatedOnUtc,
            entity.LastUpdatedOnUtc,
            entity.RowVersion);
    }

    private async Task<IReadOnlyList<ProliferationCompletedProjectOption>> GetCompletedProjectsAsync(CancellationToken ct)
    {
        // SECTION: Completed non-build projects for Proliferation manager dropdown
        var projects = await _db.Projects
            .AsNoTracking()
            .Where(p =>
                !p.IsDeleted &&
                !p.IsArchived &&
                p.LifecycleStatus == ProjectLifecycleStatus.Completed &&
                !p.IsBuild)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        return projects
            .Select(p => new ProliferationCompletedProjectOption(p.Id, p.BuildDisplayName()))
            .ToList();
    }

    private async Task EnsureRowVersionAsync<TEntity>(TEntity entity, CancellationToken ct) where TEntity : class
    {
        var entry = _db.Entry(entity);
        var property = entry.Property<byte[]>(nameof(Project.RowVersion));

        if (property.CurrentValue is { Length: > 0 })
        {
            return;
        }

        property.CurrentValue = Guid.NewGuid().ToByteArray();
        property.IsModified = true;

        await _db.SaveChangesAsync(ct);
        await entry.ReloadAsync(ct);
    }

    private static IReadOnlyList<ProliferationSourceOptionVm> GetSourceOptions()
    {
        return Enum.GetValues<ProliferationSource>()
            .Select(s => new ProliferationSourceOptionVm((int)s, s.ToDisplayName()))
            .ToList();
    }

    private sealed class ManageProjection
    {
        public Guid Id { get; init; }
        public ProliferationRecordKind Kind { get; init; }
        public int ProjectId { get; init; }
        public string ProjectName { get; init; } = default!;
        public string? ProjectCode { get; init; }
        public ProliferationSource Source { get; init; }
        public string? UnitName { get; init; }
        public int Year { get; init; }
        public DateOnly? ProliferationDate { get; init; }
        public int Quantity { get; init; }
        public ApprovalStatus ApprovalStatus { get; init; }
        public DateTime CreatedOnUtc { get; init; }
        public DateTime LastUpdatedOnUtc { get; init; }
        public DateTime? ApprovedOnUtc { get; init; }
    }
}

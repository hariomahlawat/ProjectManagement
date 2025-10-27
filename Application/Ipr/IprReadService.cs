using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Application.Ipr;

public sealed class IprReadService : IIprReadService
{
    private readonly ApplicationDbContext _db;

    public IprReadService(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<PagedResult<IprListRowDto>> SearchAsync(IprFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var page = filter.Page;
        var pageSize = filter.PageSize;

        var query = BuildFilteredQuery(_db.IprRecords.AsNoTracking(), filter);
        var total = await query.CountAsync(cancellationToken);

        var queryResults = await query
            .OrderByDescending(x => x.FiledAtUtc ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.IprFilingNumber,
                x.Title,
                x.Type,
                x.Status,
                x.FiledAtUtc,
                x.FiledBy,
                x.GrantedAtUtc,
                x.ProjectId,
                ProjectName = x.Project != null ? x.Project.Name : null,
                Attachments = x.Attachments
                    .Where(a => !a.IsArchived)
                    .OrderByDescending(a => a.UploadedAtUtc)
                    .Select(a => new
                    {
                        a.Id,
                        a.OriginalFileName,
                        a.FileSize,
                        a.UploadedAtUtc,
                        UploadedByFullName = a.UploadedByUser != null ? a.UploadedByUser.FullName : null,
                        UploadedByUserName = a.UploadedByUser != null ? a.UploadedByUser.UserName : null,
                        a.UploadedByUserId
                    })
                    .ToList(),
                x.Notes
            })
            .ToListAsync(cancellationToken);

        var items = queryResults
            .Select(x => new IprListRowDto(
                x.Id,
                x.IprFilingNumber,
                x.Title,
                x.Type,
                x.Status,
                x.FiledAtUtc,
                x.FiledBy,
                x.GrantedAtUtc,
                x.ProjectId,
                x.ProjectName,
                x.Attachments.Count,
                x.Attachments
                    .Select(a => new IprListAttachmentDto(
                        a.Id,
                        a.OriginalFileName,
                        a.FileSize,
                        FormatUploadedBy(a.UploadedByFullName, a.UploadedByUserName, a.UploadedByUserId),
                        a.UploadedAtUtc))
                    .ToList(),
                x.Notes))
            .ToList();

        return new PagedResult<IprListRowDto>(items, total, page, pageSize);
    }

    public Task<IprRecord?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        return _db.IprRecords
            .AsNoTracking()
            .Include(x => x.Project)
            .Include(x => x.Attachments)
            .ThenInclude(x => x.UploadedByUser)
            .Include(x => x.Attachments)
            .ThenInclude(x => x.ArchivedByUser)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IprKpis> GetKpisAsync(IprFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var baseQuery = BuildFilteredQuery(_db.IprRecords.AsNoTracking(), filter);
        var groups = await baseQuery
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var total = groups.Sum(g => g.Count);
        var filingUnderProcess = groups.FirstOrDefault(g => g.Status == IprStatus.FilingUnderProcess)?.Count ?? 0;
        var filed = groups.FirstOrDefault(g => g.Status == IprStatus.Filed)?.Count ?? 0;
        var granted = groups.FirstOrDefault(g => g.Status == IprStatus.Granted)?.Count ?? 0;
        var rejected = groups.FirstOrDefault(g => g.Status == IprStatus.Rejected)?.Count ?? 0;
        var withdrawn = groups.FirstOrDefault(g => g.Status == IprStatus.Withdrawn)?.Count ?? 0;

        return new IprKpis(total, filingUnderProcess, filed, granted, rejected, withdrawn);
    }

    public async Task<IReadOnlyList<IprExportRowDto>> GetExportAsync(IprFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = BuildFilteredQuery(_db.IprRecords.AsNoTracking(), filter);

        var queryResults = await query
            .OrderByDescending(x => x.FiledAtUtc ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.IprFilingNumber,
                x.Title,
                x.Status,
                x.FiledBy,
                x.FiledAtUtc,
                x.GrantedAtUtc,
                ProjectName = x.Project != null ? x.Project.Name : null,
                x.Notes,
                Attachments = x.Attachments
                    .Where(a => !a.IsArchived)
                    .OrderByDescending(a => a.UploadedAtUtc)
                    .Select(a => new
                    {
                        a.Id,
                        a.OriginalFileName,
                        a.ContentType,
                        a.FileSize,
                        a.UploadedAtUtc,
                        UploadedByFullName = a.UploadedByUser != null ? a.UploadedByUser.FullName : null,
                        UploadedByUserName = a.UploadedByUser != null ? a.UploadedByUser.UserName : null,
                        a.UploadedByUserId
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var items = queryResults
            .Select(x => new IprExportRowDto(
                x.Id,
                x.IprFilingNumber,
                x.Title,
                x.Status,
                x.FiledBy,
                x.FiledAtUtc,
                x.GrantedAtUtc,
                x.ProjectName,
                x.Notes,
                x.Attachments
                    .Select(a => new IprExportAttachmentDto(
                        a.Id,
                        a.OriginalFileName,
                        a.ContentType,
                        a.FileSize,
                        FormatUploadedBy(a.UploadedByFullName, a.UploadedByUserName, a.UploadedByUserId),
                        a.UploadedAtUtc))
                    .ToList()))
            .ToList();

        return items;
    }

    private static string FormatUploadedBy(string? fullName, string? userName, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName;
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            return userName;
        }

        return fallback;
    }

    private static IQueryable<IprRecord> BuildFilteredQuery(IQueryable<IprRecord> query, IprFilter filter, bool includeStatusFilter = true)
    {
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            var trimmed = filter.Query.Trim();
            query = query.Where(x =>
                EF.Functions.ILike(x.IprFilingNumber, $"%{trimmed}%") ||
                (x.Title != null && EF.Functions.ILike(x.Title, $"%{trimmed}%")));
        }

        if (filter.Types is { Count: > 0 })
        {
            query = query.Where(x => filter.Types!.Contains(x.Type));
        }

        if (includeStatusFilter && filter.Statuses is { Count: > 0 })
        {
            query = query.Where(x => filter.Statuses!.Contains(x.Status));
        }

        if (filter.ProjectId.HasValue)
        {
            query = query.Where(x => x.ProjectId == filter.ProjectId);
        }

        if (filter.FiledFrom.HasValue)
        {
            var from = filter.FiledFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var fromOffset = new DateTimeOffset(from);
            query = query.Where(x => x.FiledAtUtc >= fromOffset);
        }

        if (filter.FiledTo.HasValue)
        {
            var to = filter.FiledTo.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            var toOffset = new DateTimeOffset(to);
            query = query.Where(x => x.FiledAtUtc <= toOffset);
        }

        return query;
    }
}

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

        var query = IprQueryFilter.Apply(_db.IprRecords.AsNoTracking(), filter);
        var total = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var queryResults = await IprQueryFilter.ApplyRegisterOrdering(query)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.IprFilingNumber,
                x.Title,
                x.Type,
                Status = x.Status == IprStatus.FilingUnderProcess ? IprStatus.Filed : x.Status,
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
                x.Status == IprStatus.FilingUnderProcess ? IprStatus.Filed : x.Status,
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

    public async Task<int?> GetPageNumberForRecordAsync(
        IprFilter filter,
        int recordId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (recordId <= 0)
        {
            return null;
        }

        var query = IprQueryFilter.Apply(_db.IprRecords.AsNoTracking(), filter);
        var target = await query
            .Where(record => record.Id == recordId)
            .Select(record => new
            {
                record.Id,
                SortDate = record.FiledAtUtc ?? DateTimeOffset.MinValue
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (target is null)
        {
            return null;
        }

        var precedingCount = await query.CountAsync(record =>
            (record.FiledAtUtc ?? DateTimeOffset.MinValue) > target.SortDate ||
            ((record.FiledAtUtc ?? DateTimeOffset.MinValue) == target.SortDate && record.Id < target.Id),
            cancellationToken);

        return (precedingCount / filter.PageSize) + 1;
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

        var baseQuery = IprQueryFilter.Apply(_db.IprRecords.AsNoTracking(), filter);
        var groups = await baseQuery
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var legacyFiled = groups.FirstOrDefault(g => g.Status == IprStatus.FilingUnderProcess)?.Count ?? 0;
        var awaitingGrant = legacyFiled + (groups.FirstOrDefault(g => g.Status == IprStatus.Filed)?.Count ?? 0);
        var granted = groups.FirstOrDefault(g => g.Status == IprStatus.Granted)?.Count ?? 0;
        var totalFiled = awaitingGrant + granted;

        return new IprKpis(totalFiled, 0, awaitingGrant, granted, 0, 0);
    }

    public async Task<IReadOnlyList<IprExportRowDto>> GetExportAsync(IprFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = IprQueryFilter.Apply(_db.IprRecords.AsNoTracking(), filter);

        var items = await IprQueryFilter.ApplyRegisterOrdering(query)
            .Select(x => new IprExportRowDto(
                x.IprFilingNumber,
                x.Title,
                x.Status == IprStatus.FilingUnderProcess ? IprStatus.Filed : x.Status,
                x.FiledBy,
                x.FiledAtUtc,
                x.GrantedAtUtc,
                x.Project != null ? x.Project.Name : null,
                x.Notes,
                x.Type))
            .ToListAsync(cancellationToken);

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
}

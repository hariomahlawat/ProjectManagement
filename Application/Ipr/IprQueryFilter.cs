using System;
using System.Linq;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Application.Ipr;

public static class IprQueryFilter
{
    public static IQueryable<IprRecord> Apply(
        IQueryable<IprRecord> query,
        IprFilter filter,
        bool includeSearch = true,
        bool includeStatus = true)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(filter);

        query = query.Where(record =>
            record.Status == IprStatus.FilingUnderProcess ||
            record.Status == IprStatus.Filed ||
            record.Status == IprStatus.Granted);

        if (includeSearch && !string.IsNullOrWhiteSpace(filter.Query))
        {
            var term = filter.Query.Trim().ToLowerInvariant();
            query = query.Where(record =>
                (!string.IsNullOrEmpty(record.IprFilingNumber) && record.IprFilingNumber.ToLower().Contains(term)) ||
                (!string.IsNullOrEmpty(record.Title) && record.Title!.ToLower().Contains(term)) ||
                (record.Project != null && record.Project.Name.ToLower().Contains(term)));
        }

        if (filter.Types is { Count: > 0 })
        {
            query = query.Where(record => filter.Types!.Contains(record.Type));
        }

        if (includeStatus && filter.Statuses is { Count: > 0 })
        {
            var wantsPending = filter.Statuses.Contains(IprStatus.Filed);
            var wantsProtected = filter.Statuses.Contains(IprStatus.Granted);
            query = query.Where(record =>
                (wantsPending && (record.Status == IprStatus.FilingUnderProcess || record.Status == IprStatus.Filed)) ||
                (wantsProtected && record.Status == IprStatus.Granted));
        }

        if (filter.ProjectId.HasValue)
        {
            query = query.Where(record => record.ProjectId == filter.ProjectId.Value);
        }
        else
        {
            query = filter.Linkage switch
            {
                IprLinkageFilter.Linked => query.Where(record => record.ProjectId.HasValue),
                IprLinkageFilter.Unassigned => query.Where(record => !record.ProjectId.HasValue),
                _ => query
            };
        }

        query = filter.Evidence switch
        {
            IprEvidenceFilter.Available => query.Where(record => record.Attachments.Any(attachment => !attachment.IsArchived)),
            IprEvidenceFilter.Missing => query.Where(record => !record.Attachments.Any(attachment => !attachment.IsArchived)),
            _ => query
        };

        if (filter.Year.HasValue)
        {
            var from = new DateOnly(filter.Year.Value, 1, 1);
            var to = new DateOnly(filter.Year.Value, 12, 31);
            if (filter.DateBasis == IprDateBasis.Protected)
            {
                query = ApplyProtectedRange(query, from, to);
            }
            else
            {
                query = ApplyFiledRange(query, from, to);
            }
        }

        query = ApplyFiledRange(query, filter.FiledFrom, filter.FiledTo);
        query = ApplyProtectedRange(query, filter.ProtectedFrom, filter.ProtectedTo);
        return query;
    }

    public static IOrderedQueryable<IprRecord> ApplyRegisterOrdering(IQueryable<IprRecord> query)
        => query
            .OrderByDescending(record => record.FiledAtUtc ?? DateTimeOffset.MinValue)
            .ThenBy(record => record.Id);

    private static IQueryable<IprRecord> ApplyFiledRange(
        IQueryable<IprRecord> query,
        DateOnly? from,
        DateOnly? to)
    {
        if (from.HasValue)
        {
            var value = new DateTimeOffset(from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            query = query.Where(record => record.FiledAtUtc >= value);
        }

        if (to.HasValue)
        {
            var value = new DateTimeOffset(to.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));
            query = query.Where(record => record.FiledAtUtc <= value);
        }

        return query;
    }

    private static IQueryable<IprRecord> ApplyProtectedRange(
        IQueryable<IprRecord> query,
        DateOnly? from,
        DateOnly? to)
    {
        if (from.HasValue)
        {
            var value = new DateTimeOffset(from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            query = query.Where(record => record.GrantedAtUtc >= value);
        }

        if (to.HasValue)
        {
            var value = new DateTimeOffset(to.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));
            query = query.Where(record => record.GrantedAtUtc <= value);
        }

        return query;
    }
}

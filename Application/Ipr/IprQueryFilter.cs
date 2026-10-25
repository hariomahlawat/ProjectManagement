using System;
using System.Collections.Generic;
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

        if (includeStatus)
        {
            query = ApplyStatus(query, filter.Statuses);
        }

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

        query = ApplyPendingAge(query, filter.PendingAge, filter.AsOf);
        query = ApplyFiledRange(query, filter.FiledFrom, filter.FiledTo);
        query = ApplyProtectedRange(query, filter.ProtectedFrom, filter.ProtectedTo);
        return query;
    }

    // With no status selected the register shows the positive count (Filed and Granted).
    // Closed records (Rejected, Withdrawn) appear only when their status is selected.
    private static IQueryable<IprRecord> ApplyStatus(
        IQueryable<IprRecord> query,
        IReadOnlyCollection<IprStatus>? statuses)
    {
        if (statuses is not { Count: > 0 })
        {
            return query.Where(IprStatusPolicy.CountedPredicate);
        }

        var wantsPending = statuses.Contains(IprStatus.Filed) || statuses.Contains(IprStatus.FilingUnderProcess);
        var wantsProtected = statuses.Contains(IprStatus.Granted);
        var wantsRejected = statuses.Contains(IprStatus.Rejected);
        var wantsWithdrawn = statuses.Contains(IprStatus.Withdrawn);
        return query.Where(record =>
            (wantsPending && (record.Status == IprStatus.FilingUnderProcess || record.Status == IprStatus.Filed)) ||
            (wantsProtected && record.Status == IprStatus.Granted) ||
            (wantsRejected && record.Status == IprStatus.Rejected) ||
            (wantsWithdrawn && record.Status == IprStatus.Withdrawn));
    }

    /// <summary>Day thresholds of the pending-age bands, shared with the Insights age chart.</summary>
    public static (int MinDays, int? MaxDays) PendingAgeRange(IprPendingAge band) => band switch
    {
        IprPendingAge.UnderOneYear => (0, 365),
        IprPendingAge.OneToThreeYears => (365, 1095),
        IprPendingAge.ThreeToFiveYears => (1095, 1825),
        IprPendingAge.OverFiveYears => (1825, null),
        _ => (0, null)
    };

    private static IQueryable<IprRecord> ApplyPendingAge(IQueryable<IprRecord> query, IprPendingAge band, DateOnly? asOf)
    {
        if (band == IprPendingAge.Any)
        {
            return query;
        }

        var today = asOf ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ProjectManagement.Utilities.TimeZoneHelper.GetIst()).DateTime);
        var (minDays, maxDays) = PendingAgeRange(band);
        // Filed dates are stored as the UTC midnight of the calendar date; age = today - filed date.
        var filedOnOrBefore = new DateTimeOffset(today.AddDays(-minDays).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        query = query.Where(record =>
            (record.Status == IprStatus.FilingUnderProcess || record.Status == IprStatus.Filed) &&
            record.FiledAtUtc.HasValue &&
            record.FiledAtUtc <= filedOnOrBefore);

        if (maxDays.HasValue)
        {
            var filedAfter = new DateTimeOffset(today.AddDays(-maxDays.Value).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            query = query.Where(record => record.FiledAtUtc > filedAfter);
        }

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

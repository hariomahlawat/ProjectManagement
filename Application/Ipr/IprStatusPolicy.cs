using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using ProjectManagement.Infrastructure.Data;

namespace ProjectManagement.Application.Ipr;

/// <summary>
/// Single rule for which IPR records count. Filed and Granted records are the positive count
/// used by the register, dashboards, compendiums and briefings. Rejected and Withdrawn records
/// are closed: they are kept as history and reported as a separate figure, never added to the
/// positive count. FilingUnderProcess is a retired alias of Filed with no remaining rows; it is
/// still read as Filed so a stray legacy row is never silently dropped.
/// </summary>
public static class IprStatusPolicy
{
    public static readonly IReadOnlyList<IprStatus> CountedStatuses = new[] { IprStatus.Filed, IprStatus.Granted };

    public static readonly IReadOnlyList<IprStatus> ClosedStatuses = new[] { IprStatus.Rejected, IprStatus.Withdrawn };

    /// <summary>Statuses an editor can assign, in display order.</summary>
    public static readonly IReadOnlyList<IprStatus> SelectableStatuses = new[]
    {
        IprStatus.Filed,
        IprStatus.Granted,
        IprStatus.Rejected,
        IprStatus.Withdrawn
    };

    public static readonly Expression<Func<IprRecord, bool>> CountedPredicate =
        record => record.Status == IprStatus.FilingUnderProcess
                  || record.Status == IprStatus.Filed
                  || record.Status == IprStatus.Granted;

    public static readonly Expression<Func<IprRecord, bool>> ClosedPredicate =
        record => record.Status == IprStatus.Rejected
                  || record.Status == IprStatus.Withdrawn;

    public static bool IsCounted(IprStatus status)
        => status is IprStatus.FilingUnderProcess or IprStatus.Filed or IprStatus.Granted;

    public static bool IsClosed(IprStatus status)
        => status is IprStatus.Rejected or IprStatus.Withdrawn;

    public static bool IsSelectable(IprStatus status)
        => status is IprStatus.Filed or IprStatus.Granted or IprStatus.Rejected or IprStatus.Withdrawn;

    /// <summary>Maps the retired FilingUnderProcess alias to Filed; other statuses are unchanged.</summary>
    public static IprStatus Normalize(IprStatus status)
        => status == IprStatus.FilingUnderProcess ? IprStatus.Filed : status;
}

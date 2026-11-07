using System.Linq.Expressions;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Admin;

/// <summary>
/// The one account-state rule. Every account is in exactly one state, checked in this order:
/// pending deletion, disabled, temporarily locked (lockout ends after now), must change
/// password, active. The state resolver, the Users list (tiles, filters, export) and Admin
/// Home all read these predicates, as EF-translatable expressions for queries and as compiled
/// delegates in memory.
/// </summary>
public static class UserAccountStateRules
{
    private static readonly Expression<Func<ApplicationUser, DateTimeOffset, bool>> PendingDeletionRule =
        (user, nowUtc) => user.PendingDeletion;

    private static readonly Expression<Func<ApplicationUser, DateTimeOffset, bool>> DisabledRule =
        (user, nowUtc) => !user.PendingDeletion && user.IsDisabled;

    private static readonly Expression<Func<ApplicationUser, DateTimeOffset, bool>> TemporarilyLockedRule =
        (user, nowUtc) => !user.PendingDeletion
            && !user.IsDisabled
            && user.LockoutEnd.HasValue
            && user.LockoutEnd > nowUtc;

    private static readonly Expression<Func<ApplicationUser, DateTimeOffset, bool>> MustChangePasswordRule =
        (user, nowUtc) => !user.PendingDeletion
            && !user.IsDisabled
            && (!user.LockoutEnd.HasValue || user.LockoutEnd <= nowUtc)
            && user.MustChangePassword;

    private static readonly Expression<Func<ApplicationUser, DateTimeOffset, bool>> ActiveRule =
        (user, nowUtc) => !user.PendingDeletion
            && !user.IsDisabled
            && (!user.LockoutEnd.HasValue || user.LockoutEnd <= nowUtc)
            && !user.MustChangePassword;

    private static readonly IReadOnlyDictionary<AdminUserAccountState, Expression<Func<ApplicationUser, DateTimeOffset, bool>>> Rules =
        new Dictionary<AdminUserAccountState, Expression<Func<ApplicationUser, DateTimeOffset, bool>>>
        {
            [AdminUserAccountState.PendingDeletion] = PendingDeletionRule,
            [AdminUserAccountState.Disabled] = DisabledRule,
            [AdminUserAccountState.TemporarilyLocked] = TemporarilyLockedRule,
            [AdminUserAccountState.MustChangePassword] = MustChangePasswordRule,
            [AdminUserAccountState.Active] = ActiveRule
        };

    // Precedence order, used by the in-memory resolver.
    private static readonly (AdminUserAccountState State, Func<ApplicationUser, DateTimeOffset, bool> Test)[] Compiled =
        new[]
        {
            AdminUserAccountState.PendingDeletion,
            AdminUserAccountState.Disabled,
            AdminUserAccountState.TemporarilyLocked,
            AdminUserAccountState.MustChangePassword,
            AdminUserAccountState.Active
        }
        .Select(state => (state, Rules[state].Compile()))
        .ToArray();

    /// <summary>All states, in precedence order.</summary>
    public static IReadOnlyList<AdminUserAccountState> States { get; } =
        Compiled.Select(rule => rule.State).ToArray();

    public static Expression<Func<ApplicationUser, bool>> PendingDeletion(DateTimeOffset nowUtc) =>
        For(AdminUserAccountState.PendingDeletion, nowUtc);

    public static Expression<Func<ApplicationUser, bool>> Disabled(DateTimeOffset nowUtc) =>
        For(AdminUserAccountState.Disabled, nowUtc);

    public static Expression<Func<ApplicationUser, bool>> TemporarilyLocked(DateTimeOffset nowUtc) =>
        For(AdminUserAccountState.TemporarilyLocked, nowUtc);

    public static Expression<Func<ApplicationUser, bool>> MustChangePassword(DateTimeOffset nowUtc) =>
        For(AdminUserAccountState.MustChangePassword, nowUtc);

    public static Expression<Func<ApplicationUser, bool>> Active(DateTimeOffset nowUtc) =>
        For(AdminUserAccountState.Active, nowUtc);

    /// <summary>
    /// The query predicate for one state. <paramref name="nowUtc"/> is bound as a query
    /// parameter, not a SQL literal, so the plan is reused.
    /// </summary>
    public static Expression<Func<ApplicationUser, bool>> For(AdminUserAccountState state, DateTimeOffset nowUtc)
    {
        var rule = Rules[state];
        var now = Expression.Field(Expression.Constant(new NowHolder(nowUtc)), nameof(NowHolder.Value));
        var body = new ParameterReplacer(rule.Parameters[1], now).Visit(rule.Body);
        return Expression.Lambda<Func<ApplicationUser, bool>>(body, rule.Parameters[0]);
    }

    /// <summary>True when the account is in <paramref name="state"/> at <paramref name="nowUtc"/>.</summary>
    public static bool Is(ApplicationUser user, AdminUserAccountState state, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Compiled.First(rule => rule.State == state).Test(user, nowUtc);
    }

    /// <summary>The single state the account is in at <paramref name="nowUtc"/>.</summary>
    public static AdminUserAccountState StateOf(ApplicationUser user, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(user);
        foreach (var (state, test) in Compiled)
        {
            if (test(user, nowUtc))
            {
                return state;
            }
        }

        // The five rules cover every combination of flags; this is unreachable.
        throw new InvalidOperationException("Account matched no state.");
    }

    private sealed class NowHolder
    {
        public NowHolder(DateTimeOffset value) => Value = value;

        public readonly DateTimeOffset Value;
    }

    private sealed class ParameterReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly Expression _to;

        public ParameterReplacer(ParameterExpression from, Expression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node) =>
            node == _from ? _to : base.VisitParameter(node);
    }
}

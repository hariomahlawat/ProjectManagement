using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Admin.Calendar;

public sealed record DeletedCalendarEventQuery(
    string? Search = null,
    EventCategory? Category = null,
    DateOnly? DeletedFrom = null,
    DateOnly? DeletedTo = null,
    int Page = 1,
    int PageSize = 25);

public sealed record DeletedCalendarEventItem(
    Guid Id,
    string Title,
    EventCategory Category,
    string? Location,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    bool IsAllDay,
    bool IsRecurring,
    DateTimeOffset DeletedAtUtc,
    string? DeletedByName,
    string? CreatedByName);

public sealed record DeletedCalendarEventPage(
    IReadOnlyList<DeletedCalendarEventItem> Items,
    int TotalCount,
    int RecurringCount,
    int AllDayCount,
    int Page,
    int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public interface ICalendarRecoveryService
{
    Task<DeletedCalendarEventPage> QueryAsync(
        DeletedCalendarEventQuery query,
        CancellationToken cancellationToken = default);

    Task<AdminOperationResult> RestoreAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);
}

public sealed class CalendarRecoveryService : ICalendarRecoveryService
{
    private const int MaximumPageSize = 100;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAdminAuditService _audit;
    private readonly IAdminTimeService _time;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CalendarRecoveryService> _logger;

    public CalendarRecoveryService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IAdminAuditService audit,
        IAdminTimeService time,
        IHttpContextAccessor httpContextAccessor,
        ILogger<CalendarRecoveryService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userManager = userManager ?? throw new ArgumentNullException(nameof(userManager));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DeletedCalendarEventPage> QueryAsync(
        DeletedCalendarEventQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 25 : request.PageSize, 10, MaximumPageSize);
        var search = request.Search?.Trim();
        if (string.IsNullOrWhiteSpace(search)) search = null;
        else if (search.Length > 160) search = search[..160];

        var events = _db.Events.AsNoTracking().Where(calendarEvent => calendarEvent.IsDeleted);
        if (search is not null)
        {
            var pattern = $"%{search}%";
            events = events.Where(calendarEvent =>
                EF.Functions.ILike(calendarEvent.Title, pattern)
                || (calendarEvent.Location != null && EF.Functions.ILike(calendarEvent.Location, pattern))
                || (calendarEvent.Description != null && EF.Functions.ILike(calendarEvent.Description, pattern))
                || (calendarEvent.CreatedById != null && EF.Functions.ILike(calendarEvent.CreatedById, pattern))
                || (calendarEvent.UpdatedById != null && EF.Functions.ILike(calendarEvent.UpdatedById, pattern)));
        }
        if (request.Category.HasValue)
            events = events.Where(calendarEvent => calendarEvent.Category == request.Category.Value);
        if (request.DeletedFrom.HasValue)
        {
            var from = _time.StartOfIstDayUtc(request.DeletedFrom.Value);
            events = events.Where(calendarEvent => calendarEvent.UpdatedAt >= from);
        }
        if (request.DeletedTo.HasValue)
        {
            var to = _time.EndExclusiveOfIstDayUtc(request.DeletedTo.Value);
            events = events.Where(calendarEvent => calendarEvent.UpdatedAt < to);
        }

        // The query set uses one request-scoped DbContext; execute aggregates
        // sequentially because EF Core disallows concurrent operations per context.
        var totalCount = await events.CountAsync(cancellationToken);
        var recurringCount = await events.CountAsync(
            item => item.RecurrenceRule != null && item.RecurrenceRule != string.Empty,
            cancellationToken);
        var allDayCount = await events.CountAsync(item => item.IsAllDay, cancellationToken);

        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);
        var rows = await events
            .OrderByDescending(calendarEvent => calendarEvent.UpdatedAt)
            .ThenBy(calendarEvent => calendarEvent.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(calendarEvent => new
            {
                calendarEvent.Id,
                calendarEvent.Title,
                calendarEvent.Category,
                calendarEvent.Location,
                calendarEvent.StartUtc,
                calendarEvent.EndUtc,
                calendarEvent.IsAllDay,
                calendarEvent.RecurrenceRule,
                calendarEvent.UpdatedAt,
                calendarEvent.UpdatedById,
                calendarEvent.CreatedById
            })
            .ToListAsync(cancellationToken);

        var userIds = rows.SelectMany(row => new[] { row.UpdatedById, row.CreatedById })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, string> userNames;
        if (userIds.Length == 0)
        {
            userNames = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        else
        {
            var users = await _userManager.Users.AsNoTracking()
                .Where(user => userIds.Contains(user.Id))
                .Select(user => new { user.Id, DisplayName = user.FullName ?? user.UserName ?? user.Id })
                .ToListAsync(cancellationToken);
            userNames = users.ToDictionary(user => user.Id, user => user.DisplayName, StringComparer.Ordinal);
        }

        string? Resolve(string? id) => !string.IsNullOrWhiteSpace(id) && userNames.TryGetValue(id, out var display) ? display : id;
        var items = rows.Select(row => new DeletedCalendarEventItem(
            row.Id,
            row.Title,
            row.Category,
            row.Location,
            row.StartUtc,
            row.EndUtc,
            row.IsAllDay,
            !string.IsNullOrWhiteSpace(row.RecurrenceRule),
            row.UpdatedAt,
            Resolve(row.UpdatedById),
            Resolve(row.CreatedById))).ToArray();

        return new DeletedCalendarEventPage(items, totalCount, recurringCount, allDayCount, page, pageSize);
    }

    public async Task<AdminOperationResult> RestoreAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty)
            return AdminOperationResult.Failure("The selected calendar event is invalid.", "InvalidCalendarEventId");

        var traceId = _httpContextAccessor.HttpContext?.TraceIdentifier;
        try
        {
            var calendarEvent = await _db.Events.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == eventId, cancellationToken);
            if (calendarEvent is null)
                return AdminOperationResult.Failure("The calendar event could not be found.", "CalendarEventNotFound");
            if (!calendarEvent.IsDeleted)
                return AdminOperationResult.Failure("The calendar event has already been restored.", "CalendarEventAlreadyRestored");

            var actorUserId = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var restoredAt = _time.UtcNow;
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            var affected = await _db.Events.Where(item => item.Id == eventId && item.IsDeleted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.IsDeleted, false)
                    .SetProperty(item => item.UpdatedAt, restoredAt)
                    .SetProperty(item => item.UpdatedById, actorUserId), cancellationToken);
            if (affected == 0)
                return AdminOperationResult.Failure("The calendar event has already been restored by another administrator.", "CalendarEventAlreadyRestored");

            await _audit.RecordAsync(new AdminAuditEntry(
                Action: "CalendarEventRestored",
                EntityType: nameof(Event),
                EntityId: calendarEvent.Id.ToString(),
                Before: new
                {
                    calendarEvent.Id,
                    calendarEvent.Title,
                    calendarEvent.Category,
                    calendarEvent.StartUtc,
                    calendarEvent.EndUtc,
                    calendarEvent.IsDeleted,
                    DeletedAtUtc = calendarEvent.UpdatedAt,
                    DeletedByUserId = calendarEvent.UpdatedById
                },
                After: new { calendarEvent.Id, calendarEvent.Title, IsDeleted = false, RestoredAtUtc = restoredAt, RestoredByUserId = actorUserId },
                Origin: "Admin.Calendar.Deleted",
                Message: $"Restored calendar event '{calendarEvent.Title}'."), cancellationToken);

            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return AdminOperationResult.Success("Calendar event restored.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to restore deleted calendar event {EventId}. Trace {TraceId}", eventId, traceId);
            return AdminOperationResult.Failure(
                "The calendar event could not be restored. Review the audit log and try again.",
                "CalendarRestoreFailed",
                traceId);
        }
    }
}

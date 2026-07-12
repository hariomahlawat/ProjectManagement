using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Admin.Calendar;

public sealed record DeletedCalendarEventQuery(
    string? Search = null,
    EventCategory? Category = null,
    int Page = 1,
    int PageSize = 20);

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
    string? DeletedByName);

public sealed record DeletedCalendarEventPage(
    IReadOnlyList<DeletedCalendarEventItem> Items,
    int TotalCount,
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
        DeletedCalendarEventQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if (search is { Length: > 160 })
        {
            search = search[..160];
        }

        var events = _db.Events
            .AsNoTracking()
            .Where(calendarEvent => calendarEvent.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            events = events.Where(calendarEvent =>
                EF.Functions.ILike(calendarEvent.Title, pattern) ||
                (calendarEvent.Location != null && EF.Functions.ILike(calendarEvent.Location, pattern)));
        }

        if (query.Category.HasValue)
        {
            events = events.Where(calendarEvent => calendarEvent.Category == query.Category.Value);
        }

        var totalCount = await events.CountAsync(cancellationToken);
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
                calendarEvent.UpdatedById
            })
            .ToListAsync(cancellationToken);

        var userIds = rows
            .Select(row => row.UpdatedById)
            .Where(userId => !string.IsNullOrWhiteSpace(userId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Dictionary<string, string> userNames;
        if (userIds.Length == 0)
        {
            userNames = new Dictionary<string, string>(StringComparer.Ordinal);
        }
        else
        {
            var users = await _userManager.Users
                .AsNoTracking()
                .Where(user => userIds.Contains(user.Id))
                .Select(user => new
                {
                    user.Id,
                    DisplayName = string.IsNullOrWhiteSpace(user.FullName)
                        ? user.UserName ?? user.Id
                        : user.FullName
                })
                .ToListAsync(cancellationToken);

            userNames = users.ToDictionary(user => user.Id, user => user.DisplayName, StringComparer.Ordinal);
        }

        var items = rows
            .Select(row => new DeletedCalendarEventItem(
                row.Id,
                row.Title,
                row.Category,
                row.Location,
                row.StartUtc,
                row.EndUtc,
                row.IsAllDay,
                !string.IsNullOrWhiteSpace(row.RecurrenceRule),
                row.UpdatedAt,
                !string.IsNullOrWhiteSpace(row.UpdatedById) && userNames.TryGetValue(row.UpdatedById, out var name)
                    ? name
                    : null))
            .ToList();

        return new DeletedCalendarEventPage(items, totalCount, page, pageSize);
    }

    public async Task<AdminOperationResult> RestoreAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        if (eventId == Guid.Empty)
        {
            return AdminOperationResult.Failure(
                "The selected calendar event is invalid.",
                "InvalidCalendarEventId");
        }

        var traceId = _httpContextAccessor.HttpContext?.TraceIdentifier;

        try
        {
            var calendarEvent = await _db.Events
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == eventId, cancellationToken);

            if (calendarEvent is null)
            {
                return AdminOperationResult.Failure(
                    "The calendar event could not be found.",
                    "CalendarEventNotFound");
            }

            if (!calendarEvent.IsDeleted)
            {
                return AdminOperationResult.Failure(
                    "The calendar event has already been restored.",
                    "CalendarEventAlreadyRestored");
            }

            var before = new
            {
                calendarEvent.Id,
                calendarEvent.Title,
                calendarEvent.Category,
                calendarEvent.StartUtc,
                calendarEvent.EndUtc,
                calendarEvent.IsDeleted,
                DeletedAtUtc = calendarEvent.UpdatedAt,
                DeletedByUserId = calendarEvent.UpdatedById
            };

            var actorUserId = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var restoredAt = _time.UtcNow;
            await using var transaction = _db.Database.IsRelational()
                ? await _db.Database.BeginTransactionAsync(cancellationToken)
                : null;

            var affected = await _db.Events
                .Where(item => item.Id == eventId && item.IsDeleted)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.IsDeleted, false)
                        .SetProperty(item => item.UpdatedAt, restoredAt)
                        .SetProperty(item => item.UpdatedById, actorUserId),
                    cancellationToken);

            if (affected == 0)
            {
                return AdminOperationResult.Failure(
                    "The calendar event has already been restored by another administrator.",
                    "CalendarEventAlreadyRestored");
            }

            await _audit.RecordAsync(
                new AdminAuditEntry(
                    Action: "CalendarEventRestored",
                    EntityType: nameof(Event),
                    EntityId: calendarEvent.Id.ToString(),
                    Before: before,
                    After: new
                    {
                        calendarEvent.Id,
                        calendarEvent.Title,
                        IsDeleted = false,
                        RestoredAtUtc = restoredAt,
                        RestoredByUserId = actorUserId
                    },
                    Origin: "Admin.Calendar.Deleted",
                    Message: $"Restored calendar event '{calendarEvent.Title}'."),
                cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return AdminOperationResult.Success("Calendar event restored.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Calendar event restoration failed. EventId={EventId}, TraceId={TraceId}",
                eventId,
                traceId);

            return AdminOperationResult.Failure(
                "The calendar event could not be restored. Quote the trace reference to the administrator.",
                "CalendarRestoreFailed",
                traceId);
        }
    }
}

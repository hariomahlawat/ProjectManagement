using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Admin;

/// <summary>One stored audit entry, as read for display.</summary>
public sealed record AuditEntrySource(
    string Action,
    string? Message,
    string? UserId,
    string? UserName,
    string? DataJson,
    string? Title = null);

/// <summary>An audit entry ready to show: who, a plain detail, and the record by name.</summary>
public sealed record AuditEntryView(
    string Actor,
    string? Detail,
    AuditRecordDisplay? Record,
    AuditRecordRef? RecordRef,
    AdminAuditPayload Payload);

/// <summary>
/// Shows audit entries the same way everywhere (audit log, Home, the list hub, a user's page):
/// people through <see cref="AuditActorNames"/>, a plain detail from
/// <see cref="AuditEntryDescriber"/>, and the affected record by name. Names are looked up for
/// the whole page at once: one query for every person (actors and affected accounts) and one
/// query per kind of record on the page, never one per row.
/// </summary>
public static class AuditEntryPresenter
{
    /// <summary>Shown for old account entries that did not keep who acted.</summary>
    public const string ActorNotRecorded = "Not recorded";

    public static async Task<IReadOnlyList<AuditEntryView>> DescribeAsync(
        ApplicationDbContext db,
        IReadOnlyList<AuditEntrySource> entries,
        IAdminAuditPayloadParser? parser,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(entries);
        parser ??= new AdminAuditPayloadParser();

        var described = entries
            .Select(entry =>
            {
                var payload = parser.Parse(entry.DataJson);
                var text = AuditEntryDescriber.Describe(
                    entry.Action, entry.Message, entry.UserId, entry.UserName, payload, entry.Title);
                return (Entry: entry, Payload: payload, Text: text);
            })
            .ToArray();

        var records = described
            .SelectMany(item => new[] { item.Text.Record, item.Text.Mention })
            .Where(record => record is not null)
            .Select(record => record!)
            .ToArray();

        // People: the actors and every account named as a record, in one query.
        var (byId, byUserName) = await AuditActorNames.LoadDirectoryAsync(
            db,
            described.SelectMany(item => new[] { item.Payload.ActorUserId, ActorId(item), item.Entry.UserId })
                .Concat(records.Where(record => record.Kind == AuditRecordKinds.Person).Select(record => record.Key)),
            described.SelectMany(item => new[] { item.Payload.ActorName, item.Entry.UserName })
                .Concat(records.Where(record => record.Kind == AuditRecordKinds.PersonUserName).Select(record => record.Key)),
            cancellationToken);

        var names = await LoadRecordNamesAsync(db, records, cancellationToken);

        return described
            .Select(item =>
            {
                var legacyActorId = ActorId(item);
                var actor = item.Text.ActorNotRecorded
                    ? ActorNotRecorded
                    : legacyActorId is not null
                        // The legacy user columns belong to the affected account, never the
                        // actor: an actor id that no longer resolves reads "Removed user".
                        ? AuditActorNames.Resolve(legacyActorId, null, byId, byUserName)
                        : AuditActorNames.Resolve(
                            FirstPresent(item.Payload.ActorUserId, item.Entry.UserId),
                            FirstPresent(item.Payload.ActorName, item.Entry.UserName),
                            byId,
                            byUserName);
                var record = item.Text.Record is null ? null : Display(item.Text.Record, names, byId, byUserName);
                var mention = item.Text.Mention is null ? null : Display(item.Text.Mention, names, byId, byUserName);
                return new AuditEntryView(
                    actor,
                    AuditEntryDescriber.Fill(item.Text.Detail, mention?.Name),
                    record,
                    item.Text.Record,
                    item.Payload);
            })
            .ToArray();
    }

    // Old account entries kept the person who acted as "Actor" in the details.
    private static string? ActorId((AuditEntrySource Entry, AdminAuditPayload Payload, AuditEntryText Text) item) =>
        item.Entry.Action.StartsWith("AdminUser", StringComparison.OrdinalIgnoreCase) && item.Payload.EntityType is null
            ? item.Payload.Field("Actor")
            : null;

    private static string? FirstPresent(string? first, string? second) =>
        string.IsNullOrWhiteSpace(first) ? second : first;

    private static AuditRecordDisplay Display(
        AuditRecordRef record,
        IReadOnlyDictionary<(string Kind, string Key), string> names,
        IReadOnlyDictionary<string, AuditActorDirectoryEntry> byId,
        IReadOnlyDictionary<string, AuditActorDirectoryEntry> byUserName)
    {
        var typeLabel = AuditRecordKinds.TypeLabel(record.Kind);
        switch (record.Kind)
        {
            case AuditRecordKinds.Person:
                return new AuditRecordDisplay(
                    byId.TryGetValue(record.Key, out var person) && person.DisplayName is { } personName
                        ? personName
                        : record.FallbackName ?? AuditActorNames.UnknownPerson,
                    typeLabel);
            case AuditRecordKinds.PersonUserName:
                // A typed user name: name the account it matches, or show what was typed.
                return byUserName.TryGetValue(record.Key, out var named) && named.DisplayName is { } namedName
                    ? new AuditRecordDisplay(namedName, typeLabel)
                    : new AuditRecordDisplay(record.Key, "User name typed (no such account)");
            case AuditRecordKinds.List:
                return new AuditRecordDisplay(record.FallbackName ?? record.Key, typeLabel);
        }

        if (names.TryGetValue((record.Kind, Normalise(record.Key)), out var name))
        {
            return new AuditRecordDisplay(name, typeLabel);
        }

        return new AuditRecordDisplay(
            record.FallbackName ?? $"No. {record.Key}",
            $"{typeLabel}, no longer in PRISM");
    }

    private static string Normalise(string key) => key.Trim().ToLowerInvariant();

    /// <summary>One query per kind of record present; keys are matched in lower case.</summary>
    internal static async Task<IReadOnlyDictionary<(string Kind, string Key), string>> LoadRecordNamesAsync(
        ApplicationDbContext db,
        IReadOnlyCollection<AuditRecordRef> records,
        CancellationToken cancellationToken)
    {
        var names = new Dictionary<(string Kind, string Key), string>();

        int[] IntKeys(string kind) => records
            .Where(record => record.Kind == kind)
            .Select(record => int.TryParse(record.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        long[] LongKeys(string kind) => records
            .Where(record => record.Kind == kind)
            .Select(record => long.TryParse(record.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : (long?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        void Add(string kind, IEnumerable<(string Key, string Name)> rows)
        {
            foreach (var (key, name) in rows)
            {
                if (!string.IsNullOrWhiteSpace(name)) names[(kind, Normalise(key))] = name.Trim();
            }
        }

        var projectIds = IntKeys(AuditRecordKinds.Project);
        if (projectIds.Length > 0)
        {
            // Deleted projects keep their name in the log.
            var rows = await db.Projects.IgnoreQueryFilters().AsNoTracking()
                .Where(item => projectIds.Contains(item.Id))
                .Select(item => new { item.Id, item.Name })
                .ToListAsync(cancellationToken);
            Add(AuditRecordKinds.Project, rows.Select(row => (row.Id.ToString(CultureInfo.InvariantCulture), row.Name)));
        }

        var deckIds = LongKeys(AuditRecordKinds.BriefingDeck);
        if (deckIds.Length > 0)
        {
            var rows = await db.ProjectBriefingDecks.IgnoreQueryFilters().AsNoTracking()
                .Where(item => deckIds.Contains(item.Id))
                .Select(item => new { item.Id, item.Name })
                .ToListAsync(cancellationToken);
            Add(AuditRecordKinds.BriefingDeck, rows.Select(row => (row.Id.ToString(CultureInfo.InvariantCulture), row.Name)));
        }

        var issueIds = LongKeys(AuditRecordKinds.ArppIssue);
        if (issueIds.Length > 0)
        {
            var rows = await db.ArppIssues.IgnoreQueryFilters().AsNoTracking()
                .Where(item => issueIds.Contains(item.Id))
                .Select(item => new { item.Id, item.Name })
                .ToListAsync(cancellationToken);
            Add(AuditRecordKinds.ArppIssue, rows.Select(row => (row.Id.ToString(CultureInfo.InvariantCulture), row.Name)));
        }

        async Task AddListAsync<T>(string kind, IQueryable<T> source, Func<IQueryable<T>, int[], Task<List<(int Id, string Name)>>> load)
        {
            var ids = IntKeys(kind);
            if (ids.Length == 0) return;
            var rows = await load(source, ids);
            Add(kind, rows.Select(row => (row.Id.ToString(CultureInfo.InvariantCulture), row.Name)));
        }

        await AddListAsync(AuditRecordKinds.ProjectCategory, db.ProjectCategories.AsNoTracking(), async (query, ids) =>
            (await query.Where(item => ids.Contains(item.Id)).Select(item => new { item.Id, item.Name }).ToListAsync(cancellationToken))
                .Select(row => (row.Id, row.Name)).ToList());
        await AddListAsync(AuditRecordKinds.TechnicalCategory, db.TechnicalCategories.AsNoTracking(), async (query, ids) =>
            (await query.Where(item => ids.Contains(item.Id)).Select(item => new { item.Id, item.Name }).ToListAsync(cancellationToken))
                .Select(row => (row.Id, row.Name)).ToList());
        await AddListAsync(AuditRecordKinds.ProjectType, db.ProjectTypes.AsNoTracking(), async (query, ids) =>
            (await query.Where(item => ids.Contains(item.Id)).Select(item => new { item.Id, item.Name }).ToListAsync(cancellationToken))
                .Select(row => (row.Id, row.Name)).ToList());
        await AddListAsync(AuditRecordKinds.SponsoringUnit, db.SponsoringUnits.AsNoTracking(), async (query, ids) =>
            (await query.Where(item => ids.Contains(item.Id)).Select(item => new { item.Id, item.Name }).ToListAsync(cancellationToken))
                .Select(row => (row.Id, row.Name)).ToList());
        await AddListAsync(AuditRecordKinds.LineDirectorate, db.LineDirectorates.AsNoTracking(), async (query, ids) =>
            (await query.Where(item => ids.Contains(item.Id)).Select(item => new { item.Id, item.Name }).ToListAsync(cancellationToken))
                .Select(row => (row.Id, row.Name)).ToList());
        await AddListAsync(AuditRecordKinds.ActivityType, db.ActivityTypes.AsNoTracking(), async (query, ids) =>
            (await query.Where(item => ids.Contains(item.Id)).Select(item => new { item.Id, item.Name }).ToListAsync(cancellationToken))
                .Select(row => (row.Id, row.Name)).ToList());
        await AddListAsync(AuditRecordKinds.Holiday, db.Holidays.AsNoTracking(), async (query, ids) =>
            (await query.Where(item => ids.Contains(item.Id)).Select(item => new { item.Id, item.Name }).ToListAsync(cancellationToken))
                .Select(row => (row.Id, row.Name)).ToList());

        var celebrationIds = records
            .Where(record => record.Kind == AuditRecordKinds.Celebration)
            .Select(record => Guid.TryParse(record.Key, out var id) ? id : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        if (celebrationIds.Length > 0)
        {
            var rows = await db.Celebrations.IgnoreQueryFilters().AsNoTracking()
                .Where(item => celebrationIds.Contains(item.Id))
                .Select(item => new { item.Id, item.Name })
                .ToListAsync(cancellationToken);
            Add(AuditRecordKinds.Celebration, rows.Select(row => (row.Id.ToString(), row.Name)));
        }

        return names;
    }
}

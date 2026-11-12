using System.Text.Json;

namespace ProjectManagement.Pages.Photos.People;

/// <summary>
/// What the reviewer just decided on the Review people page, kept for a few minutes so the
/// decision can be taken back from the message that confirms it. Only two decisions can be
/// undone: naming faces (they go back to review) and leaving faces unnamed (they are reopened).
/// </summary>
public sealed record PeopleReviewUndo(string Kind, Guid? PersonId, IReadOnlyList<Guid> FaceIds, string UserId, DateTimeOffset DecidedAtUtc)
{
    public const string Named = "named";
    public const string LeftUnnamed = "left-unnamed";

    /// <summary>The reason recorded against the removed assignments when a naming is undone.</summary>
    public const string UndoReason = "Undone by the reviewer straight after naming.";

    /// <summary>How long after the decision Undo is still offered and honoured.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The state travels in TempData, which may be a cookie. Past this many faces no Undo is
    /// offered; the decision can still be corrected from the person's page.
    /// </summary>
    public const int MaximumFaces = 40;

    public static string? ForNaming(Guid personId, IReadOnlyCollection<Guid> faceIds, string userId, DateTimeOffset now)
        => Serialize(Named, personId, faceIds, userId, now);

    public static string? ForLeavingUnnamed(IReadOnlyCollection<Guid> faceIds, string userId, DateTimeOffset now)
        => Serialize(LeftUnnamed, null, faceIds, userId, now);

    /// <summary>
    /// Reads the state back. Null when there is none, when it is malformed, when it belongs to
    /// another user or when it is older than <see cref="Lifetime"/>.
    /// </summary>
    public static PeopleReviewUndo? Read(string? state, string userId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return null;
        }

        PeopleReviewUndo? undo;
        try
        {
            undo = JsonSerializer.Deserialize<PeopleReviewUndo>(state);
        }
        catch (JsonException)
        {
            return null;
        }

        if (undo is null
            || undo.FaceIds is null
            || undo.FaceIds.Count == 0
            || undo.FaceIds.Count > MaximumFaces
            || !string.Equals(undo.UserId, userId, StringComparison.Ordinal)
            || now - undo.DecidedAtUtc > Lifetime
            || undo.DecidedAtUtc - now > TimeSpan.FromMinutes(1))
        {
            return null;
        }

        return undo.Kind switch
        {
            Named when undo.PersonId is { } personId && personId != Guid.Empty => undo,
            LeftUnnamed => undo,
            _ => null
        };
    }

    private static string? Serialize(string kind, Guid? personId, IReadOnlyCollection<Guid> faceIds, string userId, DateTimeOffset now)
    {
        var faces = faceIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (faces.Count == 0 || faces.Count > MaximumFaces || string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return JsonSerializer.Serialize(new PeopleReviewUndo(kind, personId, faces, userId, now));
    }
}

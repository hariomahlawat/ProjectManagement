using System.Text.Json;

namespace ProjectManagement.Services.Remarks;

/// <summary>
/// Creates and validates structured metadata written to the remark audit jsonb column.
/// All callers must use valid JSON; plain text is deliberately rejected before persistence.
/// </summary>
public static class RemarkAuditMetadata
{
    public const string InvalidMetadataMessage = "Remark audit metadata must be valid JSON.";

    public static string ForOfficerConferenceReview(string officerUserId)
    {
        if (string.IsNullOrWhiteSpace(officerUserId))
        {
            throw new ArgumentException("Officer user id is required.", nameof(officerUserId));
        }

        return JsonSerializer.Serialize(new
        {
            origin = "officer-conference-review",
            officerUserId = officerUserId.Trim()
        });
    }

    public static void Validate(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return;
        }

        try
        {
            using var _ = JsonDocument.Parse(metadata);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(InvalidMetadataMessage, ex);
        }
    }
}

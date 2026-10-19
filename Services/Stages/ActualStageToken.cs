using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ProjectManagement.Models.Execution;

namespace ProjectManagement.Services.Stages;

/// <summary>
/// Optimistic per-stage editor identity for Actual-date forms. A form records
/// the token of every row it renders; a save that would change a row is
/// rejected as stale when the stored row no longer matches. It is calculated
/// from persisted facts only and is not an authorization or CSRF token.
/// </summary>
public static class ActualStageToken
{
    /// <summary>Posted by baseline-aware forms; its absence marks a legacy post.</summary>
    public const int BaselineVersion = 1;

    public static string Compute(ProjectStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);

        var canonical = string.Join(
            '|',
            stage.StageCode.Trim().ToUpperInvariant(),
            stage.Status.ToString(),
            Format(stage.ActualStart),
            Format(stage.CompletedOn),
            stage.RequiresBackfill ? "1" : "0");

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static bool Matches(string? postedToken, ProjectStage stage) =>
        !string.IsNullOrWhiteSpace(postedToken)
        && string.Equals(postedToken.Trim(), Compute(stage), StringComparison.OrdinalIgnoreCase);

    private static string Format(DateOnly? date) =>
        date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
}

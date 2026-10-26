using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application
{
    // SECTION: Proliferation report lookups
    // The Reports page runs through ProliferationAnalysisService; this service only serves the
    // receiving-unit suggestions used by the Records editor.
    public sealed class ProliferationReportsService
    {
        private readonly ApplicationDbContext _db;

        public ProliferationReportsService(ApplicationDbContext db)
        {
            _db = db;
        }

        // SECTION: Unit suggestions
        public async Task<IReadOnlyList<string>> GetUnitSuggestionsAsync(string? q, int take, CancellationToken ct)
        {
            var term = (q ?? string.Empty).Trim();
            if (term.Length < 2)
            {
                return Array.Empty<string>();
            }

            var like = $"%{EscapeLikePattern(term)}%";
            var results = await _db.ProliferationGranularEntries
                .AsNoTracking()
                .Where(x => x.ApprovalStatus == ApprovalStatus.Approved)
                .Where(x => x.UnitName != null && EF.Functions.ILike(x.UnitName, like, "\\"))
                .Select(x => x.UnitName!)
                .Distinct()
                .OrderBy(x => x)
                .Take(Math.Clamp(take, 1, 50))
                .ToListAsync(ct);

            return results;
        }

        private static string EscapeLikePattern(string value)
        {
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal);
        }
    }
}

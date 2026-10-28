using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.DocumentRepository.Models;
using ProjectManagement.Configuration;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents
{
    /// <summary>
    /// The repository's main page: a summary strip, scope tabs (all, favourites, AOTS), filters
    /// with live counts and the result list (rows or cards). The results region is also served
    /// on its own (<c>partial=true</c>) so filters apply without reloading the page.
    /// </summary>
    [Authorize(Policy = "DocRepo.View")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IDocumentSearchService _searchService;
        private readonly IDocRepoAccess _access;
        private readonly IAuthorizationService _authorization;
        private readonly IAotsUnreadService _aotsUnread;

        public IndexModel(
            ApplicationDbContext db,
            IDocumentSearchService searchService,
            IDocRepoAccess access,
            IAuthorizationService authorization,
            IAotsUnreadService aotsUnread)
        {
            _db = db;
            _searchService = searchService;
            _access = access;
            _authorization = authorization;
            _aotsUnread = aotsUnread;
        }

        // Years outside this range are ignored rather than failing the page.
        public const int MinYear = 1900;
        public const int MaxYear = 2100;
        private const int DefaultPageSize = 30;

        // SECTION: Query (names are part of shared links; keep them stable)
        [FromQuery(Name = "q")] public string? Q { get; set; }
        [FromQuery(Name = "tag")] public string? Tag { get; set; }
        [FromQuery(Name = "officeCategoryId")] public long? OfficeCategoryId { get; set; }
        [FromQuery(Name = "documentCategoryId")] public long? DocumentCategoryId { get; set; }
        [FromQuery(Name = "year")] public int? Year { get; set; }
        [FromQuery(Name = "includeInactive")] public bool IncludeInactive { get; set; }
        [FromQuery(Name = "view")] public string? View { get; set; }
        [FromQuery(Name = "partial")] public bool IsPartial { get; set; }
        [FromQuery(Name = "scope")] public string? Scope { get; set; }
        [FromQuery(Name = "sort")] public string? Sort { get; set; }
        [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;
        [FromQuery] public int PageSize { get; set; } = DefaultPageSize;

        // SECTION: View state
        // "list" (default, rows with snippets), "table" (compact, like the IPR register) or "cards".
        public bool IsCardView => string.Equals(View, "cards", StringComparison.OrdinalIgnoreCase);
        public bool IsTableView => string.Equals(View, "table", StringComparison.OrdinalIgnoreCase);
        public bool IsListView => !IsCardView && !IsTableView;
        public string ViewMode => IsCardView ? "cards" : IsTableView ? "table" : "list";
        public string? ViewValue => IsListView ? null : ViewMode;
        public bool IsFavouritesScope => string.Equals(Scope, "favourites", StringComparison.OrdinalIgnoreCase);
        public bool IsAotsScope => string.Equals(Scope, "aots", StringComparison.OrdinalIgnoreCase);
        public bool IsDefaultScope => !IsFavouritesScope && !IsAotsScope;
        public string? ScopeValue => IsFavouritesScope ? "favourites" : IsAotsScope ? "aots" : null;
        public bool IsSearchActive { get; private set; }
        public bool HasTagMatch { get; private set; }
        public DocRepoSort SortOrder { get; private set; } = DocRepoSort.Newest;
        public string ReturnUrl { get; private set; } = "/DocumentRepository/Documents";

        public bool HasAnyFilter =>
            IsSearchActive || !string.IsNullOrWhiteSpace(Tag) || OfficeCategoryId.HasValue ||
            DocumentCategoryId.HasValue || Year.HasValue || IncludeInactive;

        // SECTION: What the user may do (checked once per page, not per row)
        public bool CanViewInactive { get; private set; }
        public bool CanUpload { get; private set; }
        public bool CanEdit { get; private set; }
        public bool CanManage { get; private set; }
        public bool CanApproveDeletes { get; private set; }
        public bool CanPurge { get; private set; }
        public bool CanManageCategories { get; private set; }
        public bool HasAdminLinks => CanApproveDeletes || CanPurge || CanManageCategories;

        // SECTION: Results
        public int TotalCount { get; private set; }
        public int PageCount => Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize));
        public List<DocumentSearchResultVm> Items { get; private set; } = new();

        // SECTION: Filters with counts (each count applies every other active filter)
        public IReadOnlyList<FacetOption> OfficeFacets { get; private set; } = Array.Empty<FacetOption>();
        public IReadOnlyList<FacetOption> TypeFacets { get; private set; } = Array.Empty<FacetOption>();
        public IReadOnlyList<FacetOption> YearFacets { get; private set; } = Array.Empty<FacetOption>();
        public string? OfficeName => OfficeFacets.FirstOrDefault(f => f.Value == OfficeCategoryId?.ToString(CultureInfo.InvariantCulture))?.Label;
        public string? TypeName => TypeFacets.FirstOrDefault(f => f.Value == DocumentCategoryId?.ToString(CultureInfo.InvariantCulture))?.Label;

        // SECTION: Summary strip (whole repository, whatever the filters)
        public RepositorySummary Summary { get; private set; } = new();

        /// <summary>The most used tags, offered as suggestions in the Tag filter.</summary>
        public IReadOnlyList<string> PopularTags { get; private set; } = Array.Empty<string>();

        public async Task OnGetAsync()
        {
            // SECTION: Partial request detection
            if (!IsPartial &&
                Request.Headers.TryGetValue("X-Requested-With", out var requestedWith) &&
                string.Equals(requestedWith.ToString(), "DocRepoPartial", StringComparison.OrdinalIgnoreCase))
            {
                IsPartial = true;
            }

            // SECTION: Guard input
            if (PageNumber < 1) PageNumber = 1;
            if (PageSize < 10 || PageSize > 200) PageSize = DefaultPageSize;
            if (Year is < MinYear or > MaxYear) Year = null;
            Tag = string.IsNullOrWhiteSpace(Tag) ? null : Tag.Trim();

            await LoadPermissionsAsync();
            if (!CanViewInactive)
            {
                IncludeInactive = false;
            }

            ReturnUrl = BuildReturnUrl();
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // SECTION: Scope (all / favourites / AOTS) — the base every count is taken from
            var scoped = ApplyScope(_access.Readable(_db.Documents.AsNoTracking(), IncludeInactive)
                .Where(document => !document.IsExternal), userId);

            var hasSearch = _searchService.TryPrepareQuery(Q, out var preparedQuery);
            if (hasSearch)
            {
                Q = preparedQuery;
                IsSearchActive = true;
            }

            SortOrder = DocRepoSortParser.Parse(Sort, hasSearch);

            // SECTION: Results
            var filtered = ApplyFilters(scoped, FacetKind.None);
            var rows = hasSearch
                ? _searchService.ApplySearchProjected(filtered, preparedQuery)
                : Project(filtered);
            rows = ApplySort(rows, SortOrder);

            TotalCount = await rows.CountAsync();
            if (PageNumber > PageCount)
            {
                PageNumber = PageCount;
            }

            Items = await rows
                .Skip((PageNumber - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            await ApplyTagsAsync(Items);
            await ApplyPersonalStateAsync(userId, Items);
            HasTagMatch = hasSearch && await _searchService.ApplySearchProjected(filtered, preparedQuery).AnyAsync(item => item.MatchedInTags);

            // SECTION: Filter counts
            await LoadFacetsAsync(scoped, hasSearch ? preparedQuery : null);

            // SECTION: Summary and tag suggestions (full page only)
            if (!IsPartial)
            {
                await LoadSummaryAsync(userId);
            }

            PopularTags = await _db.DocumentTags.AsNoTracking()
                .Where(documentTag => !documentTag.Document.IsDeleted && documentTag.Document.IsActive && !documentTag.Document.IsExternal)
                .GroupBy(documentTag => documentTag.Tag.Name)
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Key)
                .Select(group => group.Key)
                .Take(40)
                .ToListAsync();
        }

        // SECTION: Favourite toggle handler
        public async Task<IActionResult> OnPostToggleFavouriteAsync(Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized();
            }

            var canViewInactive = await _access.CanViewInactiveAsync(User);
            var canViewDocument = await _access.Readable(_db.Documents.AsNoTracking(), canViewInactive)
                .AnyAsync(document => document.Id == id && !document.IsExternal);

            if (!canViewDocument)
            {
                return NotFound();
            }

            var existing = await _db.DocRepoFavourites
                .FirstOrDefaultAsync(favourite => favourite.UserId == userId && favourite.DocumentId == id);

            if (existing != null)
            {
                _db.DocRepoFavourites.Remove(existing);
                await _db.SaveChangesAsync();
                return new JsonResult(new { isFavourite = false });
            }

            var favourite = new DocRepoFavourite
            {
                UserId = userId,
                DocumentId = id,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.DocRepoFavourites.Add(favourite);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // A second click can race this one; if it already starred the document, that is
                // the result the user asked for.
                _db.Entry(favourite).State = EntityState.Detached;
                var alreadyStarred = await _db.DocRepoFavourites.AsNoTracking()
                    .AnyAsync(existingFavourite => existingFavourite.UserId == userId && existingFavourite.DocumentId == id);
                if (!alreadyStarred)
                {
                    throw;
                }
            }

            return new JsonResult(new { isFavourite = true });
        }

        // SECTION: Preview handler (wide-screen reading pane)
        public async Task<IActionResult> OnGetPreviewAsync(Guid id)
        {
            await LoadPermissionsAsync();
            var document = await _access.Readable(_db.Documents.AsNoTracking(), CanViewInactive)
                .Where(d => d.Id == id && !d.IsExternal)
                .Select(d => new DocumentPreviewVm
                {
                    Id = d.Id,
                    Subject = d.Subject,
                    ReceivedFrom = d.ReceivedFrom,
                    DocumentDate = d.DocumentDate,
                    OfficeName = d.OfficeCategory.Name,
                    DocumentCategoryName = d.DocumentCategory.Name,
                    OriginalFileName = d.OriginalFileName,
                    FileSizeBytes = d.FileSizeBytes,
                    IsActive = d.IsActive,
                    IsAots = d.IsAots,
                    OcrStatus = d.OcrStatus,
                    CreatedAtUtc = d.CreatedAtUtc,
                    CreatedByUserId = d.CreatedByUserId,
                    Tags = d.DocumentTags.Select(dt => dt.Tag.Name).OrderBy(name => name).ToList()
                })
                .FirstOrDefaultAsync();

            if (document is null)
            {
                return NotFound();
            }

            // Previewing an AOTS document is reading it, as opening it in the reader is.
            if (document.IsAots)
            {
                await _aotsUnread.MarkAsReadAsync(document.Id, User.FindFirstValue(ClaimTypes.NameIdentifier));
            }

            document.CreatedByName = await _db.Users.AsNoTracking()
                .Where(user => user.Id == document.CreatedByUserId)
                .Select(user => string.IsNullOrWhiteSpace(user.FullName) ? user.UserName : user.FullName)
                .FirstOrDefaultAsync();

            var returnUrl = Url.IsLocalUrl(Request.Query["returnUrl"].ToString()) ? Request.Query["returnUrl"].ToString() : ReturnUrl;
            document.ReturnUrl = returnUrl;
            ViewData["Page"] = this;
            return Partial("_DocumentPreview", document);
        }

        // SECTION: URL helpers
        /// <summary>The current list URL with one query value replaced (null removes it); page resets to 1 unless set.</summary>
        public string UrlWith(string key, string? value, bool keepPage = false)
        {
            var query = QueryHelpers.ParseQuery(Request.QueryString.ToString())
                .Where(kv => !string.Equals(kv.Key, "partial", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);

            if (!keepPage && !string.Equals(key, "page", StringComparison.OrdinalIgnoreCase))
            {
                query.Remove("page");
            }

            if (string.IsNullOrEmpty(value))
            {
                query.Remove(key);
            }
            else
            {
                query[key] = value;
            }

            var builder = new QueryBuilder(query.Where(kv => !string.IsNullOrEmpty(kv.Value)));
            return $"{Request.PathBase}{Request.Path}{builder}";
        }

        public string ScopeUrl(string? scope)
        {
            var builder = new QueryBuilder();
            if (!string.IsNullOrEmpty(scope)) builder.Add("scope", scope);
            if (ViewValue is not null) builder.Add("view", ViewValue);
            return $"{Request.PathBase}{Request.Path}{builder}";
        }

        public string ClearFiltersUrl()
        {
            var builder = new QueryBuilder();
            if (ScopeValue is not null) builder.Add("scope", ScopeValue);
            if (ViewValue is not null) builder.Add("view", ViewValue);
            return $"{Request.PathBase}{Request.Path}{builder}";
        }

        // SECTION: Query building
        private enum FacetKind { None, Office, Type, Year }

        private IQueryable<Document> ApplyScope(IQueryable<Document> query, string? userId)
        {
            if (IsFavouritesScope)
            {
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return query.Where(document => false);
                }

                var favouriteIds = _db.DocRepoFavourites
                    .Where(favourite => favourite.UserId == userId)
                    .Select(favourite => favourite.DocumentId);
                query = query.Where(document => favouriteIds.Contains(document.Id));
            }

            if (IsAotsScope)
            {
                query = query.Where(document => document.IsAots);
            }

            return query;
        }

        // Applies every active filter except the one a facet is counting.
        private IQueryable<Document> ApplyFilters(IQueryable<Document> query, FacetKind except)
        {
            if (Tag is not null)
            {
                var loweredTag = Tag.ToLowerInvariant();
                var tag = Tag;
                query = query.Where(document => document.DocumentTags.Any(documentTag =>
                    documentTag.Tag.Name == tag || documentTag.Tag.NormalizedName == loweredTag));
            }

            if (except != FacetKind.Office && OfficeCategoryId.HasValue)
            {
                var officeId = OfficeCategoryId.Value;
                query = query.Where(document => document.OfficeCategoryId == officeId);
            }

            if (except != FacetKind.Type && DocumentCategoryId.HasValue)
            {
                var documentCategoryId = DocumentCategoryId.Value;
                query = query.Where(document => document.DocumentCategoryId == documentCategoryId);
            }

            if (except != FacetKind.Year && Year.HasValue)
            {
                var start = new DateOnly(Year.Value, 1, 1);
                var end = start.AddYears(1);
                query = query.Where(document =>
                    document.DocumentDate.HasValue &&
                    document.DocumentDate.Value >= start &&
                    document.DocumentDate.Value < end);
            }

            return query;
        }

        private static IQueryable<DocumentSearchResultVm> Project(IQueryable<Document> query)
            => query.Select(document => new DocumentSearchResultVm
            {
                Id = document.Id,
                Subject = document.Subject,
                ReceivedFrom = document.ReceivedFrom,
                DocumentDate = document.DocumentDate,
                OfficeCategoryId = document.OfficeCategoryId,
                DocumentCategoryId = document.DocumentCategoryId,
                OfficeCategoryName = document.OfficeCategory != null ? document.OfficeCategory.Name : null,
                DocumentCategoryName = document.DocumentCategory != null ? document.DocumentCategory.Name : null,
                FileSizeBytes = document.FileSizeBytes,
                CreatedAtUtc = document.CreatedAtUtc,
                IsActive = document.IsActive,
                OcrStatus = document.OcrStatus,
                OcrFailureReason = document.OcrFailureReason,
                IsAots = document.IsAots
            });

        private static IQueryable<DocumentSearchResultVm> ApplySort(IQueryable<DocumentSearchResultVm> rows, DocRepoSort sort)
            => sort switch
            {
                // Search results arrive ordered by relevance.
                DocRepoSort.Relevance => rows,
                DocRepoSort.Oldest => rows
                    .OrderByDescending(row => row.DocumentDate.HasValue)
                    .ThenBy(row => row.DocumentDate)
                    .ThenBy(row => row.CreatedAtUtc),
                DocRepoSort.RecentlyAdded => rows.OrderByDescending(row => row.CreatedAtUtc),
                DocRepoSort.Title => rows.OrderBy(row => row.Subject).ThenByDescending(row => row.DocumentDate),
                _ => rows
                    .OrderByDescending(row => row.DocumentDate.HasValue)
                    .ThenByDescending(row => row.DocumentDate)
                    .ThenByDescending(row => row.CreatedAtUtc)
            };

        private IQueryable<Document> Searched(IQueryable<Document> query, string? preparedQuery)
            => preparedQuery is null ? query : _searchService.ApplyMatch(query, preparedQuery);

        private async Task LoadFacetsAsync(IQueryable<Document> scoped, string? preparedQuery)
        {
            var officeCounts = await Searched(ApplyFilters(scoped, FacetKind.Office), preparedQuery)
                .GroupBy(document => document.OfficeCategoryId)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.Key, item => item.Count);

            var typeCounts = await Searched(ApplyFilters(scoped, FacetKind.Type), preparedQuery)
                .GroupBy(document => document.DocumentCategoryId)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.Key, item => item.Count);

            var yearCounts = await Searched(ApplyFilters(scoped, FacetKind.Year), preparedQuery)
                .Where(document => document.DocumentDate.HasValue)
                .GroupBy(document => document.DocumentDate!.Value.Year)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToListAsync();

            // Inactive categories still appear while documents use them.
            var usedOffices = officeCounts.Keys.ToList();
            var usedTypes = typeCounts.Keys.ToList();
            var offices = await _db.OfficeCategories.AsNoTracking()
                .Where(office => office.IsActive || usedOffices.Contains(office.Id))
                .OrderBy(office => office.SortOrder).ThenBy(office => office.Name)
                .Select(office => new { office.Id, office.Name })
                .ToListAsync();

            var types = await _db.DocumentCategories.AsNoTracking()
                .Where(category => category.IsActive || usedTypes.Contains(category.Id))
                .OrderBy(category => category.SortOrder).ThenBy(category => category.Name)
                .Select(category => new { category.Id, category.Name })
                .ToListAsync();

            OfficeFacets = offices
                .Select(office => new FacetOption(office.Id.ToString(CultureInfo.InvariantCulture), office.Name, officeCounts.GetValueOrDefault(office.Id)))
                .ToList();
            TypeFacets = types
                .Select(type => new FacetOption(type.Id.ToString(CultureInfo.InvariantCulture), type.Name, typeCounts.GetValueOrDefault(type.Id)))
                .ToList();

            var years = yearCounts
                .OrderByDescending(item => item.Key)
                .Select(item => new FacetOption(item.Key.ToString(CultureInfo.InvariantCulture), item.Key.ToString(CultureInfo.InvariantCulture), item.Count))
                .ToList();
            if (Year.HasValue && years.All(item => item.Value != Year.Value.ToString(CultureInfo.InvariantCulture)))
            {
                // A year from a link with no documents still shows as the current choice.
                years.Insert(0, new FacetOption(Year.Value.ToString(CultureInfo.InvariantCulture), Year.Value.ToString(CultureInfo.InvariantCulture), 0));
            }

            YearFacets = years;
        }

        private async Task LoadSummaryAsync(string? userId)
        {
            var library = _access.Readable(_db.Documents.AsNoTracking(), canViewInactive: false)
                .Where(document => !document.IsExternal);
            var now = DateTime.UtcNow;
            var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var byOcr = await library
                .GroupBy(document => document.OcrStatus)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToListAsync();

            Summary = new RepositorySummary
            {
                Documents = byOcr.Sum(item => item.Count),
                AddedThisMonth = await library.CountAsync(document => document.CreatedAtUtc >= monthStart),
                AddedLast30Days = await library.CountAsync(document => document.CreatedAtUtc >= now.AddDays(-30)),
                Aots = await library.CountAsync(document => document.IsAots),
                AotsUnread = await _aotsUnread.GetUnreadCountAsync(userId),
                Favourites = string.IsNullOrWhiteSpace(userId)
                    ? 0
                    : await library.CountAsync(document => _db.DocRepoFavourites.Any(f => f.UserId == userId && f.DocumentId == document.Id)),
                TextReady = byOcr.Where(item => item.Key == DocOcrStatus.Succeeded).Sum(item => item.Count),
                TextPending = byOcr.Where(item => item.Key == DocOcrStatus.Pending).Sum(item => item.Count),
                TextFailed = byOcr.Where(item => item.Key == DocOcrStatus.Failed).Sum(item => item.Count),
                Inactive = CanViewInactive
                    ? await _access.Readable(_db.Documents.AsNoTracking(), canViewInactive: true)
                        .CountAsync(document => !document.IsExternal && !document.IsActive)
                    : 0
            };
        }

        private async Task LoadPermissionsAsync()
        {
            CanViewInactive = await _access.CanViewInactiveAsync(User);
            CanUpload = await IsAllowedAsync(Policies.Documents.Upload);
            CanEdit = await IsAllowedAsync(Policies.Documents.EditMetadata);
            CanManage = await IsAllowedAsync(Policies.Documents.SoftDelete);
            CanApproveDeletes = await IsAllowedAsync(Policies.Documents.DeleteApprove);
            CanPurge = await IsAllowedAsync(Policies.Documents.Purge);
            CanManageCategories = await IsAllowedAsync(Policies.Documents.ManageCategories);
        }

        private async Task<bool> IsAllowedAsync(string policy)
            => (await _authorization.AuthorizeAsync(User, policy)).Succeeded;

        private string BuildReturnUrl()
        {
            var query = QueryHelpers.ParseQuery(Request.QueryString.ToString())
                .Where(kv => !string.Equals(kv.Key, "partial", StringComparison.OrdinalIgnoreCase))
                .SelectMany(kv => kv.Value, (kv, value) => new KeyValuePair<string, string>(kv.Key, value ?? string.Empty));
            return $"{Request.PathBase}{Request.Path}{new QueryBuilder(query)}";
        }

        // SECTION: Per-row state
        private async Task ApplyTagsAsync(IReadOnlyCollection<DocumentSearchResultVm> items)
        {
            if (items.Count == 0)
            {
                return;
            }

            var ids = items.Select(item => item.Id).ToList();
            var tags = await _db.DocumentTags.AsNoTracking()
                .Where(documentTag => ids.Contains(documentTag.DocumentId))
                .Select(documentTag => new { documentTag.DocumentId, documentTag.Tag.Name })
                .ToListAsync();
            var byDocument = tags.ToLookup(tag => tag.DocumentId, tag => tag.Name);
            foreach (var item in items)
            {
                item.Tags = byDocument[item.Id].OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        private async Task ApplyPersonalStateAsync(string? userId, IReadOnlyCollection<DocumentSearchResultVm> items)
        {
            if (string.IsNullOrWhiteSpace(userId) || items.Count == 0)
            {
                return;
            }

            var ids = items.Select(item => item.Id).ToList();
            var favourites = (await _db.DocRepoFavourites.AsNoTracking()
                .Where(favourite => favourite.UserId == userId && ids.Contains(favourite.DocumentId))
                .Select(favourite => favourite.DocumentId)
                .ToListAsync()).ToHashSet();
            var seen = (await _db.DocRepoAotsViews.AsNoTracking()
                .Where(view => view.UserId == userId && ids.Contains(view.DocumentId))
                .Select(view => view.DocumentId)
                .ToListAsync()).ToHashSet();

            foreach (var item in items)
            {
                item.IsFavourite = favourites.Contains(item.Id);
                item.IsAotsUnread = item.IsAots && !seen.Contains(item.Id);
            }
        }
    }

    // SECTION: Page models
    public sealed record FacetOption(string Value, string Label, int Count);

    public sealed class RepositorySummary
    {
        public int Documents { get; init; }
        public int AddedThisMonth { get; init; }
        public int AddedLast30Days { get; init; }
        public int Aots { get; init; }
        public int AotsUnread { get; init; }
        public int Favourites { get; init; }
        public int TextReady { get; init; }
        public int TextPending { get; init; }
        public int TextFailed { get; init; }
        public int Inactive { get; init; }
        public int TextReadyPercent => Documents == 0 ? 0 : (int)Math.Round(100.0 * TextReady / Documents);
    }

    public enum DocRepoSort
    {
        Relevance,
        Newest,
        Oldest,
        RecentlyAdded,
        Title
    }

    public static class DocRepoSortParser
    {
        public static readonly IReadOnlyList<(DocRepoSort Sort, string Value, string Label)> Options = new[]
        {
            (DocRepoSort.Relevance, "relevance", "Best match"),
            (DocRepoSort.Newest, "newest", "Newest first"),
            (DocRepoSort.Oldest, "oldest", "Oldest first"),
            (DocRepoSort.RecentlyAdded, "added", "Recently added"),
            (DocRepoSort.Title, "title", "Title A–Z")
        };

        /// <summary>Relevance applies only to a search; without one the default is newest first.</summary>
        public static DocRepoSort Parse(string? value, bool hasSearch)
        {
            var match = Options.FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase));
            if (match.Value is null)
            {
                return hasSearch ? DocRepoSort.Relevance : DocRepoSort.Newest;
            }

            return match.Sort == DocRepoSort.Relevance && !hasSearch ? DocRepoSort.Newest : match.Sort;
        }

        public static string ValueOf(DocRepoSort sort) => Options.First(option => option.Sort == sort).Value;
    }
}

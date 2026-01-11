using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ProjectManagement.Areas.DocumentRepository.Models;
using ProjectManagement.Data;
using ProjectManagement.Data.DocRepo;
using ProjectManagement.Services.DocRepo;

namespace ProjectManagement.Areas.DocumentRepository.Pages.Documents
{
    [Authorize(Policy = "DocRepo.View")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IDocumentSearchService _searchService;
        private readonly IConfiguration _configuration;

        public IndexModel(ApplicationDbContext db, IDocumentSearchService searchService, IConfiguration configuration)
        {
            _db = db;
            _searchService = searchService;
            _configuration = configuration;
        }

        // SECTION: Filters
        [FromQuery(Name = "q")] public string? Q { get; set; }
        [FromQuery(Name = "tag")] public string? Tag { get; set; }
        [FromQuery(Name = "officeCategoryId")] public long? OfficeCategoryId { get; set; }
        [FromQuery(Name = "documentCategoryId")] public long? DocumentCategoryId { get; set; }
        [FromQuery(Name = "year")] public int? Year { get; set; }
        [FromQuery(Name = "includeInactive")] public bool IncludeInactive { get; set; }
        [FromQuery(Name = "view")] public string? View { get; set; }

        // SECTION: View state
        public bool EnableListViewUxUpgrade { get; private set; }
        public bool EnableAutoApplyFilters { get; private set; }
        public string ViewMode { get; private set; } = "cards";
        public bool IsListView => ViewMode.Equals("list", StringComparison.OrdinalIgnoreCase);

        // SECTION: Paging
        private const int DefaultPageSize = 30;

        [FromQuery(Name = "page")]
        public int PageNumber { get; set; } = 1;

        [FromQuery]
        public int PageSize { get; set; } = DefaultPageSize;

        public int TotalCount { get; private set; }
        public int PageCount => (int)Math.Ceiling((double)TotalCount / PageSize);

        // SECTION: Result sets
        public List<DocumentSearchResultVm> Items { get; private set; } = new();
        public List<DocumentListItemVm> ListItems { get; private set; } = new();
        public bool IsSearchActive { get; private set; }
        public bool HasTagMatch { get; private set; }

        // SECTION: Lookup collections for filters
        public List<OfficeCategory> OfficeCategories { get; private set; } = new();
        public List<DocumentCategory> DocumentCategories { get; private set; } = new();

        // SECTION: View URL helper
        public string UrlForView(string viewMode)
        {
            var query = QueryHelpers.ParseQuery(Request.QueryString.ToString())
                .ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.OrdinalIgnoreCase);

            query["view"] = viewMode;

            var q = string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            var path = Request.Path.HasValue ? Request.Path.Value : string.Empty;
            return string.IsNullOrEmpty(q) ? path : $"{path}?{q}";
        }

        public async Task OnGetAsync()
        {
            // SECTION: Feature flags
            EnableListViewUxUpgrade = _configuration.GetValue<bool>("DocRepo:EnableListViewUxUpgrade");
            EnableAutoApplyFilters = _configuration.GetValue<bool>("DocRepo:EnableAutoApplyFilters");

            ViewMode = EnableListViewUxUpgrade && string.Equals(View, "cards", StringComparison.OrdinalIgnoreCase)
                ? "cards"
                : "list";

            if (!EnableListViewUxUpgrade)
            {
                ViewMode = "cards";
            }

            // SECTION: Guard paging input
            if (PageNumber < 1)
            {
                PageNumber = 1;
            }

            if (PageSize < 10 || PageSize > 200)
            {
                PageSize = DefaultPageSize;
            }

            // SECTION: Base query
        IQueryable<Document> query = _db.Documents
            .AsNoTracking()
            .Where(document => !document.IsDeleted && !document.IsExternal);

            if (!IncludeInactive)
            {
                query = query.Where(document => document.IsActive);
            }

            // SECTION: Tag filter
            if (!string.IsNullOrWhiteSpace(Tag))
            {
                var trimmedTag = Tag.Trim();
                var loweredTag = trimmedTag.ToLowerInvariant();

                query = query.Where(document => document.DocumentTags.Any(documentTag =>
                    documentTag.Tag.Name == trimmedTag ||
                    documentTag.Tag.NormalizedName == loweredTag));
            }

            // SECTION: Office category filter
            if (OfficeCategoryId.HasValue)
            {
                var officeId = OfficeCategoryId.Value;
                query = query.Where(document => document.OfficeCategoryId == officeId);
            }

            // SECTION: Document category filter
            if (DocumentCategoryId.HasValue)
            {
                var documentCategoryId = DocumentCategoryId.Value;
                query = query.Where(document => document.DocumentCategoryId == documentCategoryId);
            }

            // SECTION: Year filter
            if (Year.HasValue)
            {
                var start = new DateOnly(Year.Value, 1, 1);
                var end = start.AddYears(1);

                query = query.Where(document =>
                    document.DocumentDate.HasValue &&
                    document.DocumentDate.Value >= start &&
                    document.DocumentDate.Value < end);
            }

            // SECTION: Search handling
            var hasSearch = _searchService.TryPrepareQuery(Q, out var preparedQuery);
            if (hasSearch)
            {
                Q = preparedQuery;
                IsSearchActive = true;

                var searchedQuery = _searchService.ApplySearch(query, preparedQuery);
                var searchedProjectedQuery = _searchService.ApplySearchProjected(query, preparedQuery);

                TotalCount = await searchedQuery.CountAsync();

                if (IsListView)
                {
                    ListItems = await searchedQuery
                        .Select(document => new DocumentListItemVm
                        {
                            Id = document.Id,
                            Subject = document.Subject,
                            DocumentDate = document.DocumentDate,
                            OfficeName = document.OfficeCategory != null ? document.OfficeCategory.Name : null,
                            DocumentCategoryName = document.DocumentCategory != null ? document.DocumentCategory.Name : null,
                            OcrStatus = document.OcrStatus,
                            IsActive = document.IsActive
                        })
                        .Skip((PageNumber - 1) * PageSize)
                        .Take(PageSize)
                        .ToListAsync();
                }
                else
                {
                    Items = await searchedProjectedQuery
                        .Skip((PageNumber - 1) * PageSize)
                        .Take(PageSize)
                        .ToListAsync();
                }

                HasTagMatch = await searchedProjectedQuery.AnyAsync(item => item.MatchedInTags);
            }
            else
            {
                TotalCount = await query.CountAsync();

                var orderedQuery = query
                    .OrderByDescending(document => document.DocumentDate.HasValue)
                    .ThenByDescending(document => document.DocumentDate)
                    .ThenByDescending(document => document.CreatedAtUtc);

                if (IsListView)
                {
                    ListItems = await orderedQuery
                        .Select(document => new DocumentListItemVm
                        {
                            Id = document.Id,
                            Subject = document.Subject,
                            DocumentDate = document.DocumentDate,
                            OfficeName = document.OfficeCategory != null ? document.OfficeCategory.Name : null,
                            DocumentCategoryName = document.DocumentCategory != null ? document.DocumentCategory.Name : null,
                            OcrStatus = document.OcrStatus,
                            IsActive = document.IsActive
                        })
                        .Skip((PageNumber - 1) * PageSize)
                        .Take(PageSize)
                        .ToListAsync();
                }
                else
                {
                    Items = await orderedQuery
                        .Select(document => new DocumentSearchResultVm
                        {
                            Id = document.Id,
                            Subject = document.Subject,
                            DocumentDate = document.DocumentDate,
                            OfficeCategoryName = document.OfficeCategory != null ? document.OfficeCategory.Name : null,
                            DocumentCategoryName = document.DocumentCategory != null ? document.DocumentCategory.Name : null,
                            Tags = document.DocumentTags
                                .OrderBy(documentTag => documentTag.Tag.Name)
                                .Select(documentTag => documentTag.Tag.Name)
                                .ToList(),
                            IsActive = document.IsActive,
                            OcrStatus = document.OcrStatus,
                            OcrFailureReason = document.OcrFailureReason,
                            Rank = null,
                            Snippet = null,
                            MatchedInSubject = false,
                            MatchedInTags = false,
                            MatchedInBody = false
                        })
                        .Skip((PageNumber - 1) * PageSize)
                        .Take(PageSize)
                        .ToListAsync();
                }

                HasTagMatch = false;
            }

            // SECTION: Lookup data for filter modal
            OfficeCategories = await _db.OfficeCategories
                .AsNoTracking()
                .Where(office => office.IsActive)
                .OrderBy(office => office.SortOrder)
                .ThenBy(office => office.Name)
                .ToListAsync();

            DocumentCategories = await _db.DocumentCategories
                .AsNoTracking()
                .Where(category => category.IsActive)
                .OrderBy(category => category.SortOrder)
                .ThenBy(category => category.Name)
                .ToListAsync();
        }
    }
}

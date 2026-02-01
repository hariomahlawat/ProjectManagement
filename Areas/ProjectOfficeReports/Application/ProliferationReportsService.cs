using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Api;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Utilities.Reporting;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application
{
    // SECTION: Proliferation report queries + export
    public sealed class ProliferationReportsService
    {
        private readonly ApplicationDbContext _db;
        private readonly IProliferationReportExcelWorkbookBuilder _excel;

        public ProliferationReportsService(ApplicationDbContext db, IProliferationReportExcelWorkbookBuilder excel)
        {
            _db = db;
            _excel = excel;
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

        // SECTION: Report execution
        public async Task<ProliferationReportPageDto> RunAsync(ProliferationReportQueryDto q, CancellationToken ct)
        {
            return await RunInternalAsync(q, ct, maxPageSize: 200);
        }

        // SECTION: Report execution (internal)
        private async Task<ProliferationReportPageDto> RunInternalAsync(
            ProliferationReportQueryDto q,
            CancellationToken ct,
            int maxPageSize)
        {
            var page = q.Page < 1 ? 1 : q.Page;
            var pageSize = q.PageSize < 1 ? 50 : Math.Min(q.PageSize, maxPageSize);

            var statusFilter = NormalizeStatus(q.ApprovalStatus);

            DateOnly? fromDate = q.FromDateUtc.HasValue ? DateOnly.FromDateTime(q.FromDateUtc.Value) : null;
            DateOnly? toDate = q.ToDateUtc.HasValue ? DateOnly.FromDateTime(q.ToDateUtc.Value) : null;

            if (q.Report != ProliferationReportKind.YearlyReconciliation
                && fromDate.HasValue
                && toDate.HasValue
                && fromDate.Value > toDate.Value)
            {
                throw new InvalidOperationException("From date must be on or before To date.");
            }

            return q.Report switch
            {
                ProliferationReportKind.ProjectToUnits => await RunProjectToUnitsAsync(q, fromDate, toDate, statusFilter, page, pageSize, ct),
                ProliferationReportKind.UnitToProjects => await RunUnitToProjectsAsync(q, fromDate, toDate, statusFilter, page, pageSize, ct),
                ProliferationReportKind.ProjectCoverageSummary => await RunProjectCoverageSummaryAsync(q, fromDate, toDate, statusFilter, page, pageSize, ct),
                ProliferationReportKind.GranularLedger => await RunGranularLedgerAsync(q, fromDate, toDate, statusFilter, page, pageSize, ct),
                ProliferationReportKind.YearlyReconciliation => await RunYearlyReconciliationAsync(q, statusFilter, page, pageSize, ct),
                _ => throw new InvalidOperationException("Unsupported report kind.")
            };
        }

        // SECTION: Export
        public async Task<(byte[] Content, string FileName)> ExportAsync(ProliferationReportQueryDto q, CancellationToken ct)
        {
            const int maxExportRows = 100_000;

            var pageDto = await RunInternalAsync(new ProliferationReportQueryDto
            {
                Report = q.Report,
                Source = q.Source,
                ProjectId = q.ProjectId,
                UnitName = q.UnitName,
                ProjectCategoryId = q.ProjectCategoryId,
                TechnicalCategoryId = q.TechnicalCategoryId,
                FromDateUtc = q.FromDateUtc,
                ToDateUtc = q.ToDateUtc,
                ApprovalStatus = q.ApprovalStatus,
                Page = 1,
                PageSize = maxExportRows
            }, ct, maxExportRows);

            var columns = pageDto.Columns.Select(c => (c.Key, c.Label)).ToList();
            var rows = pageDto.Rows.Select(RowToDictionary).ToList();
            var filters = await BuildFilterDictionaryAsync(q, ct);

            var title = $"Proliferation Report: {q.Report}";
            var bytes = _excel.Build(q.Report, columns, rows, title, filters);

            var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
            var fileName = $"proliferation-report-{q.Report.ToString().ToLowerInvariant()}-{ts}.xlsx";

            return (bytes, fileName);
        }

        // SECTION: Helpers
        private static string EscapeLikePattern(string value)
        {
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal);
        }

        private static IDictionary<string, object?> RowToDictionary(ProliferationReportRowDto r)
        {
            return new Dictionary<string, object?>
            {
                ["projectName"] = r.ProjectName,
                ["projectCode"] = r.ProjectCode,
                ["sourceLabel"] = r.SourceLabel,
                ["unitName"] = r.UnitName,
                ["proliferationDate"] = r.ProliferationDateUtc?.Date,
                ["year"] = r.Year,
                ["quantity"] = r.Quantity,
                ["totalQuantity"] = r.TotalQuantity,
                ["uniqueUnits"] = r.UniqueUnits,
                ["firstDate"] = r.FirstProliferationDateUtc?.Date,
                ["lastDate"] = r.LastProliferationDateUtc?.Date,
                ["yearlyApprovedTotal"] = r.YearlyApprovedTotal,
                ["granularApprovedTotal"] = r.GranularApprovedTotal,
                ["preferenceMode"] = r.PreferenceMode,
                ["effectiveTotal"] = r.EffectiveTotal,
                ["remarks"] = r.Remarks,
                ["approvalStatus"] = r.ApprovalStatus
            };
        }

        // SECTION: Export filter labels
        private async Task<IDictionary<string, string>> BuildFilterDictionaryAsync(ProliferationReportQueryDto q, CancellationToken ct)
        {
            string projectLabel = string.Empty;
            if (q.ProjectId.HasValue)
            {
                var p = await _db.Projects
                    .AsNoTracking()
                    .Where(x => x.Id == q.ProjectId.Value)
                    .Select(x => new { x.Name, x.CaseFileNumber })
                    .FirstOrDefaultAsync(ct);

                if (p != null)
                {
                    projectLabel = string.IsNullOrWhiteSpace(p.CaseFileNumber)
                        ? p.Name
                        : $"{p.Name} ({p.CaseFileNumber})";
                }
            }

            string projectCategoryName = string.Empty;
            if (q.ProjectCategoryId.HasValue)
            {
                projectCategoryName = await _db.ProjectCategories
                    .AsNoTracking()
                    .Where(x => x.Id == q.ProjectCategoryId.Value)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync(ct) ?? string.Empty;
            }

            string technicalCategoryName = string.Empty;
            if (q.TechnicalCategoryId.HasValue)
            {
                technicalCategoryName = await _db.TechnicalCategories
                    .AsNoTracking()
                    .Where(x => x.Id == q.TechnicalCategoryId.Value)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync(ct) ?? string.Empty;
            }

            return new Dictionary<string, string>
            {
                ["Report"] = q.Report.ToString(),
                ["Source"] = q.Source?.ToDisplayName() ?? "All",
                ["Approval status"] = string.IsNullOrWhiteSpace(q.ApprovalStatus) ? "Approved" : q.ApprovalStatus!.Trim(),
                ["Project"] = projectLabel,
                ["Unit name"] = q.UnitName?.Trim() ?? string.Empty,
                ["From"] = q.FromDateUtc?.ToString("yyyy-MM-dd") ?? string.Empty,
                ["To"] = q.ToDateUtc?.ToString("yyyy-MM-dd") ?? string.Empty,
                ["Project category"] = projectCategoryName,
                ["Technical category"] = technicalCategoryName
            };
        }

        private static string NormalizeStatus(string? status)
        {
            var s = (status ?? string.Empty).Trim();
            if (s.Equals("All", StringComparison.OrdinalIgnoreCase)) return "All";
            if (s.Equals(nameof(ApprovalStatus.Pending), StringComparison.OrdinalIgnoreCase)) return nameof(ApprovalStatus.Pending);
            if (s.Equals(nameof(ApprovalStatus.Rejected), StringComparison.OrdinalIgnoreCase)) return nameof(ApprovalStatus.Rejected);
            return nameof(ApprovalStatus.Approved);
        }

        private IQueryable<Project> EligibleProjectsQuery(int? projectCategoryId, int? technicalCategoryId)
        {
            var projects = _db.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Completed);

            if (projectCategoryId.HasValue)
            {
                projects = projects.Where(p => p.CategoryId == projectCategoryId.Value);
            }

            if (technicalCategoryId.HasValue)
            {
                projects = projects.Where(p => p.TechnicalCategoryId == technicalCategoryId.Value);
            }

            return projects;
        }

        private IQueryable<ProliferationGranular> GranularBase(
            ProliferationReportQueryDto q,
            DateOnly? fromDate,
            DateOnly? toDate,
            string statusFilter)
        {
            var g = _db.ProliferationGranularEntries.AsNoTracking();

            if (q.Source.HasValue)
            {
                g = g.Where(x => x.Source == q.Source.Value);
            }

            if (fromDate.HasValue) g = g.Where(x => x.ProliferationDate >= fromDate.Value);
            if (toDate.HasValue) g = g.Where(x => x.ProliferationDate <= toDate.Value);

            if (statusFilter != "All")
            {
                var parsed = Enum.Parse<ApprovalStatus>(statusFilter, ignoreCase: true);
                g = g.Where(x => x.ApprovalStatus == parsed);
            }

            if (q.ProjectId.HasValue)
            {
                g = g.Where(x => x.ProjectId == q.ProjectId.Value);
            }

            if (!string.IsNullOrWhiteSpace(q.UnitName))
            {
                var unit = q.UnitName.Trim();
                var like = $"%{EscapeLikePattern(unit)}%";
                g = g.Where(x => x.UnitName != null && EF.Functions.ILike(x.UnitName, like, "\\"));
            }

            return g;
        }

        // SECTION: Project to units
        private async Task<ProliferationReportPageDto> RunProjectToUnitsAsync(
            ProliferationReportQueryDto q,
            DateOnly? fromDate,
            DateOnly? toDate,
            string statusFilter,
            int page,
            int pageSize,
            CancellationToken ct)
        {
            if (!q.ProjectId.HasValue)
            {
                return Empty(q.Report, ProjectToUnitsColumns(), page, pageSize);
            }

            var projects = EligibleProjectsQuery(q.ProjectCategoryId, q.TechnicalCategoryId);

            var baseQuery = from g in GranularBase(q, fromDate, toDate, statusFilter)
                            join p in projects on g.ProjectId equals p.Id
                            select new { g, p };

            var total = await baseQuery.CountAsync(ct);

            var rows = await baseQuery
                .OrderByDescending(x => x.g.ProliferationDate)
                .ThenBy(x => x.g.UnitName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new ProliferationReportRowDto
                {
                    ProjectId = x.p.Id,
                    ProjectName = x.p.Name,
                    ProjectCode = x.p.CaseFileNumber,
                    Source = x.g.Source,
                    SourceLabel = x.g.Source.ToDisplayName(),
                    UnitName = x.g.UnitName,
                    ProliferationDateUtc = x.g.ProliferationDate.ToDateTime(TimeOnly.MinValue),
                    Year = x.g.ProliferationDate.Year,
                    Quantity = x.g.Quantity,
                    Remarks = x.g.Remarks,
                    ApprovalStatus = x.g.ApprovalStatus.ToString()
                })
                .ToListAsync(ct);

            return new ProliferationReportPageDto
            {
                Report = q.Report,
                Columns = ProjectToUnitsColumns(),
                Rows = rows,
                Total = total,
                Page = page,
                PageSize = pageSize
            };
        }

        // SECTION: Unit to projects
        private async Task<ProliferationReportPageDto> RunUnitToProjectsAsync(
            ProliferationReportQueryDto q,
            DateOnly? fromDate,
            DateOnly? toDate,
            string statusFilter,
            int page,
            int pageSize,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(q.UnitName))
            {
                return Empty(q.Report, UnitToProjectsColumns(), page, pageSize);
            }

            var projects = EligibleProjectsQuery(q.ProjectCategoryId, q.TechnicalCategoryId);

            var baseQuery = from g in GranularBase(q, fromDate, toDate, statusFilter)
                            join p in projects on g.ProjectId equals p.Id
                            select new { g, p };

            var total = await baseQuery.CountAsync(ct);

            var rows = await baseQuery
                .OrderByDescending(x => x.g.ProliferationDate)
                .ThenBy(x => x.p.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new ProliferationReportRowDto
                {
                    UnitName = x.g.UnitName,
                    ProjectId = x.p.Id,
                    ProjectName = x.p.Name,
                    ProjectCode = x.p.CaseFileNumber,
                    Source = x.g.Source,
                    SourceLabel = x.g.Source.ToDisplayName(),
                    ProliferationDateUtc = x.g.ProliferationDate.ToDateTime(TimeOnly.MinValue),
                    Year = x.g.ProliferationDate.Year,
                    Quantity = x.g.Quantity,
                    Remarks = x.g.Remarks,
                    ApprovalStatus = x.g.ApprovalStatus.ToString()
                })
                .ToListAsync(ct);

            return new ProliferationReportPageDto
            {
                Report = q.Report,
                Columns = UnitToProjectsColumns(),
                Rows = rows,
                Total = total,
                Page = page,
                PageSize = pageSize
            };
        }

        // SECTION: Project coverage summary
        private async Task<ProliferationReportPageDto> RunProjectCoverageSummaryAsync(
            ProliferationReportQueryDto q,
            DateOnly? fromDate,
            DateOnly? toDate,
            string statusFilter,
            int page,
            int pageSize,
            CancellationToken ct)
        {
            var projects = EligibleProjectsQuery(q.ProjectCategoryId, q.TechnicalCategoryId);

            var baseQuery = from g in GranularBase(q, fromDate, toDate, statusFilter)
                            join p in projects on g.ProjectId equals p.Id
                            select new { g, p };

            var grouped = baseQuery
                .GroupBy(x => new { x.p.Id, x.p.Name, x.p.CaseFileNumber, x.g.Source })
                .Select(grp => new
                {
                    grp.Key.Id,
                    grp.Key.Name,
                    grp.Key.CaseFileNumber,
                    Source = grp.Key.Source,
                    TotalQty = grp.Sum(x => x.g.Quantity),
                    UniqueUnits = grp.Select(x => x.g.UnitName).Where(u => u != null).Distinct().Count(),
                    FirstDate = grp.Min(x => x.g.ProliferationDate),
                    LastDate = grp.Max(x => x.g.ProliferationDate)
                });

            var total = await grouped.CountAsync(ct);

            var rows = await grouped
                .OrderBy(x => x.Name)
                .ThenBy(x => x.Source)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new ProliferationReportRowDto
                {
                    ProjectId = x.Id,
                    ProjectName = x.Name,
                    ProjectCode = x.CaseFileNumber,
                    Source = x.Source,
                    SourceLabel = x.Source.ToDisplayName(),
                    TotalQuantity = x.TotalQty,
                    UniqueUnits = x.UniqueUnits,
                    FirstProliferationDateUtc = x.FirstDate.ToDateTime(TimeOnly.MinValue),
                    LastProliferationDateUtc = x.LastDate.ToDateTime(TimeOnly.MinValue)
                })
                .ToListAsync(ct);

            return new ProliferationReportPageDto
            {
                Report = q.Report,
                Columns = ProjectCoverageColumns(),
                Rows = rows,
                Total = total,
                Page = page,
                PageSize = pageSize
            };
        }

        // SECTION: Granular ledger
        private async Task<ProliferationReportPageDto> RunGranularLedgerAsync(
            ProliferationReportQueryDto q,
            DateOnly? fromDate,
            DateOnly? toDate,
            string statusFilter,
            int page,
            int pageSize,
            CancellationToken ct)
        {
            var projects = EligibleProjectsQuery(q.ProjectCategoryId, q.TechnicalCategoryId);

            var baseQuery = from g in GranularBase(q, fromDate, toDate, statusFilter)
                            join p in projects on g.ProjectId equals p.Id
                            select new { g, p };

            var total = await baseQuery.CountAsync(ct);

            var rows = await baseQuery
                .OrderByDescending(x => x.g.ProliferationDate)
                .ThenBy(x => x.p.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new ProliferationReportRowDto
                {
                    ProjectId = x.p.Id,
                    ProjectName = x.p.Name,
                    ProjectCode = x.p.CaseFileNumber,
                    Source = x.g.Source,
                    SourceLabel = x.g.Source.ToDisplayName(),
                    UnitName = x.g.UnitName,
                    ProliferationDateUtc = x.g.ProliferationDate.ToDateTime(TimeOnly.MinValue),
                    Year = x.g.ProliferationDate.Year,
                    Quantity = x.g.Quantity,
                    Remarks = x.g.Remarks,
                    ApprovalStatus = x.g.ApprovalStatus.ToString()
                })
                .ToListAsync(ct);

            return new ProliferationReportPageDto
            {
                Report = q.Report,
                Columns = GranularLedgerColumns(),
                Rows = rows,
                Total = total,
                Page = page,
                PageSize = pageSize
            };
        }

        // SECTION: Yearly reconciliation
        private async Task<ProliferationReportPageDto> RunYearlyReconciliationAsync(
            ProliferationReportQueryDto q,
            string statusFilter,
            int page,
            int pageSize,
            CancellationToken ct)
        {
            var projects = EligibleProjectsQuery(q.ProjectCategoryId, q.TechnicalCategoryId);

            var yearlies = _db.ProliferationYearlies.AsNoTracking();
            var granular = _db.ProliferationGranularEntries.AsNoTracking();
            var prefs = _db.ProliferationYearPreferences.AsNoTracking();

            // SECTION: Filters
            if (q.Source.HasValue)
            {
                yearlies = yearlies.Where(x => x.Source == q.Source.Value);
                granular = granular.Where(x => x.Source == q.Source.Value);
                prefs = prefs.Where(x => x.Source == q.Source.Value);
            }

            if (statusFilter != "All")
            {
                var parsed = Enum.Parse<ApprovalStatus>(statusFilter, ignoreCase: true);
                yearlies = yearlies.Where(x => x.ApprovalStatus == parsed);
                granular = granular.Where(x => x.ApprovalStatus == parsed);
            }

            if (q.ProjectId.HasValue)
            {
                yearlies = yearlies.Where(x => x.ProjectId == q.ProjectId.Value);
                granular = granular.Where(x => x.ProjectId == q.ProjectId.Value);
                prefs = prefs.Where(x => x.ProjectId == q.ProjectId.Value);
            }

            // SECTION: Aggregations
            var granularAgg = granular
                .GroupBy(x => new { x.ProjectId, x.Source, Year = x.ProliferationDate.Year })
                .Select(g => new
                {
                    g.Key.ProjectId,
                    g.Key.Source,
                    g.Key.Year,
                    GranularTotal = g.Sum(x => x.Quantity)
                });

            var yearlyAgg = yearlies
                .GroupBy(x => new { x.ProjectId, x.Source, x.Year })
                .Select(g => new
                {
                    g.Key.ProjectId,
                    g.Key.Source,
                    Year = g.Key.Year,
                    YearlyTotal = g.Sum(x => x.TotalQuantity)
                });

            // SECTION: Merge yearly + granular totals
            var merged = yearlyAgg
                .Select(x => new
                {
                    x.ProjectId,
                    x.Source,
                    x.Year,
                    x.YearlyTotal,
                    GranularTotal = 0
                })
                .Concat(granularAgg.Select(x => new
                {
                    x.ProjectId,
                    x.Source,
                    x.Year,
                    YearlyTotal = 0,
                    x.GranularTotal
                }))
                .GroupBy(x => new { x.ProjectId, x.Source, x.Year })
                .Select(g => new
                {
                    g.Key.ProjectId,
                    g.Key.Source,
                    g.Key.Year,
                    YearlyTotal = g.Sum(x => x.YearlyTotal),
                    GranularTotal = g.Sum(x => x.GranularTotal)
                });

            var withPrefs = from m in merged
                            join pref in prefs on new { m.ProjectId, m.Source, Year = m.Year } equals new { pref.ProjectId, pref.Source, Year = pref.Year } into pj
                            from pref in pj.DefaultIfEmpty()
                            select new
                            {
                                m.ProjectId,
                                m.Source,
                                m.Year,
                                m.YearlyTotal,
                                m.GranularTotal,
                                Mode = pref != null ? pref.Mode : YearPreferenceMode.UseYearlyAndGranular
                            };

            var withProjects = from x in withPrefs
                               join p in projects on x.ProjectId equals p.Id
                               select new
                               {
                                   p.Id,
                                   p.Name,
                                   p.CaseFileNumber,
                                   x.Source,
                                   x.Year,
                                   x.YearlyTotal,
                                   x.GranularTotal,
                                   x.Mode
                               };

            var total = await withProjects.CountAsync(ct);

            var pageRows = await withProjects
                .OrderByDescending(x => x.Year)
                .ThenBy(x => x.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var rows = pageRows.Select(x =>
            {
                var effective = ComputeEffectiveTotal(x.Source, x.Mode, x.YearlyTotal, x.GranularTotal);
                return new ProliferationReportRowDto
                {
                    ProjectId = x.Id,
                    ProjectName = x.Name,
                    ProjectCode = x.CaseFileNumber,
                    Source = x.Source,
                    SourceLabel = x.Source.ToDisplayName(),
                    Year = x.Year,
                    YearlyApprovedTotal = x.YearlyTotal,
                    GranularApprovedTotal = x.GranularTotal,
                    PreferenceMode = x.Mode.ToString(),
                    EffectiveTotal = effective
                };
            }).ToList();

            return new ProliferationReportPageDto
            {
                Report = q.Report,
                Columns = YearlyReconciliationColumns(),
                Rows = rows,
                Total = total,
                Page = page,
                PageSize = pageSize
            };
        }

        private static int ComputeEffectiveTotal(ProliferationSource source, YearPreferenceMode mode, int yearly, int granular)
        {
            if (source == ProliferationSource.Abw515)
            {
                return yearly;
            }

            return mode switch
            {
                YearPreferenceMode.UseYearly => yearly,
                YearPreferenceMode.UseGranular => granular,
                YearPreferenceMode.Auto => granular > 0 ? granular : yearly,
                YearPreferenceMode.UseYearlyAndGranular => yearly + granular,
                _ => yearly + granular
            };
        }

        private static ProliferationReportPageDto Empty(ProliferationReportKind report, IReadOnlyList<ProliferationReportColumnDto> cols, int page, int pageSize)
        {
            return new ProliferationReportPageDto
            {
                Report = report,
                Columns = cols,
                Rows = Array.Empty<ProliferationReportRowDto>(),
                Total = 0,
                Page = page,
                PageSize = pageSize
            };
        }

        // SECTION: Column definitions
        private static IReadOnlyList<ProliferationReportColumnDto> ProjectToUnitsColumns() => new[]
        {
            Col("projectName", "Project"),
            Col("projectCode", "Code"),
            Col("sourceLabel", "Source"),
            Col("unitName", "Unit"),
            Col("proliferationDate", "Proliferation date"),
            Col("year", "Year"),
            Col("quantity", "Quantity"),
            Col("remarks", "Remarks"),
            Col("approvalStatus", "Status")
        };

        private static IReadOnlyList<ProliferationReportColumnDto> UnitToProjectsColumns() => new[]
        {
            Col("unitName", "Unit"),
            Col("projectName", "Project"),
            Col("projectCode", "Code"),
            Col("sourceLabel", "Source"),
            Col("proliferationDate", "Proliferation date"),
            Col("year", "Year"),
            Col("quantity", "Quantity"),
            Col("remarks", "Remarks"),
            Col("approvalStatus", "Status")
        };

        private static IReadOnlyList<ProliferationReportColumnDto> ProjectCoverageColumns() => new[]
        {
            Col("projectName", "Project"),
            Col("projectCode", "Code"),
            Col("sourceLabel", "Source"),
            Col("totalQuantity", "Total quantity"),
            Col("uniqueUnits", "Unique units"),
            Col("firstDate", "First proliferation date"),
            Col("lastDate", "Last proliferation date")
        };

        private static IReadOnlyList<ProliferationReportColumnDto> GranularLedgerColumns() => new[]
        {
            Col("projectName", "Project"),
            Col("projectCode", "Code"),
            Col("sourceLabel", "Source"),
            Col("proliferationDate", "Proliferation date"),
            Col("unitName", "Unit"),
            Col("quantity", "Quantity"),
            Col("remarks", "Remarks"),
            Col("approvalStatus", "Status")
        };

        private static IReadOnlyList<ProliferationReportColumnDto> YearlyReconciliationColumns() => new[]
        {
            Col("projectName", "Project"),
            Col("projectCode", "Code"),
            Col("sourceLabel", "Source"),
            Col("year", "Year"),
            Col("yearlyApprovedTotal", "Yearly approved total"),
            Col("granularApprovedTotal", "Granular approved total"),
            Col("preferenceMode", "Preference mode"),
            Col("effectiveTotal", "Effective total")
        };

        private static ProliferationReportColumnDto Col(string key, string label) => new ProliferationReportColumnDto { Key = key, Label = label };
    }
}

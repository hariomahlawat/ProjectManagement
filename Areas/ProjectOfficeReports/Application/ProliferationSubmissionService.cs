using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Api;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application
{
    public sealed class ServiceResult
    {
        public bool Success { get; private set; }
        public string? Error { get; private set; }
        public static ServiceResult Ok() => new() { Success = true };
        public static ServiceResult Fail(string error) => new() { Success = false, Error = error };
    }

    public partial class ProliferationSubmissionService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _users;
        private readonly IClock _clock;
        private readonly IAuditService _audit;

        public ProliferationSubmissionService(
            ApplicationDbContext db,
            UserManager<ApplicationUser> users,
            IClock clock,
            IAuditService audit)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _users = users ?? throw new ArgumentNullException(nameof(users));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        }

        public async Task<ServiceResult> CreateYearlyAsync(ProliferationYearlyCreateDto dto, ClaimsPrincipal user, CancellationToken ct)
        {
            if (dto is null) throw new ArgumentNullException(nameof(dto));
            if (user is null) throw new ArgumentNullException(nameof(user));

            var actor = await _users.GetUserAsync(user);
            if (actor is null)
            {
                return ServiceResult.Fail("User not found.");
            }

            var project = await GetCompletedProjectAsync(dto.ProjectId, ct);
            if (project is null)
            {
                return ServiceResult.Fail("Only completed projects may record proliferation data.");
            }

            if (dto.TotalQuantity < 0)
            {
                return ServiceResult.Fail("Total quantity must be zero or greater.");
            }

            if (dto.Source != ProliferationSource.Sdd && dto.Source != ProliferationSource.Abw515)
            {
                return ServiceResult.Fail("Unsupported source.");
            }

            var now = _clock.UtcNow.UtcDateTime;
            var remarks = Normalize(dto.Remarks, 500);

            var entity = new ProliferationYearly
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Source = dto.Source,
                Year = dto.Year,
                TotalQuantity = dto.TotalQuantity,
                Remarks = remarks,
                SubmittedByUserId = actor.Id,
                CreatedOnUtc = now,
                LastUpdatedOnUtc = now,
                RowVersion = Array.Empty<byte>()
            };

            if (RequiresImmediateApproval(user))
            {
                var duplicate = await _db.ProliferationYearlies
                    .AsNoTracking()
                    .AnyAsync(y => y.ProjectId == entity.ProjectId && y.Source == entity.Source && y.Year == entity.Year && y.ApprovalStatus == ApprovalStatus.Approved, ct);
                if (duplicate)
                {
                    return ServiceResult.Fail("An approved yearly entry already exists for this project, source, and year.");
                }

                entity.ApprovalStatus = ApprovalStatus.Approved;
                entity.ApprovedByUserId = actor.Id;
                entity.ApprovedOnUtc = now;
            }
            else
            {
                entity.ApprovalStatus = ApprovalStatus.Pending;
            }

            _db.ProliferationYearlies.Add(entity);
            await _db.SaveChangesAsync(ct);

            await Audit.Events
                .ProliferationYearlyRecorded(project.Id, entity.Source, entity.Year, entity.TotalQuantity, entity.ApprovalStatus, actor.Id, "Create")
                .WriteAsync(_audit, userName: actor.UserName);

            return ServiceResult.Ok();
        }

        public async Task<ServiceResult> CreateGranularAsync(ProliferationGranularCreateDto dto, ClaimsPrincipal user, CancellationToken ct)
        {
            if (dto is null) throw new ArgumentNullException(nameof(dto));
            if (user is null) throw new ArgumentNullException(nameof(user));

            var actor = await _users.GetUserAsync(user);
            if (actor is null)
            {
                return ServiceResult.Fail("User not found.");
            }

            var project = await GetCompletedProjectAsync(dto.ProjectId, ct);
            if (project is null)
            {
                return ServiceResult.Fail("Only completed projects may record proliferation data.");
            }

            string simulator;
            string unit;
            try
            {
                simulator = RequireValue(dto.SimulatorName, nameof(dto.SimulatorName), 200);
                unit = RequireValue(dto.UnitName, nameof(dto.UnitName), 200);
            }
            catch (ValidationException ex)
            {
                return ServiceResult.Fail(ex.Message);
            }

            var remarks = Normalize(dto.Remarks, 500);

            if (dto.Quantity <= 0)
            {
                return ServiceResult.Fail("Quantity must be greater than zero.");
            }

            var now = _clock.UtcNow.UtcDateTime;
            var normalizedDateTime = DateTime.SpecifyKind(dto.ProliferationDateUtc, DateTimeKind.Utc);
            var maxDate = now.AddDays(30);
            if (normalizedDateTime > maxDate)
            {
                return ServiceResult.Fail("Proliferation date cannot be more than 30 days in the future.");
            }

            var date = DateOnly.FromDateTime(normalizedDateTime);

            var entity = new ProliferationGranular
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Source = ProliferationSource.Sdd,
                SimulatorName = simulator,
                UnitName = unit,
                ProliferationDate = date,
                Quantity = dto.Quantity,
                Remarks = remarks,
                SubmittedByUserId = actor.Id,
                CreatedOnUtc = now,
                LastUpdatedOnUtc = now,
                RowVersion = Array.Empty<byte>()
            };

            if (RequiresImmediateApproval(user))
            {
                entity.ApprovalStatus = ApprovalStatus.Approved;
                entity.ApprovedByUserId = actor.Id;
                entity.ApprovedOnUtc = now;
            }
            else
            {
                entity.ApprovalStatus = ApprovalStatus.Pending;
            }

            _db.ProliferationGranularEntries.Add(entity);
            await _db.SaveChangesAsync(ct);

            await Audit.Events
                .ProliferationGranularRecorded(project.Id, entity.Source, entity.SimulatorName, entity.UnitName, entity.ProliferationDate, entity.Quantity, entity.ApprovalStatus, actor.Id, "Create")
                .WriteAsync(_audit, userName: actor.UserName);

            return ServiceResult.Ok();
        }

        public async Task<ServiceResult> SetYearPreferenceAsync(ProliferationYearPreferenceDto dto, ClaimsPrincipal user, CancellationToken ct)
        {
            if (dto is null) throw new ArgumentNullException(nameof(dto));
            if (user is null) throw new ArgumentNullException(nameof(user));

            var actor = await _users.GetUserAsync(user);
            if (actor is null)
            {
                return ServiceResult.Fail("User not found.");
            }

            if (dto.Source == ProliferationSource.Abw515 && dto.Mode != YearPreferenceMode.Auto)
            {
                return ServiceResult.Fail("ABW 515 uses Yearly totals and cannot be overridden.");
            }

            var project = await GetCompletedProjectAsync(dto.ProjectId, ct);
            if (project is null)
            {
                return ServiceResult.Fail("Only completed projects may configure preferences.");
            }

            var now = _clock.UtcNow.UtcDateTime;

            var existing = await _db.ProliferationYearPreferences
                .FirstOrDefaultAsync(p => p.ProjectId == dto.ProjectId && p.Source == dto.Source && p.Year == dto.Year, ct);

            var isNew = existing is null;

            if (existing is null)
            {
                existing = new ProliferationYearPreference
                {
                    Id = Guid.NewGuid(),
                    ProjectId = dto.ProjectId,
                    Source = dto.Source,
                    Year = dto.Year,
                    Mode = dto.Mode,
                    SetByUserId = actor.Id,
                    SetOnUtc = now
                };

                _db.ProliferationYearPreferences.Add(existing);
            }
            else
            {
                existing.Mode = dto.Mode;
                existing.SetByUserId = actor.Id;
                existing.SetOnUtc = now;
            }

            await _db.SaveChangesAsync(ct);

            await Audit.Events
                .ProliferationPreferenceChanged(project.Id, dto.Source, dto.Year, actor.Id, actor.Id, isNew ? "Created" : "Updated")
                .WriteAsync(_audit, userName: actor.UserName);

            return ServiceResult.Ok();
        }

        public async Task<ImportResultDto> ImportYearlyCsvAsync(Stream csv, ClaimsPrincipal user, CancellationToken ct)
        {
            if (csv is null) throw new ArgumentNullException(nameof(csv));
            if (user is null) throw new ArgumentNullException(nameof(user));

            var actor = await _users.GetUserAsync(user);
            if (actor is null)
            {
                return new ImportResultDto { Accepted = 0, Rejected = 0, ErrorCsvBase64 = null };
            }

            var rows = await ParseCsvAsync(csv, ct);
            if (rows.Count == 0)
            {
                return new ImportResultDto { Accepted = 0, Rejected = 0, ErrorCsvBase64 = BuildErrorCsv(new[] { (1, "The uploaded file is empty.") }) };
            }

            try
            {
                ValidateHeader(rows[0], new[] { "ProjectCode", "Source", "Year", "TotalQuantity", "Remarks" });
            }
            catch (ValidationException ex)
            {
                return new ImportResultDto
                {
                    Accepted = 0,
                    Rejected = 0,
                    ErrorCsvBase64 = BuildErrorCsv(new[] { (1, ex.Message) })
                };
            }

            var errors = new List<(int RowNumber, string Message)>();
            var validEntries = new List<ProliferationYearly>();
            var now = _clock.UtcNow.UtcDateTime;
            var isApprover = RequiresImmediateApproval(user);

            for (var index = 1; index < rows.Count; index++)
            {
                var row = rows[index];
                if (row.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                try
                {
                    var code = RequireValue(row.ElementAtOrDefault(0), "ProjectCode", 64);
                    var source = ParseSource(row.ElementAtOrDefault(1));
                    var year = ParseInt(row.ElementAtOrDefault(2), "Year");
                    var quantity = ParseInt(row.ElementAtOrDefault(3), "TotalQuantity");
                    var remarks = Normalize(row.ElementAtOrDefault(4), 500);

                    if (quantity < 0)
                    {
                        throw new ValidationException("TotalQuantity must be zero or greater.");
                    }

                    var project = await GetCompletedProjectByCodeAsync(code, ct);
                    if (project is null)
                    {
                        throw new ValidationException($"Project '{code}' not found or not completed.");
                    }

                    if (isApprover)
                    {
                        var duplicate = await _db.ProliferationYearlies
                            .AsNoTracking()
                            .AnyAsync(y => y.ProjectId == project.Id && y.Source == source && y.Year == year && y.ApprovalStatus == ApprovalStatus.Approved, ct);
                        if (duplicate)
                        {
                            throw new ValidationException("An approved yearly entry already exists for this project, source, and year.");
                        }
                    }

                    var entity = new ProliferationYearly
                    {
                        Id = Guid.NewGuid(),
                        ProjectId = project.Id,
                        Source = source,
                        Year = year,
                        TotalQuantity = quantity,
                        Remarks = remarks,
                        SubmittedByUserId = actor.Id,
                        CreatedOnUtc = now,
                        LastUpdatedOnUtc = now,
                        RowVersion = Array.Empty<byte>(),
                        ApprovalStatus = isApprover ? ApprovalStatus.Approved : ApprovalStatus.Pending,
                        ApprovedByUserId = isApprover ? actor.Id : null,
                        ApprovedOnUtc = isApprover ? now : null
                    };

                    validEntries.Add(entity);
                }
                catch (ValidationException ex)
                {
                    errors.Add((index + 1, ex.Message));
                }
            }

            var accepted = 0;

            if (validEntries.Count > 0)
            {
                await using var transaction = await _db.Database.BeginTransactionAsync(ct);
                foreach (var entry in validEntries)
                {
                    _db.ProliferationYearlies.Add(entry);
                }

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                foreach (var entry in validEntries)
                {
                    await Audit.Events
                        .ProliferationYearlyRecorded(entry.ProjectId, entry.Source, entry.Year, entry.TotalQuantity, entry.ApprovalStatus, actor.Id, "Import")
                        .WriteAsync(_audit, userName: actor.UserName);
                }

                accepted = validEntries.Count;
            }

            var errorCsv = errors.Count == 0 ? null : BuildErrorCsv(errors);
            var auditSource = validEntries.FirstOrDefault()?.Source ?? ProliferationSource.Sdd;

            await Audit.Events
                .ProliferationImportCompleted(actor.Id, "Yearly", auditSource, null, rows.Count - 1, accepted, errors.Count)
                .WriteAsync(_audit, userName: actor.UserName);

            return new ImportResultDto
            {
                Accepted = accepted,
                Rejected = errors.Count,
                ErrorCsvBase64 = errorCsv
            };
        }

        public async Task<ImportResultDto> ImportGranularCsvAsync(Stream csv, ClaimsPrincipal user, CancellationToken ct)
        {
            if (csv is null) throw new ArgumentNullException(nameof(csv));
            if (user is null) throw new ArgumentNullException(nameof(user));

            var actor = await _users.GetUserAsync(user);
            if (actor is null)
            {
                return new ImportResultDto { Accepted = 0, Rejected = 0, ErrorCsvBase64 = null };
            }

            var rows = await ParseCsvAsync(csv, ct);
            if (rows.Count == 0)
            {
                return new ImportResultDto { Accepted = 0, Rejected = 0, ErrorCsvBase64 = BuildErrorCsv(new[] { (1, "The uploaded file is empty.") }) };
            }

            try
            {
                ValidateHeader(rows[0], new[] { "ProjectCode", "SimulatorName", "UnitName", "ProliferationDate", "Quantity", "Remarks" });
            }
            catch (ValidationException ex)
            {
                return new ImportResultDto
                {
                    Accepted = 0,
                    Rejected = 0,
                    ErrorCsvBase64 = BuildErrorCsv(new[] { (1, ex.Message) })
                };
            }

            var errors = new List<(int RowNumber, string Message)>();
            var validEntries = new List<ProliferationGranular>();
            var now = _clock.UtcNow.UtcDateTime;
            var isApprover = RequiresImmediateApproval(user);

            for (var index = 1; index < rows.Count; index++)
            {
                var row = rows[index];
                if (row.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                try
                {
                    var code = RequireValue(row.ElementAtOrDefault(0), "ProjectCode", 64);
                    var simulator = RequireValue(row.ElementAtOrDefault(1), "SimulatorName", 200);
                    var unit = RequireValue(row.ElementAtOrDefault(2), "UnitName", 200);
                    var date = ParseDate(row.ElementAtOrDefault(3), "ProliferationDate");
                    var quantity = ParseInt(row.ElementAtOrDefault(4), "Quantity");
                    var remarks = Normalize(row.ElementAtOrDefault(5), 500);

                    if (quantity <= 0)
                    {
                        throw new ValidationException("Quantity must be greater than zero.");
                    }

                    if (date > DateOnly.FromDateTime(now.AddDays(30)))
                    {
                        throw new ValidationException("Proliferation date cannot be more than 30 days in the future.");
                    }

                    var project = await GetCompletedProjectByCodeAsync(code, ct);
                    if (project is null)
                    {
                        throw new ValidationException($"Project '{code}' not found or not completed.");
                    }

                    var entity = new ProliferationGranular
                    {
                        Id = Guid.NewGuid(),
                        ProjectId = project.Id,
                        Source = ProliferationSource.Sdd,
                        SimulatorName = simulator,
                        UnitName = unit,
                        ProliferationDate = date,
                        Quantity = quantity,
                        Remarks = remarks,
                        SubmittedByUserId = actor.Id,
                        CreatedOnUtc = now,
                        LastUpdatedOnUtc = now,
                        RowVersion = Array.Empty<byte>(),
                        ApprovalStatus = isApprover ? ApprovalStatus.Approved : ApprovalStatus.Pending,
                        ApprovedByUserId = isApprover ? actor.Id : null,
                        ApprovedOnUtc = isApprover ? now : null
                    };

                    validEntries.Add(entity);
                }
                catch (ValidationException ex)
                {
                    errors.Add((index + 1, ex.Message));
                }
            }

            var accepted = 0;

            if (validEntries.Count > 0)
            {
                await using var transaction = await _db.Database.BeginTransactionAsync(ct);
                foreach (var entry in validEntries)
                {
                    _db.ProliferationGranularEntries.Add(entry);
                }

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                foreach (var entry in validEntries)
                {
                    await Audit.Events
                        .ProliferationGranularRecorded(entry.ProjectId, entry.Source, entry.SimulatorName, entry.UnitName, entry.ProliferationDate, entry.Quantity, entry.ApprovalStatus, actor.Id, "Import")
                        .WriteAsync(_audit, userName: actor.UserName);
                }

                accepted = validEntries.Count;
            }

            var errorCsv = errors.Count == 0 ? null : BuildErrorCsv(errors);

            await Audit.Events
                .ProliferationImportCompleted(actor.Id, "Granular", ProliferationSource.Sdd, null, rows.Count - 1, accepted, errors.Count)
                .WriteAsync(_audit, userName: actor.UserName);

            return new ImportResultDto
            {
                Accepted = accepted,
                Rejected = errors.Count,
                ErrorCsvBase64 = errorCsv
            };
        }

        private async Task<Project?> GetCompletedProjectAsync(int projectId, CancellationToken ct)
        {
            return await _db.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == projectId && !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Completed, ct);
        }

        private async Task<Project?> GetCompletedProjectByCodeAsync(string projectCode, CancellationToken ct)
        {
            var trimmed = projectCode.Trim();

            return await _db.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.CaseFileNumber != null &&
                    EF.Functions.ILike(p.CaseFileNumber!, trimmed) &&
                    !p.IsDeleted &&
                    !p.IsArchived &&
                    p.LifecycleStatus == ProjectLifecycleStatus.Completed,
                    ct);
        }

        private static bool RequiresImmediateApproval(ClaimsPrincipal principal)
            => principal.IsInRole("Admin") || principal.IsInRole("HoD");

        private static string RequireValue(string? value, string field, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ValidationException($"{field} is required.");
            }

            var trimmed = value.Trim();
            if (trimmed.Length > maxLength)
            {
                throw new ValidationException($"{field} exceeds the maximum length of {maxLength} characters.");
            }

            return trimmed;
        }

        private static string? Normalize(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
        }

        private static int ParseInt(string? value, string field)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ValidationException($"{field} is required.");
            }

            if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            {
                throw new ValidationException($"{field} must be a number.");
            }

            return result;
        }

        private static DateOnly ParseDate(string? value, string field)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ValidationException($"{field} is required.");
            }

            if (!DateOnly.TryParse(value.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            {
                throw new ValidationException($"{field} must use YYYY-MM-DD format.");
            }

            return result;
        }

        private static ProliferationSource ParseSource(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ValidationException("Source is required.");
            }

            var token = value.Trim().Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
            return token switch
            {
                "SDD" => ProliferationSource.Sdd,
                "ABW515" => ProliferationSource.Abw515,
                "515ABW" => ProliferationSource.Abw515,
                _ => throw new ValidationException($"Unsupported source '{value}'.")
            };
        }

        private static async Task<List<string[]>> ParseCsvAsync(Stream csv, CancellationToken ct)
        {
            var rows = new List<string[]>();
            if (csv.CanSeek)
            {
                csv.Seek(0, SeekOrigin.Begin);
            }
            using var reader = new StreamReader(csv, Encoding.UTF8, leaveOpen: true);
            string? line;
            while ((line = await reader.ReadLineAsync()) is not null)
            {
                ct.ThrowIfCancellationRequested();
                rows.Add(ParseCsvLine(line));
            }

            return rows;
        }

        private static string[] ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var builder = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var current = line[i];
                if (current == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (current == ',' && !inQuotes)
                {
                    fields.Add(builder.ToString());
                    builder.Clear();
                }
                else
                {
                    builder.Append(current);
                }
            }

            fields.Add(builder.ToString());
            return fields.ToArray();
        }

        private static void ValidateHeader(IReadOnlyList<string> header, IReadOnlyList<string> expected)
        {
            if (header.Count < expected.Count)
            {
                throw new ValidationException("The CSV header is invalid.");
            }

            for (var i = 0; i < expected.Count; i++)
            {
                if (!string.Equals(header[i]?.Trim(), expected[i], StringComparison.OrdinalIgnoreCase))
                {
                    throw new ValidationException("The CSV header is invalid.");
                }
            }
        }

        private static string? BuildErrorCsv(IEnumerable<(int RowNumber, string Message)> errors)
        {
            var list = errors.ToList();
            if (list.Count == 0)
            {
                return null;
            }

            var sb = new StringBuilder();
            sb.AppendLine("RowNumber,Error");
            foreach (var (row, message) in list)
            {
                var safe = message.Replace("\"", "\"\"");
                sb.AppendLine($"{row},\"{safe}\"");
            }

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(sb.ToString()));
        }
    }

    public sealed class ValidationException : Exception
    {
        public ValidationException(string message) : base(message)
        {
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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

            string unit;
            try
            {
                unit = RequireValue(dto.UnitName, nameof(dto.UnitName), 200);
            }
            catch (ValidationException ex)
            {
                return ServiceResult.Fail(ex.Message);
            }

            if (dto.Source != ProliferationSource.Sdd)
            {
                return ServiceResult.Fail("Granular entries support only the SDD source.");
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
                Source = dto.Source,
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
                .ProliferationGranularRecorded(project.Id, project.Name, entity.Source, entity.UnitName, entity.ProliferationDate, entity.Quantity, entity.ApprovalStatus, actor.Id, "Create")
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

        public async Task<ServiceResult> UpdateYearlyAsync(Guid id, ProliferationYearlyUpdateDto dto, ClaimsPrincipal user, CancellationToken ct)
        {
            if (dto is null) throw new ArgumentNullException(nameof(dto));
            if (user is null) throw new ArgumentNullException(nameof(user));

            var actor = await _users.GetUserAsync(user);
            if (actor is null)
            {
                return ServiceResult.Fail("User not found.");
            }

            var entity = await _db.ProliferationYearlies.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity is null)
            {
                return ServiceResult.Fail("Record not found.");
            }

            if (!TryDecodeRowVersion(dto.RowVersion, out var rowVersion))
            {
                return ServiceResult.Fail("The record is out of date. Refresh and try again.");
            }

            if (dto.TotalQuantity < 0)
            {
                return ServiceResult.Fail("Total quantity must be zero or greater.");
            }

            if (dto.Source != ProliferationSource.Sdd && dto.Source != ProliferationSource.Abw515)
            {
                return ServiceResult.Fail("Unsupported source.");
            }

            var project = await GetCompletedProjectAsync(dto.ProjectId, ct);
            if (project is null)
            {
                return ServiceResult.Fail("Only completed projects may record proliferation data.");
            }

            var remarks = Normalize(dto.Remarks, 500);
            var now = _clock.UtcNow.UtcDateTime;

            var requiresApproval = RequiresImmediateApproval(user) || entity.ApprovalStatus == ApprovalStatus.Approved;
            if (requiresApproval)
            {
                var duplicate = await _db.ProliferationYearlies
                    .AsNoTracking()
                    .AnyAsync(y =>
                        y.Id != entity.Id &&
                        y.ProjectId == project.Id &&
                        y.Source == dto.Source &&
                        y.Year == dto.Year &&
                        y.ApprovalStatus == ApprovalStatus.Approved,
                        ct);

                if (duplicate)
                {
                    return ServiceResult.Fail("An approved yearly entry already exists for this project, source, and year.");
                }
            }

            entity.ProjectId = project.Id;
            entity.Source = dto.Source;
            entity.Year = dto.Year;
            entity.TotalQuantity = dto.TotalQuantity;
            entity.Remarks = remarks;
            entity.LastUpdatedOnUtc = now;

            _db.Entry(entity).Property(e => e.RowVersion).OriginalValue = rowVersion;

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult.Fail("The record was modified by another user. Refresh and try again.");
            }

            await Audit.Events
                .ProliferationYearlyRecorded(entity.ProjectId, entity.Source, entity.Year, entity.TotalQuantity, entity.ApprovalStatus, actor.Id, "Update")
                .WriteAsync(_audit, userName: actor.UserName);

            return ServiceResult.Ok();
        }

        public async Task<ServiceResult> UpdateGranularAsync(Guid id, ProliferationGranularUpdateDto dto, ClaimsPrincipal user, CancellationToken ct)
        {
            if (dto is null) throw new ArgumentNullException(nameof(dto));
            if (user is null) throw new ArgumentNullException(nameof(user));

            var actor = await _users.GetUserAsync(user);
            if (actor is null)
            {
                return ServiceResult.Fail("User not found.");
            }

            var entity = await _db.ProliferationGranularEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity is null)
            {
                return ServiceResult.Fail("Record not found.");
            }

            if (!TryDecodeRowVersion(dto.RowVersion, out var rowVersion))
            {
                return ServiceResult.Fail("The record is out of date. Refresh and try again.");
            }

            if (dto.Source != ProliferationSource.Sdd)
            {
                return ServiceResult.Fail("Granular entries support only the SDD source.");
            }

            string unit;
            try
            {
                unit = RequireValue(dto.UnitName, nameof(dto.UnitName), 200);
            }
            catch (ValidationException ex)
            {
                return ServiceResult.Fail(ex.Message);
            }

            if (dto.Quantity <= 0)
            {
                return ServiceResult.Fail("Quantity must be greater than zero.");
            }

            var project = await GetCompletedProjectAsync(dto.ProjectId, ct);
            if (project is null)
            {
                return ServiceResult.Fail("Only completed projects may record proliferation data.");
            }

            var now = _clock.UtcNow.UtcDateTime;
            var normalizedDateTime = DateTime.SpecifyKind(dto.ProliferationDateUtc, DateTimeKind.Utc);
            var maxDate = now.AddDays(30);
            if (normalizedDateTime > maxDate)
            {
                return ServiceResult.Fail("Proliferation date cannot be more than 30 days in the future.");
            }

            var date = DateOnly.FromDateTime(normalizedDateTime);
            var remarks = Normalize(dto.Remarks, 500);

            entity.ProjectId = project.Id;
            entity.Source = dto.Source;
            entity.UnitName = unit;
            entity.ProliferationDate = date;
            entity.Quantity = dto.Quantity;
            entity.Remarks = remarks;
            entity.LastUpdatedOnUtc = now;

            _db.Entry(entity).Property(e => e.RowVersion).OriginalValue = rowVersion;

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult.Fail("The record was modified by another user. Refresh and try again.");
            }

            await Audit.Events
                .ProliferationGranularRecorded(entity.ProjectId, project.Name, entity.Source, entity.UnitName, entity.ProliferationDate, entity.Quantity, entity.ApprovalStatus, actor.Id, "Update")
                .WriteAsync(_audit, userName: actor.UserName);

            return ServiceResult.Ok();
        }

        public async Task<ServiceResult> DeleteYearlyAsync(Guid id, string? rowVersionBase64, ClaimsPrincipal user, CancellationToken ct)
        {
            if (user is null) throw new ArgumentNullException(nameof(user));

            var actor = await _users.GetUserAsync(user);
            if (actor is null)
            {
                return ServiceResult.Fail("User not found.");
            }

            var entity = await _db.ProliferationYearlies.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity is null)
            {
                return ServiceResult.Fail("Record not found.");
            }

            if (!TryDecodeRowVersion(rowVersionBase64, out var rowVersion))
            {
                return ServiceResult.Fail("The record is out of date. Refresh and try again.");
            }

            _db.Entry(entity).Property(e => e.RowVersion).OriginalValue = rowVersion;
            _db.ProliferationYearlies.Remove(entity);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult.Fail("The record was modified by another user. Refresh and try again.");
            }

            await Audit.Events
                .ProliferationYearlyRecorded(entity.ProjectId, entity.Source, entity.Year, entity.TotalQuantity, entity.ApprovalStatus, actor.Id, "Delete")
                .WriteAsync(_audit, userName: actor.UserName);

            return ServiceResult.Ok();
        }

        public async Task<ServiceResult> DeleteGranularAsync(Guid id, string? rowVersionBase64, ClaimsPrincipal user, CancellationToken ct)
        {
            if (user is null) throw new ArgumentNullException(nameof(user));

            var actor = await _users.GetUserAsync(user);
            if (actor is null)
            {
                return ServiceResult.Fail("User not found.");
            }

            var entity = await _db.ProliferationGranularEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity is null)
            {
                return ServiceResult.Fail("Record not found.");
            }

            if (!TryDecodeRowVersion(rowVersionBase64, out var rowVersion))
            {
                return ServiceResult.Fail("The record is out of date. Refresh and try again.");
            }

            _db.Entry(entity).Property(e => e.RowVersion).OriginalValue = rowVersion;
            _db.ProliferationGranularEntries.Remove(entity);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult.Fail("The record was modified by another user. Refresh and try again.");
            }

            var project = await _db.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == entity.ProjectId, ct);

            var projectName = project?.Name ?? "(unknown)";

            await Audit.Events
                .ProliferationGranularRecorded(entity.ProjectId, projectName, entity.Source, entity.UnitName, entity.ProliferationDate, entity.Quantity, entity.ApprovalStatus, actor.Id, "Delete")
                .WriteAsync(_audit, userName: actor.UserName);

            return ServiceResult.Ok();
        }

        private async Task<Project?> GetCompletedProjectAsync(int projectId, CancellationToken ct)
        {
            return await _db.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == projectId && !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Completed, ct);
        }

        private static bool RequiresImmediateApproval(ClaimsPrincipal principal)
            => principal.IsInRole("Admin") || principal.IsInRole("HoD");

        private static bool TryDecodeRowVersion(string? value, out byte[] rowVersion)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                rowVersion = Array.Empty<byte>();
                return false;
            }

            try
            {
                rowVersion = Convert.FromBase64String(value);
                return rowVersion.Length > 0;
            }
            catch (FormatException)
            {
                rowVersion = Array.Empty<byte>();
                return false;
            }
        }

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

    }

    public sealed class ValidationException : Exception
    {
        public ValidationException(string message) : base(message)
        {
        }
    }
}

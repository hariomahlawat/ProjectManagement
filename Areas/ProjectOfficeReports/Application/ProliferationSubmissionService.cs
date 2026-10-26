using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
        public Guid? EntityId { get; private set; }
        public static ServiceResult Ok(Guid? entityId = null) => new() { Success = true, EntityId = entityId };
        public static ServiceResult Fail(string error) => new() { Success = false, Error = error };
    }

    public partial class ProliferationSubmissionService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _users;
        private readonly IClock _clock;
        private readonly IAuditService _audit;
        private static readonly DateTime MinimumProliferationDateUtc = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private const string ConcurrencyError = "The record was modified by another user. Refresh and try again.";
        private const string PendingAmendmentExistsError = "An amendment to this record is already awaiting approval. Open it under Pending approval to change it.";
        private const string NotPendingError = "This record is no longer pending approval. Refresh and try again.";
        private const string DuplicateYearlyError = "Another approved yearly entry already exists for this project, source, and year.";
        private const string AmendedRecordDeletedError = "This record was deleted while you were editing it. Refresh and try again.";

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

            // SECTION: Required field validation
            var requiredError = ValidateYearlyRequiredFields(dto, _clock.UtcNow);
            if (requiredError is not null)
            {
                return requiredError;
            }

            var project = await GetCompletedProjectAsync(dto.ProjectId, ct);
            if (project is null)
            {
                return ServiceResult.Fail("Only completed projects may record proliferation data.");
            }
            if (project.IsBuild)
            {
                return ServiceResult.Fail(ProliferationProjectEligibility.RepeatBuildRecordError);
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
            await using var transaction = await BeginWriteAsync(ct);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (entity.ApprovalStatus == ApprovalStatus.Approved && !ct.IsCancellationRequested)
            {
                await RollbackAsync(transaction, ct);
                if (await HasOtherApprovedYearlyAsync(entity, ct))
                {
                    return ServiceResult.Fail(DuplicateYearlyError);
                }

                throw;
            }

            await Audit.Events
                .ProliferationYearlyRecorded(project.Id, entity.Source, entity.Year, entity.TotalQuantity, entity.ApprovalStatus, actor.Id, "Create")
                .WriteAsync(_audit, userName: actor.UserName);

            await CommitAsync(transaction, ct);

            return ServiceResult.Ok(entity.Id);
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

            // SECTION: Required field validation
            var requiredError = ValidateGranularRequiredFields(dto);
            if (requiredError is not null)
            {
                return requiredError;
            }

            var project = await GetCompletedProjectAsync(dto.ProjectId, ct);
            if (project is null)
            {
                return ServiceResult.Fail("Only completed projects may record proliferation data.");
            }
            if (project.IsBuild)
            {
                return ServiceResult.Fail(ProliferationProjectEligibility.RepeatBuildRecordError);
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

            if (dto.Source != ProliferationSource.Sdd && dto.Source != ProliferationSource.Abw515)
            {
                return ServiceResult.Fail("Unsupported source.");
            }

            var remarks = Normalize(dto.Remarks, 500);

            var now = _clock.UtcNow.UtcDateTime;
            var normalizedDateTime = DateTime.SpecifyKind(dto.ProliferationDateUtc, DateTimeKind.Utc);
            var maxDate = now.AddDays(30);
            if (normalizedDateTime < MinimumProliferationDateUtc)
            {
                return ServiceResult.Fail("Proliferation date cannot be earlier than 01 Jan 2000.");
            }
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
            await using var transaction = await BeginWriteAsync(ct);
            await _db.SaveChangesAsync(ct);

            await Audit.Events
                .ProliferationGranularRecorded(project.Id, project.Name, entity.Source, entity.UnitName, entity.ProliferationDate, entity.Quantity, entity.ApprovalStatus, actor.Id, "Create")
                .WriteAsync(_audit, userName: actor.UserName);

            await CommitAsync(transaction, ct);

            return ServiceResult.Ok(entity.Id);
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

            if (dto.UseSourceDefault)
            {
                dto.Mode = ProliferationAggregateReadService.GetDefaultMode(dto.Source);
            }

            // SECTION: Required field validation
            var requiredError = ValidatePreferenceRequiredFields(dto, _clock.UtcNow);
            if (requiredError is not null)
            {
                return requiredError;
            }

            var project = await GetCompletedProjectAsync(dto.ProjectId, ct);
            if (project is null)
            {
                return ServiceResult.Fail("Only completed projects may configure preferences.");
            }

            var existing = await _db.ProliferationYearPreferences
                .FirstOrDefaultAsync(p => p.ProjectId == dto.ProjectId && p.Source == dto.Source && p.Year == dto.Year, ct);

            // Existing legacy rules remain editable for historical continuity, but
            // a repeat-build project cannot receive a new counting-rule exception.
            if (project.IsBuild && existing is null)
            {
                return ServiceResult.Fail(ProliferationProjectEligibility.RepeatBuildPreferenceError);
            }

            var now = _clock.UtcNow.UtcDateTime;
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

            await using var transaction = await BeginWriteAsync(ct);
            await _db.SaveChangesAsync(ct);

            await Audit.Events
                .ProliferationPreferenceChanged(
                    project.Id,
                    dto.Source,
                    dto.Year,
                    actor.Id,
                    actor.Id,
                    isNew ? "Created" : "Updated",
                    Normalize(dto.Reason, 500))
                .WriteAsync(_audit, userName: actor.UserName);

            await CommitAsync(transaction, ct);

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

            // SECTION: Required field validation
            var requiredError = ValidateYearlyRequiredFields(dto, _clock.UtcNow);
            if (requiredError is not null)
            {
                return requiredError;
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

            if (dto.Source != ProliferationSource.Sdd && dto.Source != ProliferationSource.Abw515)
            {
                return ServiceResult.Fail("Unsupported source.");
            }

            var project = await GetCompletedProjectAsync(dto.ProjectId, ct);
            if (project is null)
            {
                return ServiceResult.Fail("Only completed projects may record proliferation data.");
            }
            if (project.IsBuild && project.Id != entity.ProjectId)
            {
                return ServiceResult.Fail(ProliferationProjectEligibility.RepeatBuildRecordError);
            }

            var remarks = Normalize(dto.Remarks, 500);
            var now = _clock.UtcNow.UtcDateTime;

            var canApproveImmediately = RequiresImmediateApproval(user);

            // SECTION: Amendment of an approved record
            // Project Office never changes an approved figure in place. The change is filed
            // as its own pending row linked to the approved record, which keeps counting
            // until the amendment is approved and is left untouched if it is rejected.
            if (!canApproveImmediately && entity.ApprovalStatus == ApprovalStatus.Approved)
            {
                if (!entity.RowVersion.AsSpan().SequenceEqual(rowVersion))
                {
                    return ServiceResult.Fail(ConcurrencyError);
                }

                if (await HasPendingYearlyAmendmentAsync(entity.Id, null, ct))
                {
                    return ServiceResult.Fail(PendingAmendmentExistsError);
                }

                var amendment = new ProliferationYearly
                {
                    Id = Guid.NewGuid(),
                    ProjectId = project.Id,
                    Source = dto.Source,
                    Year = dto.Year,
                    TotalQuantity = dto.TotalQuantity,
                    Remarks = remarks,
                    AmendsId = entity.Id,
                    ApprovalStatus = ApprovalStatus.Pending,
                    SubmittedByUserId = actor.Id,
                    CreatedOnUtc = now,
                    LastUpdatedOnUtc = now,
                    RowVersion = Array.Empty<byte>()
                };

                _db.ProliferationYearlies.Add(amendment);
                await using var amendmentTransaction = await BeginWriteAsync(ct);
                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException) when (!ct.IsCancellationRequested)
                {
                    await RollbackAsync(amendmentTransaction, ct);

                    // A concurrent amendment won the unique pending-amendment index, or the
                    // record was deleted meanwhile and the AmendsId foreign key refused the row.
                    if (await HasPendingYearlyAmendmentAsync(entity.Id, amendment.Id, ct))
                    {
                        return ServiceResult.Fail(PendingAmendmentExistsError);
                    }

                    if (!await _db.ProliferationYearlies.AsNoTracking().AnyAsync(y => y.Id == entity.Id, ct))
                    {
                        return ServiceResult.Fail(AmendedRecordDeletedError);
                    }

                    throw;
                }

                await Audit.Events
                    .ProliferationYearlyRecorded(amendment.ProjectId, amendment.Source, amendment.Year, amendment.TotalQuantity, amendment.ApprovalStatus, actor.Id, "Amend", amendment.Id, entity.Id)
                    .WriteAsync(_audit, userName: actor.UserName);

                await CommitAsync(amendmentTransaction, ct);

                return ServiceResult.Ok(amendment.Id);
            }

            if (!canApproveImmediately
                && entity.AmendsId.HasValue
                && await HasPendingYearlyAmendmentAsync(entity.AmendsId.Value, entity.Id, ct))
            {
                return ServiceResult.Fail(PendingAmendmentExistsError);
            }

            entity.ProjectId = project.Id;
            entity.Source = dto.Source;
            entity.Year = dto.Year;
            entity.TotalQuantity = dto.TotalQuantity;
            entity.Remarks = remarks;
            entity.LastUpdatedOnUtc = now;
            _db.Entry(entity).Property(e => e.RowVersion).OriginalValue = rowVersion;

            var amendedRecordId = entity.AmendsId;
            var saved = entity;
            if (canApproveImmediately)
            {
                var approval = await ApplyYearlyApprovalAsync(entity, actor.Id, now, ct);
                if (approval.Error is not null)
                {
                    return ServiceResult.Fail(approval.Error);
                }

                saved = approval.Record!;
            }
            else
            {
                entity.ApprovalStatus = ApprovalStatus.Pending;
                entity.ApprovedByUserId = null;
                entity.ApprovedOnUtc = null;
            }

            await using var transaction = await BeginWriteAsync(ct);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult.Fail(ConcurrencyError);
            }
            catch (DbUpdateException) when (!ct.IsCancellationRequested)
            {
                await RollbackAsync(transaction, ct);

                // Resubmitting a rejected amendment raced another pending amendment.
                if (amendedRecordId.HasValue
                    && await HasPendingYearlyAmendmentAsync(amendedRecordId.Value, entity.Id, ct))
                {
                    return ServiceResult.Fail(PendingAmendmentExistsError);
                }

                // An approval raced another approved row for the same project, source and year.
                if (saved.ApprovalStatus == ApprovalStatus.Approved && await HasOtherApprovedYearlyAsync(saved, ct))
                {
                    return ServiceResult.Fail(DuplicateYearlyError);
                }

                throw;
            }

            await Audit.Events
                .ProliferationYearlyRecorded(saved.ProjectId, saved.Source, saved.Year, saved.TotalQuantity, saved.ApprovalStatus, actor.Id, "Update", entity.Id, amendedRecordId)
                .WriteAsync(_audit, userName: actor.UserName);

            await CommitAsync(transaction, ct);

            return ServiceResult.Ok(saved.Id);
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

            // SECTION: Required field validation
            var requiredError = ValidateGranularRequiredFields(dto);
            if (requiredError is not null)
            {
                return requiredError;
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

            if (dto.Source != ProliferationSource.Sdd && dto.Source != ProliferationSource.Abw515)
            {
                return ServiceResult.Fail("Unsupported source.");
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

            var project = await GetCompletedProjectAsync(dto.ProjectId, ct);
            if (project is null)
            {
                return ServiceResult.Fail("Only completed projects may record proliferation data.");
            }
            if (project.IsBuild && project.Id != entity.ProjectId)
            {
                return ServiceResult.Fail(ProliferationProjectEligibility.RepeatBuildRecordError);
            }

            var now = _clock.UtcNow.UtcDateTime;
            var normalizedDateTime = DateTime.SpecifyKind(dto.ProliferationDateUtc, DateTimeKind.Utc);
            var maxDate = now.AddDays(30);
            if (normalizedDateTime < MinimumProliferationDateUtc)
            {
                return ServiceResult.Fail("Proliferation date cannot be earlier than 01 Jan 2000.");
            }
            if (normalizedDateTime > maxDate)
            {
                return ServiceResult.Fail("Proliferation date cannot be more than 30 days in the future.");
            }

            var date = DateOnly.FromDateTime(normalizedDateTime);
            var remarks = Normalize(dto.Remarks, 500);
            var canApproveImmediately = RequiresImmediateApproval(user);

            // SECTION: Amendment of an approved record
            // Same rule as annual records: an approved detailed entry is amended through a
            // linked pending row, never edited in place by Project Office.
            if (!canApproveImmediately && entity.ApprovalStatus == ApprovalStatus.Approved)
            {
                if (!entity.RowVersion.AsSpan().SequenceEqual(rowVersion))
                {
                    return ServiceResult.Fail(ConcurrencyError);
                }

                if (await HasPendingGranularAmendmentAsync(entity.Id, null, ct))
                {
                    return ServiceResult.Fail(PendingAmendmentExistsError);
                }

                var amendment = new ProliferationGranular
                {
                    Id = Guid.NewGuid(),
                    ProjectId = project.Id,
                    Source = dto.Source,
                    UnitName = unit,
                    ProliferationDate = date,
                    Quantity = dto.Quantity,
                    Remarks = remarks,
                    AmendsId = entity.Id,
                    ApprovalStatus = ApprovalStatus.Pending,
                    SubmittedByUserId = actor.Id,
                    CreatedOnUtc = now,
                    LastUpdatedOnUtc = now,
                    RowVersion = Array.Empty<byte>()
                };

                _db.ProliferationGranularEntries.Add(amendment);
                await using var amendmentTransaction = await BeginWriteAsync(ct);
                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException) when (!ct.IsCancellationRequested)
                {
                    await RollbackAsync(amendmentTransaction, ct);

                    if (await HasPendingGranularAmendmentAsync(entity.Id, amendment.Id, ct))
                    {
                        return ServiceResult.Fail(PendingAmendmentExistsError);
                    }

                    if (!await _db.ProliferationGranularEntries.AsNoTracking().AnyAsync(g => g.Id == entity.Id, ct))
                    {
                        return ServiceResult.Fail(AmendedRecordDeletedError);
                    }

                    throw;
                }

                await Audit.Events
                    .ProliferationGranularRecorded(amendment.ProjectId, project.Name, amendment.Source, amendment.UnitName, amendment.ProliferationDate, amendment.Quantity, amendment.ApprovalStatus, actor.Id, "Amend", amendment.Id, entity.Id)
                    .WriteAsync(_audit, userName: actor.UserName);

                await CommitAsync(amendmentTransaction, ct);

                return ServiceResult.Ok(amendment.Id);
            }

            if (!canApproveImmediately
                && entity.AmendsId.HasValue
                && await HasPendingGranularAmendmentAsync(entity.AmendsId.Value, entity.Id, ct))
            {
                return ServiceResult.Fail(PendingAmendmentExistsError);
            }

            entity.ProjectId = project.Id;
            entity.Source = dto.Source;
            entity.UnitName = unit;
            entity.ProliferationDate = date;
            entity.Quantity = dto.Quantity;
            entity.Remarks = remarks;
            entity.LastUpdatedOnUtc = now;
            _db.Entry(entity).Property(e => e.RowVersion).OriginalValue = rowVersion;

            var amendedRecordId = entity.AmendsId;
            var saved = entity;
            if (canApproveImmediately)
            {
                saved = await ApplyGranularApprovalAsync(entity, actor.Id, now, ct);
            }
            else
            {
                entity.ApprovalStatus = ApprovalStatus.Pending;
                entity.ApprovedByUserId = null;
                entity.ApprovedOnUtc = null;
            }

            await using var transaction = await BeginWriteAsync(ct);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult.Fail(ConcurrencyError);
            }
            catch (DbUpdateException) when (amendedRecordId.HasValue && !ct.IsCancellationRequested)
            {
                await RollbackAsync(transaction, ct);

                if (await HasPendingGranularAmendmentAsync(amendedRecordId.Value, entity.Id, ct))
                {
                    return ServiceResult.Fail(PendingAmendmentExistsError);
                }

                throw;
            }

            await Audit.Events
                .ProliferationGranularRecorded(saved.ProjectId, project.Name, saved.Source, saved.UnitName, saved.ProliferationDate, saved.Quantity, saved.ApprovalStatus, actor.Id, "Update", entity.Id, amendedRecordId)
                .WriteAsync(_audit, userName: actor.UserName);

            await CommitAsync(transaction, ct);

            return ServiceResult.Ok(saved.Id);
        }

        public async Task<ServiceResult> DecideYearlyAsync(Guid id, bool approve, string? rowVersionBase64, string? reason, ClaimsPrincipal user, CancellationToken ct)
        {
            if (user is null) throw new ArgumentNullException(nameof(user));

            var normalizedReason = Normalize(reason, 500);
            if (!approve && string.IsNullOrWhiteSpace(normalizedReason))
            {
                return ServiceResult.Fail("A reason is required when rejecting a record.");
            }

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

            // Only a pending record can be decided; approved and rejected records change
            // through an amendment or a resubmission, never by deciding them again.
            if (entity.ApprovalStatus != ApprovalStatus.Pending)
            {
                return ServiceResult.Fail(NotPendingError);
            }

            if (!TryDecodeRowVersion(rowVersionBase64, out var rowVersion))
            {
                return ServiceResult.Fail("The record is out of date. Refresh and try again.");
            }

            _db.Entry(entity).Property(e => e.RowVersion).OriginalValue = rowVersion;

            var now = _clock.UtcNow.UtcDateTime;
            var decided = entity;

            if (approve)
            {
                var approval = await ApplyYearlyApprovalAsync(entity, actor.Id, now, ct);
                if (approval.Error is not null)
                {
                    return ServiceResult.Fail(approval.Error);
                }

                decided = approval.Record!;
            }
            else
            {
                entity.ApprovalStatus = ApprovalStatus.Rejected;
                entity.ApprovedByUserId = null;
                entity.ApprovedOnUtc = null;
            }

            entity.LastUpdatedOnUtc = now;

            await using var transaction = await BeginWriteAsync(ct);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult.Fail(ConcurrencyError);
            }
            catch (DbUpdateException) when (approve && !ct.IsCancellationRequested)
            {
                // Two approvals for the same project, source and year raced; the unique
                // approved-year index let only one through.
                await RollbackAsync(transaction, ct);
                if (await HasOtherApprovedYearlyAsync(decided, ct))
                {
                    return ServiceResult.Fail(DuplicateYearlyError);
                }

                throw;
            }

            await Audit.Events
                .ProliferationYearlyDecided(decided.ProjectId, decided.Source, decided.Year, approve, actor.Id, normalizedReason, entity.Id, decided.Id == entity.Id ? null : decided.Id)
                .WriteAsync(_audit, userName: actor.UserName);

            await CommitAsync(transaction, ct);

            return ServiceResult.Ok(decided.Id);
        }

        public async Task<ServiceResult> DecideGranularAsync(Guid id, bool approve, string? rowVersionBase64, string? reason, ClaimsPrincipal user, CancellationToken ct)
        {
            if (user is null) throw new ArgumentNullException(nameof(user));

            var normalizedReason = Normalize(reason, 500);
            if (!approve && string.IsNullOrWhiteSpace(normalizedReason))
            {
                return ServiceResult.Fail("A reason is required when rejecting a record.");
            }

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

            if (entity.ApprovalStatus != ApprovalStatus.Pending)
            {
                return ServiceResult.Fail(NotPendingError);
            }

            if (!TryDecodeRowVersion(rowVersionBase64, out var rowVersion))
            {
                return ServiceResult.Fail("The record is out of date. Refresh and try again.");
            }

            _db.Entry(entity).Property(e => e.RowVersion).OriginalValue = rowVersion;

            var now = _clock.UtcNow.UtcDateTime;
            var decided = entity;

            if (approve)
            {
                decided = await ApplyGranularApprovalAsync(entity, actor.Id, now, ct);
            }
            else
            {
                entity.ApprovalStatus = ApprovalStatus.Rejected;
                entity.ApprovedByUserId = null;
                entity.ApprovedOnUtc = null;
            }

            entity.LastUpdatedOnUtc = now;

            await using var transaction = await BeginWriteAsync(ct);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult.Fail(ConcurrencyError);
            }

            await Audit.Events
                .ProliferationGranularDecided(decided.ProjectId, decided.Source, decided.ProliferationDate, approve, actor.Id, normalizedReason, entity.Id, decided.Id == entity.Id ? null : decided.Id)
                .WriteAsync(_audit, userName: actor.UserName);

            await CommitAsync(transaction, ct);

            return ServiceResult.Ok(decided.Id);
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

            if (entity.ApprovalStatus == ApprovalStatus.Approved && !RequiresImmediateApproval(user))
            {
                return ServiceResult.Fail("Approved records can be deleted only by Admin or HoD.");
            }

            if (!TryDecodeRowVersion(rowVersionBase64, out var rowVersion))
            {
                return ServiceResult.Fail("The record is out of date. Refresh and try again.");
            }

            _db.Entry(entity).Property(e => e.RowVersion).OriginalValue = rowVersion;
            _db.ProliferationYearlies.Remove(entity);

            // Amendments of a deleted record have nothing left to amend; leaving them would
            // let a later approval bring the deleted figure back as a new record.
            var linkedAmendments = await _db.ProliferationYearlies
                .Where(x => x.AmendsId == entity.Id)
                .ToListAsync(ct);
            _db.ProliferationYearlies.RemoveRange(linkedAmendments);

            await using var transaction = await BeginWriteAsync(ct);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult.Fail(ConcurrencyError);
            }

            foreach (var removed in linkedAmendments.Prepend(entity))
            {
                await Audit.Events
                    .ProliferationYearlyRecorded(removed.ProjectId, removed.Source, removed.Year, removed.TotalQuantity, removed.ApprovalStatus, actor.Id, "Delete", removed.Id, removed.AmendsId)
                    .WriteAsync(_audit, userName: actor.UserName);
            }

            await CommitAsync(transaction, ct);

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

            if (entity.ApprovalStatus == ApprovalStatus.Approved && !RequiresImmediateApproval(user))
            {
                return ServiceResult.Fail("Approved records can be deleted only by Admin or HoD.");
            }

            if (!TryDecodeRowVersion(rowVersionBase64, out var rowVersion))
            {
                return ServiceResult.Fail("The record is out of date. Refresh and try again.");
            }

            _db.Entry(entity).Property(e => e.RowVersion).OriginalValue = rowVersion;
            _db.ProliferationGranularEntries.Remove(entity);

            var linkedAmendments = await _db.ProliferationGranularEntries
                .Where(x => x.AmendsId == entity.Id)
                .ToListAsync(ct);
            _db.ProliferationGranularEntries.RemoveRange(linkedAmendments);

            await using var transaction = await BeginWriteAsync(ct);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult.Fail(ConcurrencyError);
            }

            var project = await _db.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == entity.ProjectId, ct);

            var projectName = project?.Name ?? "(unknown)";

            foreach (var removed in linkedAmendments.Prepend(entity))
            {
                await Audit.Events
                    .ProliferationGranularRecorded(removed.ProjectId, projectName, removed.Source, removed.UnitName, removed.ProliferationDate, removed.Quantity, removed.ApprovalStatus, actor.Id, "Delete", removed.Id, removed.AmendsId)
                    .WriteAsync(_audit, userName: actor.UserName);
            }

            await CommitAsync(transaction, ct);

            return ServiceResult.Ok();
        }

        // SECTION: Write transactions
        // A change and its audit entry commit together: if the audit insert fails the change
        // is rolled back, so nobody is told "error" about a change that was in fact saved and
        // then retries it into a duplicate. Non-relational test providers run without one.
        private async Task<IDbContextTransaction?> BeginWriteAsync(CancellationToken ct)
            => _db.Database.IsRelational() ? await _db.Database.BeginTransactionAsync(ct) : null;

        private static Task CommitAsync(IDbContextTransaction? transaction, CancellationToken ct)
            => transaction?.CommitAsync(ct) ?? Task.CompletedTask;

        // After a failed statement PostgreSQL refuses further commands in the transaction, so
        // roll back before running the follow-up queries that explain the failure.
        private static Task RollbackAsync(IDbContextTransaction? transaction, CancellationToken ct)
            => transaction?.RollbackAsync(ct) ?? Task.CompletedTask;

        // SECTION: Approval and amendment helpers
        // Approves a yearly record. An amendment is applied onto the approved record it
        // amends and then removed, so the record keeps its identity and is counted once.
        private async Task<(ProliferationYearly? Record, string? Error)> ApplyYearlyApprovalAsync(
            ProliferationYearly entity,
            string approverUserId,
            DateTime now,
            CancellationToken ct)
        {
            var amendedId = entity.AmendsId;
            var duplicate = await _db.ProliferationYearlies
                .AsNoTracking()
                .AnyAsync(y =>
                    y.Id != entity.Id &&
                    (amendedId == null || y.Id != amendedId.Value) &&
                    y.ProjectId == entity.ProjectId &&
                    y.Source == entity.Source &&
                    y.Year == entity.Year &&
                    y.ApprovalStatus == ApprovalStatus.Approved,
                    ct);

            if (duplicate)
            {
                return (null, DuplicateYearlyError);
            }

            var amended = amendedId.HasValue
                ? await _db.ProliferationYearlies.FirstOrDefaultAsync(y => y.Id == amendedId.Value, ct)
                : null;

            if (amended is { ApprovalStatus: ApprovalStatus.Approved })
            {
                amended.ProjectId = entity.ProjectId;
                amended.Source = entity.Source;
                amended.Year = entity.Year;
                amended.TotalQuantity = entity.TotalQuantity;
                amended.Remarks = entity.Remarks;
                amended.SubmittedByUserId = entity.SubmittedByUserId;
                amended.ApprovedByUserId = approverUserId;
                amended.ApprovedOnUtc = now;
                amended.LastUpdatedOnUtc = now;
                _db.ProliferationYearlies.Remove(entity);
                return (amended, null);
            }

            // The amended record is gone (or no longer approved): the amendment stands alone.
            entity.AmendsId = null;
            entity.ApprovalStatus = ApprovalStatus.Approved;
            entity.ApprovedByUserId = approverUserId;
            entity.ApprovedOnUtc = now;
            return (entity, null);
        }

        private async Task<ProliferationGranular> ApplyGranularApprovalAsync(
            ProliferationGranular entity,
            string approverUserId,
            DateTime now,
            CancellationToken ct)
        {
            var amended = entity.AmendsId.HasValue
                ? await _db.ProliferationGranularEntries.FirstOrDefaultAsync(g => g.Id == entity.AmendsId.Value, ct)
                : null;

            if (amended is { ApprovalStatus: ApprovalStatus.Approved })
            {
                amended.ProjectId = entity.ProjectId;
                amended.Source = entity.Source;
                amended.UnitName = entity.UnitName;
                amended.ProliferationDate = entity.ProliferationDate;
                amended.Quantity = entity.Quantity;
                amended.Remarks = entity.Remarks;
                amended.SubmittedByUserId = entity.SubmittedByUserId;
                amended.ApprovedByUserId = approverUserId;
                amended.ApprovedOnUtc = now;
                amended.LastUpdatedOnUtc = now;
                _db.ProliferationGranularEntries.Remove(entity);
                return amended;
            }

            entity.AmendsId = null;
            entity.ApprovalStatus = ApprovalStatus.Approved;
            entity.ApprovedByUserId = approverUserId;
            entity.ApprovedOnUtc = now;
            return entity;
        }

        private Task<bool> HasOtherApprovedYearlyAsync(ProliferationYearly record, CancellationToken ct)
            => _db.ProliferationYearlies
                .AsNoTracking()
                .AnyAsync(y =>
                    y.Id != record.Id &&
                    y.ProjectId == record.ProjectId &&
                    y.Source == record.Source &&
                    y.Year == record.Year &&
                    y.ApprovalStatus == ApprovalStatus.Approved,
                    ct);

        private Task<bool> HasPendingYearlyAmendmentAsync(Guid amendedId, Guid? excludeId, CancellationToken ct)
            => _db.ProliferationYearlies
                .AsNoTracking()
                .AnyAsync(y =>
                    y.AmendsId == amendedId &&
                    y.ApprovalStatus == ApprovalStatus.Pending &&
                    (excludeId == null || y.Id != excludeId.Value),
                    ct);

        private Task<bool> HasPendingGranularAmendmentAsync(Guid amendedId, Guid? excludeId, CancellationToken ct)
            => _db.ProliferationGranularEntries
                .AsNoTracking()
                .AnyAsync(g =>
                    g.AmendsId == amendedId &&
                    g.ApprovalStatus == ApprovalStatus.Pending &&
                    (excludeId == null || g.Id != excludeId.Value),
                    ct);

        private async Task<Project?> GetCompletedProjectAsync(int projectId, CancellationToken ct)
        {
            return await ProliferationProjectEligibility
                .CompletedVisibleProjects(_db.Projects.AsNoTracking())
                .FirstOrDefaultAsync(p => p.Id == projectId, ct);
        }

        // SECTION: Required field validation helpers
        private static ServiceResult? ValidateYearlyRequiredFields(ProliferationYearlyCreateDto dto, DateTimeOffset now)
        {
            if (dto.ProjectId <= 0)
            {
                return ServiceResult.Fail("Project is required.");
            }

            if (!ProliferationYearPolicy.IsValid(dto.Year, now))
            {
                return ServiceResult.Fail($"Year must be between {ProliferationYearPolicy.MinimumYear} and {ProliferationYearPolicy.GetMaximumYear(now)}.");
            }

            if (dto.TotalQuantity < 0)
            {
                return ServiceResult.Fail("Total quantity must be zero or greater.");
            }

            return null;
        }

        private static ServiceResult? ValidateGranularRequiredFields(ProliferationGranularCreateDto dto)
        {
            if (dto.ProjectId <= 0)
            {
                return ServiceResult.Fail("Project is required.");
            }

            if (dto.ProliferationDateUtc == default)
            {
                return ServiceResult.Fail("Proliferation date is required.");
            }

            if (dto.Quantity <= 0)
            {
                return ServiceResult.Fail("Quantity must be greater than zero.");
            }

            return null;
        }

        private static ServiceResult? ValidatePreferenceRequiredFields(ProliferationYearPreferenceDto dto, DateTimeOffset now)
        {
            if (dto.ProjectId <= 0)
            {
                return ServiceResult.Fail("Project is required.");
            }

            if (!ProliferationYearPolicy.IsValid(dto.Year, now))
            {
                return ServiceResult.Fail($"Year must be between {ProliferationYearPolicy.MinimumYear} and {ProliferationYearPolicy.GetMaximumYear(now)}.");
            }

            if (dto.Source != ProliferationSource.Sdd && dto.Source != ProliferationSource.Abw515)
            {
                return ServiceResult.Fail("Unsupported source.");
            }

            if (!Enum.IsDefined(typeof(YearPreferenceMode), dto.Mode))
            {
                return ServiceResult.Fail("Preference mode is required.");
            }

            var sourceDefault = ProliferationAggregateReadService.GetDefaultMode(dto.Source);
            if (dto.Mode != sourceDefault && string.IsNullOrWhiteSpace(dto.Reason))
            {
                return ServiceResult.Fail("A reason is required for a counting exception.");
            }

            return null;
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

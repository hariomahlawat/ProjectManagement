using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class VisitService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IVisitPhotoService _photoService;

    public VisitService(ApplicationDbContext db, IClock clock, IAuditService audit, IVisitPhotoService photoService)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _photoService = photoService ?? throw new ArgumentNullException(nameof(photoService));
    }

    public async Task<IReadOnlyList<VisitListItem>> SearchAsync(VisitQueryOptions options, CancellationToken cancellationToken)
    {
        IQueryable<Visit> query = _db.Visits.AsNoTracking()
            .Include(x => x.VisitType)
            .Include(x => x.Photos);

        if (options.VisitTypeId.HasValue)
        {
            query = query.Where(x => x.VisitTypeId == options.VisitTypeId.Value);
        }

        if (options.StartDate.HasValue)
        {
            query = query.Where(x => x.DateOfVisit >= options.StartDate.Value);
        }

        if (options.EndDate.HasValue)
        {
            query = query.Where(x => x.DateOfVisit <= options.EndDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(options.RemarksQuery))
        {
            var text = options.RemarksQuery.Trim();
            if (_db.Database.IsNpgsql())
            {
                query = query.Where(x =>
                    EF.Functions.ILike(x.VisitorName, $"%{text}%") ||
                    (x.Remarks != null && EF.Functions.ILike(x.Remarks, $"%{text}%")));
            }
            else
            {
                query = query.Where(x =>
                    x.VisitorName.Contains(text) ||
                    (x.Remarks != null && x.Remarks.Contains(text)));
            }
        }

        var list = await query
            .OrderByDescending(x => x.DateOfVisit)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => new VisitListItem(
                x.Id,
                x.DateOfVisit,
                x.VisitTypeId,
                x.VisitType!.Name,
                x.VisitorName,
                x.Strength,
                x.Photos.Count,
                x.VisitType.IsActive,
                x.RowVersion))
            .ToListAsync(cancellationToken);

        return list;
    }

    public async Task<VisitDetails?> GetDetailsAsync(Guid id, CancellationToken cancellationToken)
    {
        var visit = await _db.Visits.AsNoTracking()
            .Include(x => x.VisitType)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (visit == null)
        {
            return null;
        }

        var photos = await _db.VisitPhotos.AsNoTracking()
            .Where(x => x.VisitId == id)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return new VisitDetails(visit, visit.VisitType!, photos);
    }

    public async Task<VisitMutationResult> CreateAsync(Guid visitTypeId, DateOnly dateOfVisit, string visitorName, int strength, string? remarks, string createdByUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(visitorName))
        {
            return VisitMutationResult.Invalid("Visitor name is required.");
        }

        var trimmedName = visitorName.Trim();

        if (strength <= 0)
        {
            return VisitMutationResult.Invalid("Strength must be greater than zero.");
        }

        var visitType = await _db.VisitTypes.FirstOrDefaultAsync(x => x.Id == visitTypeId, cancellationToken);
        if (visitType == null)
        {
            return VisitMutationResult.VisitTypeNotFound();
        }

        if (!visitType.IsActive)
        {
            return VisitMutationResult.VisitTypeInactive();
        }

        var now = _clock.UtcNow;
        var entity = new Visit
        {
            Id = Guid.NewGuid(),
            VisitTypeId = visitTypeId,
            DateOfVisit = dateOfVisit,
            VisitorName = trimmedName,
            Strength = strength,
            Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim(),
            CreatedAtUtc = now,
            CreatedByUserId = createdByUserId,
            LastModifiedAtUtc = now,
            LastModifiedByUserId = createdByUserId
        };

        _db.Visits.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        await Audit.Events.VisitCreated(entity.Id, visitTypeId, createdByUserId).WriteAsync(_audit);

        return VisitMutationResult.Success(entity);
    }

    public async Task<VisitMutationResult> UpdateAsync(Guid id, Guid visitTypeId, DateOnly dateOfVisit, string visitorName, int strength, string? remarks, byte[] rowVersion, string modifiedByUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(visitorName))
        {
            return VisitMutationResult.Invalid("Visitor name is required.");
        }

        var trimmedName = visitorName.Trim();

        if (strength <= 0)
        {
            return VisitMutationResult.Invalid("Strength must be greater than zero.");
        }

        var visit = await _db.Visits.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (visit == null)
        {
            return VisitMutationResult.NotFound();
        }

        var visitType = await _db.VisitTypes.FirstOrDefaultAsync(x => x.Id == visitTypeId, cancellationToken);
        if (visitType == null)
        {
            return VisitMutationResult.VisitTypeNotFound();
        }

        if (!visitType.IsActive)
        {
            return VisitMutationResult.VisitTypeInactive();
        }

        _db.Entry(visit).Property(x => x.RowVersion).OriginalValue = rowVersion;

        visit.VisitTypeId = visitTypeId;
        visit.DateOfVisit = dateOfVisit;
        visit.VisitorName = trimmedName;
        visit.Strength = strength;
        visit.Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
        visit.LastModifiedAtUtc = _clock.UtcNow;
        visit.LastModifiedByUserId = modifiedByUserId;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return VisitMutationResult.Concurrency();
        }

        await Audit.Events.VisitUpdated(id, visitTypeId, modifiedByUserId).WriteAsync(_audit);
        return VisitMutationResult.Success(visit);
    }

    public async Task<VisitDeletionResult> DeleteAsync(Guid id, byte[] rowVersion, string deletedByUserId, CancellationToken cancellationToken)
    {
        var visit = await _db.Visits
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (visit == null)
        {
            return VisitDeletionResult.NotFound();
        }

        _db.Entry(visit).Property(x => x.RowVersion).OriginalValue = rowVersion;

        var photoSnapshots = await _db.VisitPhotos.AsNoTracking()
            .Where(x => x.VisitId == id)
            .Select(x => new VisitPhoto
            {
                Id = x.Id,
                VisitId = x.VisitId,
                StorageKey = x.StorageKey,
                ContentType = x.ContentType,
                Width = x.Width,
                Height = x.Height,
                Caption = x.Caption,
                VersionStamp = x.VersionStamp,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        _db.Visits.Remove(visit);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return VisitDeletionResult.Concurrency();
        }

        if (photoSnapshots.Count > 0)
        {
            await _photoService.RemoveAllAsync(id, photoSnapshots, cancellationToken);
        }

        await Audit.Events.VisitDeleted(id, deletedByUserId).WriteAsync(_audit);
        return VisitDeletionResult.Success();
    }
}

public sealed record VisitQueryOptions(Guid? VisitTypeId, DateOnly? StartDate, DateOnly? EndDate, string? RemarksQuery);

public sealed record VisitListItem(Guid Id, DateOnly DateOfVisit, Guid VisitTypeId, string VisitTypeName, string VisitorName, int Strength, int PhotoCount, bool VisitTypeIsActive, byte[] RowVersion);

public sealed record VisitDetails(Visit Visit, VisitType VisitType, IReadOnlyList<VisitPhoto> Photos);

public sealed record VisitMutationResult(VisitMutationOutcome Outcome, Visit? Entity, IReadOnlyList<string> Errors)
{
    public static VisitMutationResult Success(Visit entity) => new(VisitMutationOutcome.Success, entity, Array.Empty<string>());

    public static VisitMutationResult NotFound() => new(VisitMutationOutcome.NotFound, null, Array.Empty<string>());

    public static VisitMutationResult VisitTypeNotFound() => new(VisitMutationOutcome.VisitTypeNotFound, null, new[] { "Selected visit type could not be found." });

    public static VisitMutationResult VisitTypeInactive() => new(VisitMutationOutcome.VisitTypeInactive, null, new[] { "The selected visit type is inactive." });

    public static VisitMutationResult Invalid(string error) => new(VisitMutationOutcome.Invalid, null, new[] { error });

    public static VisitMutationResult Concurrency() => new(VisitMutationOutcome.ConcurrencyConflict, null, new[] { "The visit was modified by another user. Please reload and try again." });
}

public enum VisitMutationOutcome
{
    Success,
    NotFound,
    VisitTypeNotFound,
    VisitTypeInactive,
    Invalid,
    ConcurrencyConflict
}

public sealed record VisitDeletionResult(VisitDeletionOutcome Outcome)
{
    public static VisitDeletionResult Success() => new(VisitDeletionOutcome.Success);

    public static VisitDeletionResult NotFound() => new(VisitDeletionOutcome.NotFound);

    public static VisitDeletionResult Concurrency() => new(VisitDeletionOutcome.ConcurrencyConflict);
}

public enum VisitDeletionOutcome
{
    Success,
    NotFound,
    ConcurrencyConflict
}

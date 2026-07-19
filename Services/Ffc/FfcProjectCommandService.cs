using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;

namespace ProjectManagement.Services.Ffc;

public interface IFfcProjectCommandService
{
    Task<FfcCommandResult> SaveAsync(
        FfcProjectSaveCommand command,
        CancellationToken cancellationToken = default);

    Task<FfcCommandResult> DeleteAsync(
        long recordId,
        long projectId,
        string? rowVersion,
        CancellationToken cancellationToken = default);
}

public sealed class FfcProjectCommandService : IFfcProjectCommandService
{
    private const string UniqueLinkedProjectConstraint = "UX_FfcProjects_Record_LinkedProject";
    private const int NameMaxLength = 256;

    private readonly ApplicationDbContext _db;
    private readonly IFfcProgressService _progressService;
    private readonly IAuditService _audit;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<FfcProjectCommandService> _logger;

    public FfcProjectCommandService(
        ApplicationDbContext db,
        IFfcProgressService progressService,
        IAuditService audit,
        IHttpContextAccessor httpContextAccessor,
        ILogger<FfcProjectCommandService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _progressService = progressService ?? throw new ArgumentNullException(nameof(progressService));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FfcCommandResult> SaveAsync(
        FfcProjectSaveCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var recordExists = await _db.FfcRecords
            .AsNoTracking()
            .AnyAsync(record => record.Id == command.RecordId && !record.IsDeleted, cancellationToken);

        if (!recordExists)
        {
            return FfcCommandResult.Invalid("The FFC record was not found or has been archived.");
        }

        FfcProject? existingEntity = null;
        byte[]? decodedRowVersion = null;
        if (command.ProjectId.HasValue)
        {
            if (!TryDecodeRowVersion(command.RowVersion, out var decoded))
            {
                return FfcCommandResult.Conflict(
                    "The project version is missing or invalid. Reload the workspace and try again.");
            }

            decodedRowVersion = decoded;
            existingEntity = await _db.FfcProjects
                .FirstOrDefaultAsync(project =>
                    project.Id == command.ProjectId.Value &&
                    project.FfcRecordId == command.RecordId,
                    cancellationToken);

            if (existingEntity is null)
            {
                return FfcCommandResult.Invalid("The project entry was not found.");
            }
        }

        var linkedProjectId = command.IsLinkedProject ? command.LinkedProjectId : null;

        var existingLinkedProjectId = existingEntity?.LinkedProjectId;

        string? linkedProjectName = null;
        if (linkedProjectId.HasValue)
        {
            linkedProjectName = await _db.Projects
                .AsNoTracking()
                .Where(project =>
                    project.Id == linkedProjectId.Value &&
                    ((!project.IsDeleted && !project.IsBuild) ||
                     (existingLinkedProjectId.HasValue && existingLinkedProjectId.Value == project.Id)))
                .Select(project => project.Name)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var validation = await ValidateAsync(command, linkedProjectId, linkedProjectName, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var normalizedName = Normalize(command.DisplayName);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            normalizedName = linkedProjectName;
        }

        var normalizedProgress = Normalize(command.ProgressText);
        var shouldUpdateLinkedProgress = linkedProjectId.HasValue &&
                                         !string.IsNullOrWhiteSpace(normalizedProgress);

        if (shouldUpdateLinkedProgress &&
            existingEntity is not null &&
            existingLinkedProjectId == linkedProjectId)
        {
            var currentProgress = await _progressService.GetCurrentProgressAsync(
                new[]
                {
                    new FfcProgressTarget(
                        existingEntity.Id,
                        existingEntity.LinkedProjectId,
                        existingEntity.Remarks)
                },
                cancellationToken);

            if (currentProgress.TryGetValue(existingEntity.Id, out var current) &&
                string.Equals(
                    Normalize(current.Text),
                    normalizedProgress,
                    StringComparison.Ordinal))
            {
                shouldUpdateLinkedProgress = false;
            }
        }

        if (shouldUpdateLinkedProgress && command.Actor is null)
        {
            return FfcCommandResult.Invalid(
                "A valid Admin or HoD user is required to update linked project progress.");
        }

        var isDelivered = command.Position is FfcUnitPosition.DeliveredAwaitingInstallation or FfcUnitPosition.Installed;
        var isInstalled = command.Position == FfcUnitPosition.Installed;
        var deliveredOn = isDelivered ? command.DeliveredOn : null;
        var installedOn = isInstalled ? command.InstalledOn : null;

        FfcProject entity;
        IReadOnlyDictionary<string, string?>? before = null;
        var isCreate = !command.ProjectId.HasValue;

        if (isCreate)
        {
            entity = new FfcProject
            {
                FfcRecordId = command.RecordId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.FfcProjects.Add(entity);
        }
        else
        {
            entity = existingEntity!;
            before = Snapshot(entity);
            _db.Entry(entity).Property(project => project.RowVersion).OriginalValue = decodedRowVersion!;
        }

        entity.Name = normalizedName!;
        entity.LinkedProjectId = linkedProjectId;
        entity.Quantity = command.Quantity;
        entity.IsDelivered = isDelivered;
        entity.DeliveredOn = deliveredOn;
        entity.IsInstalled = isInstalled;
        entity.InstalledOn = installedOn;
        entity.Remarks = linkedProjectId.HasValue ? null : normalizedProgress;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        IDbContextTransaction? transaction = null;
        if (_db.Database.IsRelational())
        {
            transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);

            if (shouldUpdateLinkedProgress)
            {
                await _progressService.UpdateProgressAsync(
                    new FfcProgressUpdateCommand(
                        FfcProjectId: entity.Id,
                        RequestedLinkedProjectId: linkedProjectId,
                        ExternalRemarkId: null,
                        ProgressText: normalizedProgress,
                        Actor: command.Actor!),
                    cancellationToken);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            return FfcCommandResult.Conflict(
                "This project entry was modified by another user. Reload the workspace, review the latest values and save again.");
        }
        catch (DbUpdateException exception) when (IsConstraintViolation(exception, UniqueLinkedProjectConstraint))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            return FfcCommandResult.Invalid(
                fieldErrors: Error(
                    "LinkedProjectId",
                    "This PRISM project is already linked to the selected FFC record."));
        }
        catch (FfcProgressValidationException exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            return FfcCommandResult.Invalid(
                fieldErrors: Error("ProgressText", exception.Message));
        }
        catch (FfcProgressNotFoundException exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            return FfcCommandResult.Invalid(exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _logger.LogWarning(
                exception,
                "Authorisation failed while updating FFC project {ProjectId} in record {RecordId}.",
                command.ProjectId,
                command.RecordId);
            return FfcCommandResult.Invalid(
                "You are not authorised to update the linked Project progress remark.");
        }
        catch (Exception exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            _logger.LogError(
                exception,
                "Failed to save FFC project {ProjectId} in record {RecordId}.",
                command.ProjectId,
                command.RecordId);
            return FfcCommandResult.Invalid(
                "The project entry could not be saved. No changes were committed.");
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        await TryAuditAsync(
            isCreate
                ? "ProjectOfficeReports.FFC.RecordProjectCreated"
                : "ProjectOfficeReports.FFC.RecordProjectUpdated",
            entity,
            before,
            Snapshot(entity));

        return FfcCommandResult.Ok(
            entity.Id,
            isCreate ? "Project added to the FFC record." : "Project entry updated.");
    }

    public async Task<FfcCommandResult> DeleteAsync(
        long recordId,
        long projectId,
        string? rowVersion,
        CancellationToken cancellationToken = default)
    {
        if (!TryDecodeRowVersion(rowVersion, out var decodedRowVersion))
        {
            return FfcCommandResult.Conflict(
                "The project version is missing or invalid. Reload the workspace and try again.");
        }

        var entity = await _db.FfcProjects
            .FirstOrDefaultAsync(project => project.Id == projectId && project.FfcRecordId == recordId, cancellationToken);

        if (entity is null)
        {
            return FfcCommandResult.Invalid("The project entry was not found.");
        }

        var before = Snapshot(entity);
        _db.Entry(entity).Property(project => project.RowVersion).OriginalValue = decodedRowVersion;
        _db.FfcProjects.Remove(entity);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return FfcCommandResult.Conflict(
                "This project entry was modified by another user. Reload the workspace before deleting it.");
        }

        await TryAuditAsync(
            "ProjectOfficeReports.FFC.RecordProjectDeleted",
            entity,
            before,
            after: null);

        return FfcCommandResult.Ok(projectId, "Project removed from the FFC record.");
    }

    private async Task<FfcCommandResult?> ValidateAsync(
        FfcProjectSaveCommand command,
        int? linkedProjectId,
        string? linkedProjectName,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var displayName = Normalize(command.DisplayName);

        if (command.IsLinkedProject && !linkedProjectId.HasValue)
        {
            errors["LinkedProjectId"] = ["Select a PRISM project to link."];
        }
        else if (linkedProjectId.HasValue && string.IsNullOrWhiteSpace(linkedProjectName))
        {
            errors["LinkedProjectId"] = ["The selected PRISM project is unavailable."];
        }

        if (!linkedProjectId.HasValue && string.IsNullOrWhiteSpace(displayName))
        {
            errors["DisplayName"] = ["Enter a name for the unlinked FFC item."];
        }

        if (!string.IsNullOrWhiteSpace(displayName) && displayName.Length > NameMaxLength)
        {
            errors["DisplayName"] = [$"Project name must be {NameMaxLength} characters or fewer."];
        }

        if (command.Quantity < 1)
        {
            errors["Quantity"] = ["Quantity must be at least 1."];
        }

        if (command.Position == FfcUnitPosition.Planned &&
            (command.DeliveredOn.HasValue || command.InstalledOn.HasValue))
        {
            errors["Position"] = ["Planned items cannot have delivery or installation dates."];
        }

        if (command.Position == FfcUnitPosition.DeliveredAwaitingInstallation && command.InstalledOn.HasValue)
        {
            errors["InstalledOn"] = ["An installation date can only be recorded when the item is installed."];
        }

        if (command.DeliveredOn.HasValue &&
            command.InstalledOn.HasValue &&
            command.InstalledOn.Value < command.DeliveredOn.Value)
        {
            errors["InstalledOn"] = ["Installation date cannot be earlier than delivery date."];
        }

        var progress = Normalize(command.ProgressText);
        if (!string.IsNullOrWhiteSpace(progress) && progress.Length > FfcProgressService.MaxProgressLength)
        {
            errors["ProgressText"] =
                [$"Current progress must be {FfcProgressService.MaxProgressLength} characters or fewer."];
        }

        if (linkedProjectId.HasValue)
        {
            var duplicateExists = await _db.FfcProjects
                .AsNoTracking()
                .AnyAsync(project =>
                    project.FfcRecordId == command.RecordId &&
                    project.LinkedProjectId == linkedProjectId.Value &&
                    (!command.ProjectId.HasValue || project.Id != command.ProjectId.Value),
                    cancellationToken);

            if (duplicateExists)
            {
                errors["LinkedProjectId"] =
                    ["This PRISM project is already linked to the selected FFC record."];
            }
        }

        return errors.Count == 0
            ? null
            : FfcCommandResult.Invalid(fieldErrors: errors);
    }

    private async Task TryAuditAsync(
        string action,
        FfcProject project,
        IReadOnlyDictionary<string, string?>? before,
        IReadOnlyDictionary<string, string?>? after)
    {
        try
        {
            var http = _httpContextAccessor.HttpContext;
            var user = http?.User;
            var data = new Dictionary<string, string?>
            {
                ["ProjectId"] = project.Id.ToString(CultureInfo.InvariantCulture),
                ["RecordId"] = project.FfcRecordId.ToString(CultureInfo.InvariantCulture)
            };

            if (before is not null)
            {
                foreach (var pair in before)
                {
                    data[$"Before.{pair.Key}"] = pair.Value;
                }
            }

            if (after is not null)
            {
                foreach (var pair in after)
                {
                    data[$"After.{pair.Key}"] = pair.Value;
                }
            }

            await _audit.LogAsync(
                action,
                userId: user?.FindFirstValue(ClaimTypes.NameIdentifier),
                userName: user?.Identity?.Name,
                data: data,
                http: http);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to write FFC audit entry {Action} for project {ProjectId}.",
                action,
                project.Id);
        }
    }

    private static Dictionary<string, string?> Snapshot(FfcProject project)
        => new(StringComparer.Ordinal)
        {
            ["Name"] = project.Name,
            ["LinkedProjectId"] = project.LinkedProjectId?.ToString(CultureInfo.InvariantCulture),
            ["Quantity"] = project.Quantity.ToString(CultureInfo.InvariantCulture),
            ["IsDelivered"] = project.IsDelivered.ToString(),
            ["DeliveredOn"] = project.DeliveredOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["IsInstalled"] = project.IsInstalled.ToString(),
            ["InstalledOn"] = project.InstalledOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["Remarks"] = project.Remarks
        };

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, string[]> Error(string key, string message)
        => new Dictionary<string, string[]>(StringComparer.Ordinal) { [key] = [message] };

    private static bool TryDecodeRowVersion(string? value, out byte[] rowVersion)
    {
        rowVersion = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            rowVersion = Convert.FromBase64String(value);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsConstraintViolation(DbUpdateException exception, string constraintName)
        => exception.InnerException is PostgresException postgresException
           && string.Equals(postgresException.ConstraintName, constraintName, StringComparison.Ordinal);
}

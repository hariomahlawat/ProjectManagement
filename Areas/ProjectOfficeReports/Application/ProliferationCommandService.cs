using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Areas.ProjectOfficeReports.Proliferation.ViewModels;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProliferationCommandService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;

    public ProliferationCommandService(ApplicationDbContext db, IClock clock, IAuditService audit)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<ProliferationYearly> CreateYearlyAsync(
        ProliferationYearlyRequestModel model,
        ApplicationUser actor,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (actor is null)
        {
            throw new ArgumentNullException(nameof(actor));
        }

        var project = await GetCompletedProjectAsync(model.ProjectId, cancellationToken);
        var now = _clock.UtcNow.UtcDateTime;
        var remarks = Normalize(model.Remarks, 500);

        var entity = new ProliferationYearly
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Source = model.Source,
            Year = model.Year,
            TotalQuantity = model.TotalQuantity,
            Remarks = remarks,
            SubmittedByUserId = actor.Id,
            CreatedOnUtc = now,
            LastUpdatedOnUtc = now,
            RowVersion = Array.Empty<byte>()
        };

        if (entity.TotalQuantity < 0)
        {
            throw new ValidationException("TotalQuantity must be zero or greater.");
        }

        if (RequiresImmediateApproval(principal))
        {
            await EnsureNoApprovedDuplicateAsync(model.ProjectId, model.Source, model.Year, cancellationToken);
            entity.ApprovalStatus = ApprovalStatus.Approved;
            entity.ApprovedByUserId = actor.Id;
            entity.ApprovedOnUtc = now;
        }
        else
        {
            entity.ApprovalStatus = ApprovalStatus.Pending;
        }

        _db.ProliferationYearlies.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events
            .ProliferationYearlyRecorded(project.Id, entity.Source, entity.Year, entity.TotalQuantity, entity.ApprovalStatus, actor.Id, "Create")
            .WriteAsync(_audit, userName: actor.UserName);

        return entity;
    }

    public async Task<ProliferationGranular> CreateGranularAsync(
        ProliferationGranularRequestModel model,
        ApplicationUser actor,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (actor is null)
        {
            throw new ArgumentNullException(nameof(actor));
        }

        var project = await GetCompletedProjectAsync(model.ProjectId, cancellationToken);
        var now = _clock.UtcNow.UtcDateTime;

        var simulator = RequireValue(model.SimulatorName, nameof(model.SimulatorName), 200);
        var unit = RequireValue(model.UnitName, nameof(model.UnitName), 200);
        var remarks = Normalize(model.Remarks, 500);

        if (model.Quantity < 0)
        {
            throw new ValidationException("Quantity must be zero or greater.");
        }

        var entity = new ProliferationGranular
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Source = ProliferationSource.Sdd,
            SimulatorName = simulator,
            UnitName = unit,
            ProliferationDate = model.ProliferationDate,
            Quantity = model.Quantity,
            Remarks = remarks,
            SubmittedByUserId = actor.Id,
            CreatedOnUtc = now,
            LastUpdatedOnUtc = now,
            RowVersion = Array.Empty<byte>()
        };

        if (RequiresImmediateApproval(principal))
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
        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events
            .ProliferationGranularRecorded(project.Id, entity.Source, entity.SimulatorName, entity.UnitName, entity.ProliferationDate, entity.Quantity, entity.ApprovalStatus, actor.Id, "Create")
            .WriteAsync(_audit, userName: actor.UserName);

        return entity;
    }

    public async Task<ProliferationYearPreference> SetPreferenceAsync(
        ProliferationPreferenceRequestModel model,
        ApplicationUser actor,
        CancellationToken cancellationToken)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (actor is null)
        {
            throw new ArgumentNullException(nameof(actor));
        }

        var project = await GetCompletedProjectAsync(model.ProjectId, cancellationToken);
        var now = _clock.UtcNow.UtcDateTime;

        var existing = await _db.ProliferationYearPreferences
            .FirstOrDefaultAsync(p => p.ProjectId == model.ProjectId && p.Source == model.Source && p.Year == model.Year, cancellationToken);

        var isNew = existing is null;

        if (existing is null)
        {
            existing = new ProliferationYearPreference
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Source = model.Source,
                Year = model.Year,
                Mode = model.Mode,
                SetByUserId = actor.Id,
                SetOnUtc = now
            };

            _db.ProliferationYearPreferences.Add(existing);
        }
        else
        {
            existing.Mode = model.Mode;
            existing.SetByUserId = actor.Id;
            existing.SetOnUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await Audit.Events
            .ProliferationPreferenceChanged(project.Id, model.Source, model.Year, actor.Id, actor.Id, isNew ? "Created" : "Updated")
            .WriteAsync(_audit, userName: actor.UserName);

        return existing!;
    }

    public async Task<ProliferationImportResult> ImportYearlyAsync(
        IFormFile file,
        ApplicationUser actor,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var rows = await ParseCsvAsync(file, cancellationToken);
        if (rows.Count == 0)
        {
            throw new ValidationException("The uploaded file is empty.");
        }

        ValidateHeader(rows[0], new[] { "ProjectCode", "Source", "Year", "TotalQuantity", "Remarks" });

        var errors = new List<ProliferationImportError>();
        var validEntries = new List<ProliferationYearly>();
        var now = _clock.UtcNow.UtcDateTime;
        var isApprover = RequiresImmediateApproval(principal);

        for (var index = 1; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            try
            {
                var code = RequireValue(row[0], "ProjectCode", 64);
                var source = ParseSource(row[1]);
                var year = ParseInt(row[2], "Year");
                var quantity = ParseInt(row[3], "TotalQuantity");
                var remarks = Normalize(row.ElementAtOrDefault(4), 500);

                var project = await GetCompletedProjectByCodeAsync(code, cancellationToken);
                if (project is null)
                {
                    throw new ValidationException($"Project '{code}' not found or not completed.");
                }

                if (quantity < 0)
                {
                    throw new ValidationException("TotalQuantity must be zero or greater.");
                }

                if (isApprover)
                {
                    await EnsureNoApprovedDuplicateAsync(project.Id, source, year, cancellationToken);
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
                errors.Add(new ProliferationImportError(index + 1, ex.Message));
            }
        }

        if (errors.Count > 0)
        {
            return new ProliferationImportResult(errors);
        }

        if (validEntries.Count == 0)
        {
            return new ProliferationImportResult(Array.Empty<ProliferationImportError>());
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var entry in validEntries)
        {
            _db.ProliferationYearlies.Add(entry);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var entry in validEntries)
        {
            await Audit.Events
                .ProliferationYearlyRecorded(entry.ProjectId, entry.Source, entry.Year, entry.TotalQuantity, entry.ApprovalStatus, actor.Id, "Import")
                .WriteAsync(_audit, userName: actor.UserName);
        }

        return new ProliferationImportResult(Array.Empty<ProliferationImportError>());
    }

    public async Task<ProliferationImportResult> ImportGranularAsync(
        IFormFile file,
        ApplicationUser actor,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var rows = await ParseCsvAsync(file, cancellationToken);
        if (rows.Count == 0)
        {
            throw new ValidationException("The uploaded file is empty.");
        }

        ValidateHeader(rows[0], new[] { "ProjectCode", "SimulatorName", "UnitName", "ProliferationDate(YYYY-MM-DD)", "Quantity", "Remarks" });

        var errors = new List<ProliferationImportError>();
        var validEntries = new List<ProliferationGranular>();
        var now = _clock.UtcNow.UtcDateTime;
        var isApprover = RequiresImmediateApproval(principal);

        for (var index = 1; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            try
            {
                var code = RequireValue(row[0], "ProjectCode", 64);
                var simulator = RequireValue(row[1], "SimulatorName", 200);
                var unit = RequireValue(row[2], "UnitName", 200);
                var date = ParseDate(row[3], "ProliferationDate");
                var quantity = ParseInt(row[4], "Quantity");
                var remarks = Normalize(row.ElementAtOrDefault(5), 500);

                var project = await GetCompletedProjectByCodeAsync(code, cancellationToken);
                if (project is null)
                {
                    throw new ValidationException($"Project '{code}' not found or not completed.");
                }

                if (quantity < 0)
                {
                    throw new ValidationException("Quantity must be zero or greater.");
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
                errors.Add(new ProliferationImportError(index + 1, ex.Message));
            }
        }

        if (errors.Count > 0)
        {
            return new ProliferationImportResult(errors);
        }

        if (validEntries.Count == 0)
        {
            return new ProliferationImportResult(Array.Empty<ProliferationImportError>());
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var entry in validEntries)
        {
            _db.ProliferationGranularEntries.Add(entry);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        foreach (var entry in validEntries)
        {
            await Audit.Events
                .ProliferationGranularRecorded(entry.ProjectId, entry.Source, entry.SimulatorName, entry.UnitName, entry.ProliferationDate, entry.Quantity, entry.ApprovalStatus, actor.Id, "Import")
                .WriteAsync(_audit, userName: actor.UserName);
        }

        return new ProliferationImportResult(Array.Empty<ProliferationImportError>());
    }

    private async Task<Project> GetCompletedProjectAsync(int projectId, CancellationToken cancellationToken)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && !p.IsDeleted && !p.IsArchived && p.LifecycleStatus == ProjectLifecycleStatus.Completed, cancellationToken);

        if (project is null)
        {
            throw new ValidationException("Only completed projects may record proliferation data.");
        }

        return project;
    }

    private async Task<Project?> GetCompletedProjectByCodeAsync(string projectCode, CancellationToken cancellationToken)
    {
        projectCode = projectCode.Trim();

        return await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.CaseFileNumber != null &&
                EF.Functions.ILike(p.CaseFileNumber!, projectCode) &&
                !p.IsDeleted &&
                !p.IsArchived &&
                p.LifecycleStatus == ProjectLifecycleStatus.Completed,
                cancellationToken);
    }

    private static bool RequiresImmediateApproval(ClaimsPrincipal principal)
        => principal.IsInRole("Admin") || principal.IsInRole("HoD");

    private async Task EnsureNoApprovedDuplicateAsync(int projectId, ProliferationSource source, int year, CancellationToken cancellationToken)
    {
        var exists = await _db.ProliferationYearlies
            .AsNoTracking()
            .AnyAsync(y => y.ProjectId == projectId && y.Source == source && y.Year == year && y.ApprovalStatus == ApprovalStatus.Approved, cancellationToken);

        if (exists)
        {
            throw new ValidationException("An approved yearly entry already exists for this project, source, and year.");
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

    private static async Task<List<string[]>> ParseCsvAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return new List<string[]>();
        }

        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var rows = new List<string[]>();
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

}

public sealed class ValidationException : Exception
{
    public ValidationException(string message) : base(message)
    {
    }
}

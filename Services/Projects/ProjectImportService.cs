using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Services.Projects;

public class ProjectImportService : IProjectImportService
{
    private static readonly string[] RequiredHeaders = { "Nomenclature" };

    private readonly ApplicationDbContext _db;

    public ProjectImportService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ProjectImportResult> ImportLegacyProjectsAsync(
        int projectCategoryId,
        int technicalCategoryId,
        IFormFile file,
        string importedByUserName)
    {
        if (file is null || file.Length == 0)
        {
            return new ProjectImportResult(false, "Upload a non-empty .xlsx file.", 0);
        }

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return new ProjectImportResult(false, "Upload a .xlsx file.", 0);
        }

        if (await _db.ProjectLegacyImports.AnyAsync(x =>
                x.ProjectCategoryId == projectCategoryId && x.TechnicalCategoryId == technicalCategoryId))
        {
            return new ProjectImportResult(false, "Import already performed for the selected categories.", 0);
        }

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        if (buffer.Length == 0)
        {
            return new ProjectImportResult(false, "Upload a non-empty .xlsx file.", 0);
        }

        var fileBytes = buffer.ToArray();
        buffer.Position = 0;

        using var workbook = new XLWorkbook(buffer);
        var worksheet = workbook.Worksheets.FirstOrDefault();
        if (worksheet is null)
        {
            return new ProjectImportResult(false, "Worksheet not found in the uploaded file.", 0);
        }

        var headerCells = worksheet.Row(1).CellsUsed().ToList();
        if (headerCells.Count == 0)
        {
            return new ProjectImportResult(false, "Header row missing. Download the sample template and try again.", 0);
        }

        var headerLookup = headerCells
            .Select((cell, index) => new { Title = cell.GetString().Trim(), Index = index + 1 })
            .Where(x => !string.IsNullOrWhiteSpace(x.Title))
            .ToDictionary(x => x.Title, x => x.Index, StringComparer.OrdinalIgnoreCase);

        if (!RequiredHeaders.All(headerLookup.ContainsKey))
        {
            return new ProjectImportResult(false, "Header row missing required columns. Download the sample template and try again.", 0);
        }

        var importerIdentifier = string.IsNullOrWhiteSpace(importedByUserName)
            ? "legacy-import"
            : importedByUserName.Trim();

        var projectCreatedBy = Truncate(importerIdentifier, 64) ?? "legacy-import";
        var auditImportedBy = Truncate(importerIdentifier, 450) ?? "legacy-import";

        var projects = new List<Project>();
        var receivedRows = 0;
        var rowNumber = 1;

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            rowNumber++;

            if (RowIsEmpty(row))
            {
                continue;
            }

            receivedRows++;

            var name = GetCellString(row, headerLookup["Nomenclature"]);
            if (string.IsNullOrWhiteSpace(name))
            {
                return new ProjectImportResult(false, $"Row {rowNumber}: Nomenclature is required.", 0);
            }

            string? armService = null;
            if (headerLookup.TryGetValue("ArmService", out var armCol))
            {
                armService = Truncate(GetCellString(row, armCol), 200);
            }

            short? yearOfDevelopment = null;
            if (headerLookup.TryGetValue("YearOfDevp", out var yearCol))
            {
                yearOfDevelopment = ParseYear(row.Cell(yearCol));
            }

            decimal? costLakhs = null;
            if (headerLookup.TryGetValue("CostLakhs", out var costCol))
            {
                costLakhs = ParseDecimal(row.Cell(costCol));
            }

            var project = new Project
            {
                Name = Truncate(name, 100),
                Description = null,
                ArmService = armService,
                YearOfDevelopment = yearOfDevelopment,
                CostLakhs = costLakhs,
                CategoryId = projectCategoryId,
                TechnicalCategoryId = technicalCategoryId,
                CreatedByUserId = projectCreatedBy,
                IsLegacy = true,
                LifecycleStatus = ProjectLifecycleStatus.Completed,
                CompletedYear = yearOfDevelopment,   // map YearOfDevp -> CompletedYear
                CreatedAt = DateTime.UtcNow
            };

            projects.Add(project);
        }

        if (projects.Count == 0)
        {
            return new ProjectImportResult(false, "No valid rows found in the file.", 0);
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        try
        {
            if (await _db.ProjectLegacyImports.AnyAsync(x =>
                    x.ProjectCategoryId == projectCategoryId && x.TechnicalCategoryId == technicalCategoryId))
            {
                await transaction.RollbackAsync();
                return new ProjectImportResult(false, "Import already performed for the selected categories.", 0);
            }

            _db.Projects.AddRange(projects);
            await _db.SaveChangesAsync();

            var audit = new ProjectLegacyImport
            {
                ProjectCategoryId = projectCategoryId,
                TechnicalCategoryId = technicalCategoryId,
                ImportedAtUtc = DateTime.UtcNow,
                ImportedByUserId = auditImportedBy,
                RowsReceived = receivedRows,
                RowsImported = projects.Count,
                SourceFileHashSha256 = ComputeSha256(fileBytes)
            };

            _db.ProjectLegacyImports.Add(audit);
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();
            return new ProjectImportResult(true, null, projects.Count);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync();
            return new ProjectImportResult(false, "Import already performed for the selected categories.", 0);
        }
    }

    private static bool RowIsEmpty(IXLRow row)
    {
        foreach (var cell in row.CellsUsed())
        {
            if (!string.IsNullOrWhiteSpace(cell.GetString()))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetCellString(IXLRow row, int columnIndex)
    {
        var cell = row.Cell(columnIndex);
        return cell.GetString().Trim();
    }

    private static short? ParseYear(IXLCell cell)
    {
        if (cell.TryGetValue(out double numericValue))
        {
            var rounded = (int)Math.Round(numericValue, MidpointRounding.AwayFromZero);
            if (rounded >= 1900 && rounded <= 2100)
            {
                return (short)rounded;
            }
        }

        var text = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) &&
            year >= 1900 && year <= 2100)
        {
            return year;
        }

        return null;
    }

    private static decimal? ParseDecimal(IXLCell cell)
    {
        if (cell.TryGetValue(out decimal numericValue))
        {
            return numericValue < 0 ? null : decimal.Round(numericValue, 2);
        }

        var text = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) && value >= 0)
        {
            return decimal.Round(value, 2);
        }

        return null;
    }

    private static string ComputeSha256(byte[] buffer)
    {
        if (buffer.Length == 0)
        {
            return string.Empty;
        }

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(buffer);
        return Convert.ToHexString(hash);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        if (exception.InnerException is PostgresException postgres)
        {
            return postgres.SqlState == PostgresErrorCodes.UniqueViolation;
        }

        return false;
    }

    [return: NotNullIfNotNull(nameof(input))]
    private static string? Truncate(string? input, int maxLength)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        return input.Length <= maxLength ? input : input[..maxLength];
    }
}

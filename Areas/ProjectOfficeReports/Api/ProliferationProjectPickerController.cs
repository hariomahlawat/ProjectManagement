using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Data;
using ProjectManagement.Models;

namespace ProjectManagement.Areas.ProjectOfficeReports.Api;

/// <summary>
/// Provides the compact, ranked project index used by proliferation project comboboxes.
/// Only completed, non-build projects that can legitimately receive proliferation records
/// are returned.
/// </summary>
[ApiController]
[Route("api/proliferation/project-picker")]
[Authorize(Policy = ProjectOfficeReportsPolicies.ViewProliferationTracker)]
public sealed class ProliferationProjectPickerController : ControllerBase
{
    private const int DefaultTake = 20;
    private const int MaximumTake = 30;

    private readonly ApplicationDbContext _db;

    public ProliferationProjectPickerController(ApplicationDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProliferationProjectPickerOptionDto>>> Search(
        [FromQuery] string? q,
        [FromQuery] int take = DefaultTake,
        CancellationToken cancellationToken = default)
    {
        var boundedTake = Math.Clamp(take, 5, MaximumTake);
        var candidates = await GetEligibleProjectsQuery()
            .OrderBy(project => project.Name)
            .Select(project => new ProjectPickerCandidate(
                project.Id,
                project.Name,
                project.CaseFileNumber,
                project.TechnicalCategory != null ? project.TechnicalCategory.Name : null))
            .ToListAsync(cancellationToken);

        var query = (q ?? string.Empty).Trim();
        IEnumerable<ProjectPickerCandidate> ranked = candidates;

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalizedQuery = Normalize(query);
            ranked = candidates
                .Select(project => new
                {
                    Project = project,
                    Score = GetMatchScore(project, normalizedQuery)
                })
                .Where(result => result.Score < int.MaxValue)
                .OrderBy(result => result.Score)
                .ThenBy(result => result.Project.Name, StringComparer.OrdinalIgnoreCase)
                .Select(result => result.Project);
        }

        var response = ranked
            .Take(boundedTake)
            .Select(ToDto)
            .ToList();

        return Ok(response);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProliferationProjectPickerOptionDto>> GetById(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (id <= 0)
        {
            return BadRequest("A valid project id is required.");
        }

        var project = await GetEligibleProjectsQuery()
            .Where(candidate => candidate.Id == id)
            .Select(candidate => new ProjectPickerCandidate(
                candidate.Id,
                candidate.Name,
                candidate.CaseFileNumber,
                candidate.TechnicalCategory != null ? candidate.TechnicalCategory.Name : null))
            .FirstOrDefaultAsync(cancellationToken);

        return project is null ? NotFound() : Ok(ToDto(project));
    }

    private IQueryable<Project> GetEligibleProjectsQuery()
        => _db.Projects
            .AsNoTracking()
            .Where(project =>
                !project.IsDeleted &&
                !project.IsArchived &&
                !project.IsBuild &&
                project.LifecycleStatus == ProjectLifecycleStatus.Completed);

    private static ProliferationProjectPickerOptionDto ToDto(ProjectPickerCandidate project)
        => new()
        {
            Id = project.Id,
            Name = project.Name,
            Code = project.Code,
            TechnicalCategory = project.TechnicalCategory,
            Display = string.IsNullOrWhiteSpace(project.Code)
                ? project.Name
                : $"{project.Name} ({project.Code})"
        };

    private static int GetMatchScore(ProjectPickerCandidate project, string normalizedQuery)
    {
        if (string.IsNullOrEmpty(normalizedQuery))
        {
            return 0;
        }

        var normalizedName = Normalize(project.Name);
        var normalizedCode = Normalize(project.Code);
        var normalizedCategory = Normalize(project.TechnicalCategory);
        var normalizedAcronym = BuildAcronym(project.Name);

        if (normalizedCode == normalizedQuery) return 0;
        if (normalizedAcronym == normalizedQuery) return 1;
        if (normalizedName == normalizedQuery) return 2;
        if (normalizedCode.StartsWith(normalizedQuery, StringComparison.Ordinal)) return 3;
        if (normalizedAcronym.StartsWith(normalizedQuery, StringComparison.Ordinal)) return 4;
        if (normalizedName.StartsWith(normalizedQuery, StringComparison.Ordinal)) return 5;
        if (normalizedCode.Contains(normalizedQuery, StringComparison.Ordinal)) return 6;
        if (normalizedAcronym.Contains(normalizedQuery, StringComparison.Ordinal)) return 7;
        if (normalizedName.Contains(normalizedQuery, StringComparison.Ordinal)) return 8;
        if (normalizedCategory.Contains(normalizedQuery, StringComparison.Ordinal)) return 9;

        return int.MaxValue;
    }

    private static string BuildAcronym(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var acronym = new StringBuilder();
        var startOfToken = true;

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (startOfToken)
                {
                    acronym.Append(char.ToLowerInvariant(character));
                    startOfToken = false;
                }
            }
            else
            {
                startOfToken = true;
            }
        }

        return acronym.ToString();
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(char.ToLowerInvariant(character));
            }
        }

        return normalized.ToString();
    }

    private sealed record ProjectPickerCandidate(
        int Id,
        string Name,
        string? Code,
        string? TechnicalCategory);
}

public sealed class ProliferationProjectPickerOptionDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Code { get; init; }
    public string? TechnicalCategory { get; init; }
    public string Display { get; init; } = string.Empty;
}

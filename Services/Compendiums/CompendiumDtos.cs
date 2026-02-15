using System;
using System.Collections.Generic;

namespace ProjectManagement.Services.Compendiums;

public sealed record CompendiumProjectDto(
    int ProjectId,
    string ProjectName,
    string TechnicalCategoryName,
    int? CompletionYearValue,
    string CompletionYearDisplay,
    string ArmServiceDisplay,
    decimal? ProliferationCostLakhs,
    int? CoverPhotoId,
    string DescriptionMarkdown);

public sealed record CompendiumCategoryGroupDto(
    string TechnicalCategoryName,
    IReadOnlyList<CompendiumProjectDto> Projects);

public sealed record CompendiumPdfDataDto(
    string Title,
    string UnitDisplayName,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<CompendiumCategoryGroupDto> Groups);

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ProjectManagement.Areas.ProjectOfficeReports.SocialMedia.ViewModels;

public sealed class SocialMediaEventListFilter
{
    public Guid? EventTypeId { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    public string? SearchQuery { get; init; }

    public string? Platform { get; init; }

    public bool OnlyActiveEventTypes { get; init; }

    public IReadOnlyList<SelectListItem> EventTypeOptions { get; init; } = Array.Empty<SelectListItem>();

    public string? StartDateValue => StartDate?.ToString("yyyy-MM-dd");

    public string? EndDateValue => EndDate?.ToString("yyyy-MM-dd");
}

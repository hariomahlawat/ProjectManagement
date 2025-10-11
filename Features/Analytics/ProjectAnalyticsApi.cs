using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ProjectManagement.Services;
using ProjectManagement.Services.Analytics;
using ProjectManagement.Services.Projects;
using ProjectManagement.Utilities;

namespace ProjectManagement.Features.Analytics;

internal static class ProjectAnalyticsApi
{
    public static void MapProjectAnalyticsApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/analytics/projects")
            .RequireAuthorization(new AuthorizeAttribute());

        group.MapGet("/category-share", async (
            [FromQuery] ProjectLifecycleFilter lifecycle,
            ProjectAnalyticsService service,
            CancellationToken cancellationToken) =>
        {
            var normalized = NormalizeLifecycle(lifecycle);
            var result = await service.GetCategoryShareAsync(normalized, cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/stage-distribution", async (
            [FromQuery] ProjectLifecycleFilter lifecycle,
            [FromQuery] int? categoryId,
            ProjectAnalyticsService service,
            CancellationToken cancellationToken) =>
        {
            var normalized = NormalizeLifecycle(lifecycle);
            var result = await service.GetStageDistributionAsync(normalized, categoryId, cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/lifecycle-breakdown", async (
            [FromQuery] int? categoryId,
            ProjectAnalyticsService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetLifecycleBreakdownAsync(categoryId, cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/monthly-stage-completions", async (
            [FromQuery] string? fromMonth,
            [FromQuery] string? toMonth,
            [FromQuery] ProjectLifecycleFilter lifecycle,
            [FromQuery] int? categoryId,
            ProjectAnalyticsService service,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            var ist = TimeZoneHelper.GetIst();
            var todayIst = TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow.UtcDateTime, ist);
            var defaultEnd = new DateOnly(todayIst.Year, todayIst.Month, 1);
            var defaultStart = defaultEnd.AddMonths(-5);

            var startMonth = ParseMonthOrDefault(fromMonth, defaultStart);
            var endMonth = ParseMonthOrDefault(toMonth, defaultEnd);
            var normalized = NormalizeLifecycle(lifecycle);

            var result = await service.GetMonthlyStageCompletionsAsync(normalized, categoryId, startMonth, endMonth, cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/slip-buckets", async (
            [FromQuery] ProjectLifecycleFilter lifecycle,
            [FromQuery] int? categoryId,
            [FromQuery] int? technicalCategoryId,
            ProjectAnalyticsService service,
            CancellationToken cancellationToken) =>
        {
            var normalized = NormalizeLifecycle(lifecycle);
            var result = await service.GetSlipBucketsAsync(normalized, categoryId, technicalCategoryId, cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/top-overdue", async (
            [FromQuery] ProjectLifecycleFilter lifecycle,
            [FromQuery] int? categoryId,
            [FromQuery] int? technicalCategoryId,
            [FromQuery] int? take,
            ProjectAnalyticsService service,
            CancellationToken cancellationToken) =>
        {
            var normalized = NormalizeLifecycle(lifecycle);
            var size = take.GetValueOrDefault(5);
            var result = await service.GetTopOverdueProjectsAsync(normalized, categoryId, technicalCategoryId, size, cancellationToken);
            return Results.Ok(result);
        });
    }

    private static ProjectLifecycleFilter NormalizeLifecycle(ProjectLifecycleFilter lifecycle)
    {
        return lifecycle switch
        {
            ProjectLifecycleFilter.Active or ProjectLifecycleFilter.Completed or ProjectLifecycleFilter.Cancelled => lifecycle,
            _ => ProjectLifecycleFilter.All
        };
    }

    private static DateOnly ParseMonthOrDefault(string? input, DateOnly fallback)
    {
        if (!string.IsNullOrWhiteSpace(input) &&
            DateOnly.TryParseExact(input, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return new DateOnly(parsed.Year, parsed.Month, 1);
        }

        return fallback;
    }
}

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
            HttpContext context,
            IProjectAnalyticsService service,
            CancellationToken cancellationToken) =>
        {
            var (lifecycle, categoryId, technicalCategoryId) = ParseFilterQuery(context.Request);
            var result = await service.GetCategoryShareAsync(
                lifecycle,
                categoryId,
                technicalCategoryId,
                cancellationToken: cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/stage-distribution", async (
            HttpContext context,
            IProjectAnalyticsService service,
            CancellationToken cancellationToken) =>
        {
            var (lifecycle, categoryId, technicalCategoryId) = ParseFilterQuery(context.Request);
            var result = await service.GetStageDistributionAsync(
                lifecycle,
                categoryId,
                technicalCategoryId,
                cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/lifecycle-breakdown", async (
            HttpContext context,
            IProjectAnalyticsService service,
            CancellationToken cancellationToken) =>
        {
            var (_, categoryId, technicalCategoryId) = ParseFilterQuery(context.Request);
            var result = await service.GetLifecycleBreakdownAsync(categoryId, technicalCategoryId, cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/monthly-stage-completions", async (
            HttpContext context,
            IProjectAnalyticsService service,
            IClock clock,
            CancellationToken cancellationToken) =>
        {
            var request = context.Request;
            var ist = TimeZoneHelper.GetIst();
            var todayIst = TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow.UtcDateTime, ist);
            var defaultEnd = new DateOnly(todayIst.Year, todayIst.Month, 1);
            var defaultStart = defaultEnd.AddMonths(-5);

            var fromMonth = request.Query.TryGetValue("fromMonth", out var fromValues)
                ? fromValues.ToString()
                : null;
            var toMonth = request.Query.TryGetValue("toMonth", out var toValues)
                ? toValues.ToString()
                : null;

            var (lifecycle, categoryId, technicalCategoryId) = ParseFilterQuery(request);

            var startMonth = ParseMonthOrDefault(fromMonth, defaultStart);
            var endMonth = ParseMonthOrDefault(toMonth, defaultEnd);

            var result = await service.GetMonthlyStageCompletionsAsync(
                lifecycle,
                categoryId,
                technicalCategoryId,
                startMonth,
                endMonth,
                cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/slip-buckets", async (
            HttpContext context,
            IProjectAnalyticsService service,
            CancellationToken cancellationToken) =>
        {
            var (lifecycle, categoryId, technicalCategoryId) = ParseFilterQuery(context.Request);
            var result = await service.GetSlipBucketsAsync(lifecycle, categoryId, technicalCategoryId, cancellationToken);
            return Results.Ok(result);
        });

        group.MapGet("/top-overdue", async (
            HttpContext context,
            IProjectAnalyticsService service,
            CancellationToken cancellationToken) =>
        {
            var request = context.Request;
            var (lifecycle, categoryId, technicalCategoryId) = ParseFilterQuery(request);
            var take = ParseNullableInt(request, "take");
            var size = take.GetValueOrDefault(5);
            var result = await service.GetTopOverdueProjectsAsync(lifecycle, categoryId, technicalCategoryId, size, cancellationToken);
            return Results.Ok(result);
        });
    }

    private static (ProjectLifecycleFilter Lifecycle, int? CategoryId, int? TechnicalCategoryId) ParseFilterQuery(HttpRequest request)
    {
        var lifecycle = ProjectLifecycleFilter.All;
        if (request.Query.TryGetValue("lifecycle", out var lifecycleValues))
        {
            var lifecycleText = lifecycleValues.ToString();
            if (!string.IsNullOrWhiteSpace(lifecycleText) &&
                Enum.TryParse<ProjectLifecycleFilter>(lifecycleText, true, out var parsed))
            {
                lifecycle = NormalizeLifecycle(parsed);
            }
        }

        var categoryId = ParseNullableInt(request, "categoryId");
        var technicalCategoryId = ParseNullableInt(request, "technicalCategoryId");

        return (lifecycle, categoryId, technicalCategoryId);
    }

    private static int? ParseNullableInt(HttpRequest request, string key)
    {
        if (request.Query.TryGetValue(key, out var values))
        {
            var text = values.ToString();
            if (!string.IsNullOrWhiteSpace(text) &&
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static ProjectLifecycleFilter NormalizeLifecycle(ProjectLifecycleFilter lifecycle)
    {
        return lifecycle switch
        {
            ProjectLifecycleFilter.Active or
            ProjectLifecycleFilter.Completed or
            ProjectLifecycleFilter.Cancelled or
            ProjectLifecycleFilter.Legacy => lifecycle,
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

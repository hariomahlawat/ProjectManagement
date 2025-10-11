using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ProjectManagement.Services.Projects;

namespace ProjectManagement.Services.Analytics;

public interface IProjectAnalyticsService
{
    Task<CategoryShareResult> GetCategoryShareAsync(
        ProjectLifecycleFilter lifecycle,
        int? categoryId = null,
        int? technicalCategoryId = null,
        CancellationToken cancellationToken = default);

    Task<StageDistributionResult> GetStageDistributionAsync(
        ProjectLifecycleFilter lifecycle,
        int? categoryId,
        int? technicalCategoryId,
        CancellationToken cancellationToken = default);

    Task<LifecycleBreakdownResult> GetLifecycleBreakdownAsync(
        int? categoryId,
        int? technicalCategoryId,
        CancellationToken cancellationToken = default);

    Task<MonthlyStageCompletionsResult> GetMonthlyStageCompletionsAsync(
        ProjectLifecycleFilter lifecycle,
        int? categoryId,
        int? technicalCategoryId,
        DateOnly startMonth,
        DateOnly endMonth,
        CancellationToken cancellationToken = default);

    Task<SlipBucketResult> GetSlipBucketsAsync(
        ProjectLifecycleFilter lifecycle,
        int? categoryId,
        int? technicalCategoryId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<int>> GetProjectIdsForSlipBucketAsync(
        ProjectLifecycleFilter lifecycle,
        int? categoryId,
        int? technicalCategoryId,
        string bucketKey,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<int>? expandedCategoryIds = null);

    Task<TopOverdueProjectsResult> GetTopOverdueProjectsAsync(
        ProjectLifecycleFilter lifecycle,
        int? categoryId,
        int? technicalCategoryId,
        int take,
        CancellationToken cancellationToken = default);
}

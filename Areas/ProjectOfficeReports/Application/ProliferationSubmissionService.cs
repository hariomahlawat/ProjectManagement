using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;

namespace ProjectManagement.Areas.ProjectOfficeReports.Application;

public sealed class ProliferationSubmissionService
{
    private readonly ApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly ILogger<ProliferationSubmissionService> _logger;

    public ProliferationSubmissionService(
        ApplicationDbContext db,
        IClock clock,
        IAuditService audit,
        ILogger<ProliferationSubmissionService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProliferationRequestActionResult> SubmitYearlyAsync(
        ProliferationYearlySubmission submission,
        string submittedByUserId,
        CancellationToken cancellationToken)
    {
        if (submission is null)
        {
            throw new ArgumentNullException(nameof(submission));
        }

        if (string.IsNullOrWhiteSpace(submittedByUserId))
        {
            throw new ArgumentException("A valid user is required to submit a yearly record.", nameof(submittedByUserId));
        }

        var validationError = ValidateYearly(submission);
        if (validationError is not null)
        {
            return ProliferationRequestActionResult.ValidationFailed(validationError);
        }

        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == submission.ProjectId, cancellationToken);

        if (project is null)
        {
            return ProliferationRequestActionResult.NotFound();
        }

        if (project.LifecycleStatus != ProjectLifecycleStatus.Completed)
        {
            return ProliferationRequestActionResult.ValidationFailed("Only completed projects can submit yearly proliferation totals.");
        }

        var request = await _db.ProliferationYearlyRequests
            .FirstOrDefaultAsync(
                r => r.ProjectId == submission.ProjectId
                    && r.Source == submission.Source
                    && r.Year == submission.Year,
                cancellationToken);

        if (request is null)
        {
            request = new ProliferationYearlyRequest
            {
                Id = Guid.NewGuid(),
                ProjectId = submission.ProjectId,
                Source = submission.Source,
                Year = submission.Year
            };

            _db.ProliferationYearlyRequests.Add(request);
        }
        else if (request.DecisionState == ProliferationRequestDecisionState.Pending)
        {
            return ProliferationRequestActionResult.Conflict("A yearly submission is already pending approval.");
        }

        ApplyMetrics(request.Metrics, submission.DirectBeneficiaries, submission.IndirectBeneficiaries, submission.InvestmentValue);
        request.Notes = submission.Notes;
        request.SubmittedByUserId = submittedByUserId;
        request.SubmittedAtUtc = _clock.UtcNow;
        request.DecisionState = ProliferationRequestDecisionState.Pending;
        request.DecidedByUserId = null;
        request.DecidedAtUtc = null;
        request.DecisionNotes = null;
        request.RowVersion = Guid.NewGuid().ToByteArray();

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Unable to persist yearly submission for project {ProjectId}.", submission.ProjectId);
            return ProliferationRequestActionResult.ValidationFailed("Unable to save the yearly submission. Please try again.");
        }

        await Audit.Events.ProliferationYearlySubmitted(
                submission.ProjectId,
                submission.Source,
                submission.Year,
                submittedByUserId)
            .WriteAsync(_audit);

        var warning = await DetectYearlyGuardrailAsync(submission, submittedByUserId, cancellationToken);

        return ProliferationRequestActionResult.Success(request.Id, request.RowVersion, warning);
    }

    public async Task<ProliferationRequestActionResult> SubmitGranularAsync(
        ProliferationGranularSubmission submission,
        string submittedByUserId,
        CancellationToken cancellationToken)
    {
        if (submission is null)
        {
            throw new ArgumentNullException(nameof(submission));
        }

        if (string.IsNullOrWhiteSpace(submittedByUserId))
        {
            throw new ArgumentException("A valid user is required to submit a granular record.", nameof(submittedByUserId));
        }

        var validationError = ValidateGranular(submission);
        if (validationError is not null)
        {
            return ProliferationRequestActionResult.ValidationFailed(validationError);
        }

        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == submission.ProjectId, cancellationToken);

        if (project is null)
        {
            return ProliferationRequestActionResult.NotFound();
        }

        if (project.LifecycleStatus != ProjectLifecycleStatus.Completed)
        {
            return ProliferationRequestActionResult.ValidationFailed("Only completed projects can submit granular proliferation records.");
        }

        var request = await _db.ProliferationGranularRequests
            .FirstOrDefaultAsync(
                r => r.ProjectId == submission.ProjectId
                    && r.Source == submission.Source
                    && r.Year == submission.Year
                    && r.Granularity == submission.Granularity
                    && r.Period == submission.Period,
                cancellationToken);

        if (request is null)
        {
            request = new ProliferationGranularRequest
            {
                Id = Guid.NewGuid(),
                ProjectId = submission.ProjectId,
                Source = submission.Source,
                Year = submission.Year,
                Granularity = submission.Granularity,
                Period = submission.Period
            };

            _db.ProliferationGranularRequests.Add(request);
        }
        else if (request.DecisionState == ProliferationRequestDecisionState.Pending)
        {
            return ProliferationRequestActionResult.Conflict("A granular submission is already pending approval for the selected period.");
        }

        request.PeriodLabel = submission.PeriodLabel;
        ApplyMetrics(request.Metrics, submission.DirectBeneficiaries, submission.IndirectBeneficiaries, submission.InvestmentValue);
        request.Notes = submission.Notes;
        request.SubmittedByUserId = submittedByUserId;
        request.SubmittedAtUtc = _clock.UtcNow;
        request.DecisionState = ProliferationRequestDecisionState.Pending;
        request.DecidedByUserId = null;
        request.DecidedAtUtc = null;
        request.DecisionNotes = null;
        request.RowVersion = Guid.NewGuid().ToByteArray();

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Unable to persist granular submission for project {ProjectId}.", submission.ProjectId);
            return ProliferationRequestActionResult.ValidationFailed("Unable to save the granular submission. Please try again.");
        }

        await Audit.Events.ProliferationGranularSubmitted(
                submission.ProjectId,
                submission.Source,
                submission.Year,
                submission.Granularity,
                submission.Period,
                submittedByUserId)
            .WriteAsync(_audit);

        var warning = await DetectGranularGuardrailAsync(submission, submittedByUserId, cancellationToken);

        return ProliferationRequestActionResult.Success(request.Id, request.RowVersion, warning);
    }

    public async Task<ProliferationRequestActionResult> DecideYearlyAsync(
        Guid requestId,
        bool approve,
        string decisionUserId,
        byte[]? expectedRowVersion,
        string? decisionNotes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(decisionUserId))
        {
            throw new ArgumentException("A valid user is required to decide on a yearly submission.", nameof(decisionUserId));
        }

        var request = await _db.ProliferationYearlyRequests
            .Include(r => r.Project)
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (request is null)
        {
            return ProliferationRequestActionResult.NotFound();
        }

        if (request.DecisionState != ProliferationRequestDecisionState.Pending)
        {
            return ProliferationRequestActionResult.Conflict("The yearly submission has already been decided.");
        }

        if (!MatchesRowVersion(request.RowVersion, expectedRowVersion))
        {
            return ProliferationRequestActionResult.Conflict("The yearly submission was updated by someone else. Reload the page to continue.");
        }

        var now = _clock.UtcNow;
        ProliferationYearly? approvedYearly = null;
        var yearlyCreated = false;

        if (approve)
        {
            var yearly = await _db.ProliferationYearlies
                .FirstOrDefaultAsync(
                    y => y.ProjectId == request.ProjectId
                        && y.Source == request.Source
                        && y.Year == request.Year,
                    cancellationToken);

            if (yearly is null)
            {
                yearly = new ProliferationYearly
                {
                    Id = Guid.NewGuid(),
                    ProjectId = request.ProjectId,
                    Source = request.Source,
                    Year = request.Year,
                    CreatedByUserId = request.SubmittedByUserId,
                    CreatedAtUtc = now,
                    RowVersion = Guid.NewGuid().ToByteArray()
                };

                ApplyMetrics(yearly.Metrics, request.Metrics.DirectBeneficiaries, request.Metrics.IndirectBeneficiaries, request.Metrics.InvestmentValue);
                yearly.Notes = request.Notes;
                yearly.LastModifiedByUserId = decisionUserId;
                yearly.LastModifiedAtUtc = now;

                _db.ProliferationYearlies.Add(yearly);
                yearlyCreated = true;
            }
            else
            {
                ApplyMetrics(yearly.Metrics, request.Metrics.DirectBeneficiaries, request.Metrics.IndirectBeneficiaries, request.Metrics.InvestmentValue);
                yearly.Notes = request.Notes;
                yearly.LastModifiedByUserId = decisionUserId;
                yearly.LastModifiedAtUtc = now;
                yearly.RowVersion = Guid.NewGuid().ToByteArray();
            }

            approvedYearly = yearly;
        }

        request.DecisionState = approve
            ? ProliferationRequestDecisionState.Approved
            : ProliferationRequestDecisionState.Rejected;
        request.DecidedByUserId = decisionUserId;
        request.DecidedAtUtc = now;
        request.DecisionNotes = string.IsNullOrWhiteSpace(decisionNotes) ? null : decisionNotes.Trim();
        request.RowVersion = Guid.NewGuid().ToByteArray();

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict while deciding yearly request {RequestId}.", requestId);
            return ProliferationRequestActionResult.Conflict("The yearly submission was updated by someone else. Reload the page to continue.");
        }

        await Audit.Events.ProliferationYearlyDecided(
                request.ProjectId,
                request.Source,
                request.Year,
                approve,
                decisionUserId)
            .WriteAsync(_audit);

        if (approve && approvedYearly is not null)
        {
            var recordEvent = yearlyCreated
                ? Audit.Events.ProliferationRecordCreated(
                    approvedYearly.ProjectId,
                    approvedYearly.Source,
                    approvedYearly.Year,
                    approvedYearly.Metrics.DirectBeneficiaries,
                    approvedYearly.Metrics.IndirectBeneficiaries,
                    approvedYearly.Metrics.InvestmentValue,
                    decisionUserId,
                    origin: "Approval")
                : Audit.Events.ProliferationRecordEdited(
                    approvedYearly.ProjectId,
                    approvedYearly.Source,
                    approvedYearly.Year,
                    approvedYearly.Metrics.DirectBeneficiaries,
                    approvedYearly.Metrics.IndirectBeneficiaries,
                    approvedYearly.Metrics.InvestmentValue,
                    decisionUserId,
                    origin: "Approval");

            await recordEvent.WriteAsync(_audit);

            await Audit.Events.ProliferationRecordApproved(
                    request.Id,
                    approvedYearly.ProjectId,
                    approvedYearly.Source,
                    approvedYearly.Year,
                    approvedYearly.Metrics.DirectBeneficiaries,
                    approvedYearly.Metrics.IndirectBeneficiaries,
                    approvedYearly.Metrics.InvestmentValue,
                    decisionUserId,
                    granularity: null,
                    period: null,
                    periodLabel: null,
                    decisionNotes: request.DecisionNotes,
                    submittedByUserId: request.SubmittedByUserId)
                .WriteAsync(_audit);
        }

        return ProliferationRequestActionResult.Success(request.Id, request.RowVersion, null);
    }

    public async Task<ProliferationRequestActionResult> DecideGranularAsync(
        Guid requestId,
        bool approve,
        string decisionUserId,
        byte[]? expectedRowVersion,
        string? decisionNotes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(decisionUserId))
        {
            throw new ArgumentException("A valid user is required to decide on a granular submission.", nameof(decisionUserId));
        }

        var request = await _db.ProliferationGranularRequests
            .Include(r => r.Project)
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (request is null)
        {
            return ProliferationRequestActionResult.NotFound();
        }

        if (request.DecisionState != ProliferationRequestDecisionState.Pending)
        {
            return ProliferationRequestActionResult.Conflict("The granular submission has already been decided.");
        }

        if (!MatchesRowVersion(request.RowVersion, expectedRowVersion))
        {
            return ProliferationRequestActionResult.Conflict("The granular submission was updated by someone else. Reload the page to continue.");
        }

        var now = _clock.UtcNow;
        ProliferationGranular? approvedGranular = null;
        var granularCreated = false;

        if (approve)
        {
            var granular = await _db.ProliferationGranularEntries
                .FirstOrDefaultAsync(
                    g => g.ProjectId == request.ProjectId
                        && g.Source == request.Source
                        && g.Year == request.Year
                        && g.Granularity == request.Granularity
                        && g.Period == request.Period,
                    cancellationToken);

            if (granular is null)
            {
                granular = new ProliferationGranular
                {
                    Id = Guid.NewGuid(),
                    ProjectId = request.ProjectId,
                    Source = request.Source,
                    Year = request.Year,
                    Granularity = request.Granularity,
                    Period = request.Period,
                    CreatedByUserId = request.SubmittedByUserId,
                    CreatedAtUtc = now,
                    RowVersion = Guid.NewGuid().ToByteArray()
                };

                ApplyMetrics(granular.Metrics, request.Metrics.DirectBeneficiaries, request.Metrics.IndirectBeneficiaries, request.Metrics.InvestmentValue);
                granular.Notes = request.Notes;
                granular.PeriodLabel = request.PeriodLabel;
                granular.LastModifiedByUserId = decisionUserId;
                granular.LastModifiedAtUtc = now;

                _db.ProliferationGranularEntries.Add(granular);
                granularCreated = true;
            }
            else
            {
                ApplyMetrics(granular.Metrics, request.Metrics.DirectBeneficiaries, request.Metrics.IndirectBeneficiaries, request.Metrics.InvestmentValue);
                granular.Notes = request.Notes;
                granular.PeriodLabel = request.PeriodLabel;
                granular.LastModifiedByUserId = decisionUserId;
                granular.LastModifiedAtUtc = now;
                granular.RowVersion = Guid.NewGuid().ToByteArray();
            }

            approvedGranular = granular;
        }

        request.DecisionState = approve
            ? ProliferationRequestDecisionState.Approved
            : ProliferationRequestDecisionState.Rejected;
        request.DecidedByUserId = decisionUserId;
        request.DecidedAtUtc = now;
        request.DecisionNotes = string.IsNullOrWhiteSpace(decisionNotes) ? null : decisionNotes.Trim();
        request.RowVersion = Guid.NewGuid().ToByteArray();

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict while deciding granular request {RequestId}.", requestId);
            return ProliferationRequestActionResult.Conflict("The granular submission was updated by someone else. Reload the page to continue.");
        }

        await Audit.Events.ProliferationGranularDecided(
                request.ProjectId,
                request.Source,
                request.Year,
                request.Granularity,
                request.Period,
                approve,
                decisionUserId)
            .WriteAsync(_audit);

        if (approve && approvedGranular is not null)
        {
            var recordEvent = granularCreated
                ? Audit.Events.ProliferationRecordCreated(
                    approvedGranular.ProjectId,
                    approvedGranular.Source,
                    approvedGranular.Year,
                    approvedGranular.Metrics.DirectBeneficiaries,
                    approvedGranular.Metrics.IndirectBeneficiaries,
                    approvedGranular.Metrics.InvestmentValue,
                    decisionUserId,
                    origin: "Approval",
                    granularity: approvedGranular.Granularity,
                    period: approvedGranular.Period,
                    periodLabel: approvedGranular.PeriodLabel)
                : Audit.Events.ProliferationRecordEdited(
                    approvedGranular.ProjectId,
                    approvedGranular.Source,
                    approvedGranular.Year,
                    approvedGranular.Metrics.DirectBeneficiaries,
                    approvedGranular.Metrics.IndirectBeneficiaries,
                    approvedGranular.Metrics.InvestmentValue,
                    decisionUserId,
                    origin: "Approval",
                    granularity: approvedGranular.Granularity,
                    period: approvedGranular.Period,
                    periodLabel: approvedGranular.PeriodLabel);

            await recordEvent.WriteAsync(_audit);

            await Audit.Events.ProliferationRecordApproved(
                    request.Id,
                    approvedGranular.ProjectId,
                    approvedGranular.Source,
                    approvedGranular.Year,
                    approvedGranular.Metrics.DirectBeneficiaries,
                    approvedGranular.Metrics.IndirectBeneficiaries,
                    approvedGranular.Metrics.InvestmentValue,
                    decisionUserId,
                    granularity: approvedGranular.Granularity,
                    period: approvedGranular.Period,
                    periodLabel: approvedGranular.PeriodLabel,
                    decisionNotes: request.DecisionNotes,
                    submittedByUserId: request.SubmittedByUserId)
                .WriteAsync(_audit);
        }

        return ProliferationRequestActionResult.Success(request.Id, request.RowVersion, null);
    }

    private static void ApplyMetrics(
        ProliferationMetrics target,
        int? direct,
        int? indirect,
        decimal? investment)
    {
        target.DirectBeneficiaries = direct;
        target.IndirectBeneficiaries = indirect;
        target.InvestmentValue = investment;
    }

    private static bool MatchesRowVersion(byte[]? stored, byte[]? expected)
    {
        if (stored is null || stored.Length == 0)
        {
            return expected is null || expected.Length == 0;
        }

        if (expected is null || expected.Length == 0 || stored.Length != expected.Length)
        {
            return false;
        }

        for (var index = 0; index < stored.Length; index++)
        {
            if (stored[index] != expected[index])
            {
                return false;
            }
        }

        return true;
    }

    private static string? ValidateYearly(ProliferationYearlySubmission submission)
    {
        if (submission.Year < 1900 || submission.Year > 9999)
        {
            return "Year must be between 1900 and 9999.";
        }

        if (!Enum.IsDefined(typeof(ProliferationSource), submission.Source) || submission.Source == ProliferationSource.Unknown)
        {
            return "A valid data source is required.";
        }

        return ValidateMetrics(submission.DirectBeneficiaries, submission.IndirectBeneficiaries, submission.InvestmentValue);
    }

    private static string? ValidateGranular(ProliferationGranularSubmission submission)
    {
        if (submission.Year < 1900 || submission.Year > 9999)
        {
            return "Year must be between 1900 and 9999.";
        }

        if (!Enum.IsDefined(typeof(ProliferationSource), submission.Source) || submission.Source == ProliferationSource.Unknown)
        {
            return "A valid data source is required.";
        }

        if (!Enum.IsDefined(typeof(ProliferationGranularity), submission.Granularity))
        {
            return "A valid granularity is required.";
        }

        if (submission.Granularity == ProliferationGranularity.Monthly)
        {
            if (submission.Period < 1 || submission.Period > 12)
            {
                return "Month must be between 1 and 12.";
            }
        }
        else if (submission.Granularity == ProliferationGranularity.Quarterly)
        {
            if (submission.Period < 1 || submission.Period > 4)
            {
                return "Quarter must be between 1 and 4.";
            }
        }

        var metricsError = ValidateMetrics(submission.DirectBeneficiaries, submission.IndirectBeneficiaries, submission.InvestmentValue);
        if (metricsError is not null)
        {
            return metricsError;
        }

        if (!string.IsNullOrWhiteSpace(submission.PeriodLabel) && submission.PeriodLabel!.Length > 200)
        {
            return "Period label cannot exceed 200 characters.";
        }

        return null;
    }

    private static string? ValidateMetrics(int? direct, int? indirect, decimal? investment)
    {
        if (direct is < 0)
        {
            return "Direct beneficiaries cannot be negative.";
        }

        if (indirect is < 0)
        {
            return "Indirect beneficiaries cannot be negative.";
        }

        if (investment is < 0)
        {
            return "Investment value cannot be negative.";
        }

        return null;
    }

    private async Task<string?> DetectYearlyGuardrailAsync(
        ProliferationYearlySubmission submission,
        string userId,
        CancellationToken cancellationToken)
    {
        var hasGranular = await _db.ProliferationGranularEntries
            .AsNoTracking()
            .AnyAsync(
                g => g.ProjectId == submission.ProjectId
                    && g.Source == submission.Source
                    && g.Year == submission.Year,
                cancellationToken);

        if (!hasGranular)
        {
            return null;
        }

        var hasYearly = await _db.ProliferationYearlies
            .AsNoTracking()
            .AnyAsync(
                y => y.ProjectId == submission.ProjectId
                    && y.Source == submission.Source
                    && y.Year == submission.Year,
                cancellationToken);

        var mode = await DetermineEffectiveModeAsync(
            submission.ProjectId,
            submission.Source,
            submission.Year,
            userId,
            hasYearly,
            hasGranular: hasGranular,
            cancellationToken);

        if (mode == ProliferationPreferenceMode.UseGranular)
        {
            return "Yearly totals were submitted, but granular data is currently preferred. Update the preference if yearly metrics should take precedence.";
        }

        return null;
    }

    private async Task<string?> DetectGranularGuardrailAsync(
        ProliferationGranularSubmission submission,
        string userId,
        CancellationToken cancellationToken)
    {
        var hasYearly = await _db.ProliferationYearlies
            .AsNoTracking()
            .AnyAsync(
                y => y.ProjectId == submission.ProjectId
                    && y.Source == submission.Source
                    && y.Year == submission.Year,
                cancellationToken);

        if (!hasYearly)
        {
            return null;
        }

        var mode = await DetermineEffectiveModeAsync(
            submission.ProjectId,
            submission.Source,
            submission.Year,
            userId,
            hasYearly,
            hasGranular: true,
            cancellationToken);

        if (mode == ProliferationPreferenceMode.UseYearly)
        {
            return "Granular metrics were submitted, but yearly totals are currently preferred. Update the preference if granular metrics should take precedence.";
        }

        return null;
    }

    private async Task<ProliferationPreferenceMode> DetermineEffectiveModeAsync(
        int projectId,
        ProliferationSource source,
        int year,
        string userId,
        bool hasYearly,
        bool hasGranular,
        CancellationToken cancellationToken)
    {
        ProliferationYearPreference? preference = null;

        if (!string.IsNullOrWhiteSpace(userId))
        {
            preference = await _db.ProliferationYearPreferences
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    p => p.ProjectId == projectId
                        && p.Source == source
                        && p.UserId == userId,
                    cancellationToken);
        }

        if (preference is not null && preference.Year == year)
        {
            if (hasYearly)
            {
                return ProliferationPreferenceMode.UseYearly;
            }

            if (hasGranular)
            {
                return ProliferationPreferenceMode.UseGranular;
            }

            return ProliferationPreferenceMode.Auto;
        }

        if (hasYearly)
        {
            return ProliferationPreferenceMode.UseYearly;
        }

        if (hasGranular)
        {
            return ProliferationPreferenceMode.UseGranular;
        }

        return ProliferationPreferenceMode.Auto;
    }
}

public sealed record ProliferationYearlySubmission(
    int ProjectId,
    ProliferationSource Source,
    int Year,
    int? DirectBeneficiaries,
    int? IndirectBeneficiaries,
    decimal? InvestmentValue,
    string? Notes);

public sealed record ProliferationGranularSubmission(
    int ProjectId,
    ProliferationSource Source,
    int Year,
    ProliferationGranularity Granularity,
    int Period,
    string? PeriodLabel,
    int? DirectBeneficiaries,
    int? IndirectBeneficiaries,
    decimal? InvestmentValue,
    string? Notes);

public enum ProliferationRequestActionStatus
{
    Success = 0,
    NotFound = 1,
    ValidationFailed = 2,
    Conflict = 3
}

public sealed record ProliferationRequestActionResult(
    ProliferationRequestActionStatus Status,
    string? ErrorMessage = null,
    Guid? RequestId = null,
    byte[]? RowVersion = null,
    string? WarningMessage = null)
{
    public bool IsSuccess => Status == ProliferationRequestActionStatus.Success;

    public static ProliferationRequestActionResult Success(Guid requestId, byte[] rowVersion, string? warning)
        => new(ProliferationRequestActionStatus.Success, null, requestId, rowVersion, warning);

    public static ProliferationRequestActionResult NotFound()
        => new(ProliferationRequestActionStatus.NotFound);

    public static ProliferationRequestActionResult ValidationFailed(string message)
        => new(ProliferationRequestActionStatus.ValidationFailed, message);

    public static ProliferationRequestActionResult Conflict(string message)
        => new(ProliferationRequestActionStatus.Conflict, message);
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Domain;

namespace ProjectManagement.Areas.ProjectOfficeReports.Api;

[ApiController]
[Route("api/project-office-reports/misc-activities")]
[Authorize(Policy = ProjectOfficeReportsPolicies.ViewMiscActivities)]
public sealed class MiscActivitiesController : ControllerBase
{
    private readonly IMiscActivityService _activityService;
    private readonly IActivityTypeService _activityTypeService;
    private readonly ILogger<MiscActivitiesController> _logger;

    public MiscActivitiesController(
        IMiscActivityService activityService,
        IActivityTypeService activityTypeService,
        ILogger<MiscActivitiesController> logger)
    {
        _activityService = activityService ?? throw new ArgumentNullException(nameof(activityService));
        _activityTypeService = activityTypeService ?? throw new ArgumentNullException(nameof(activityTypeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet]
    public async Task<ActionResult<MiscActivityListResponseDto>> GetList([FromQuery] MiscActivityListQueryDto query, CancellationToken cancellationToken)
    {
        var options = BuildQueryOptions(query);
        var results = await _activityService.SearchAsync(options, cancellationToken);
        var payload = new MiscActivityListResponseDto(results.Select(MapListItem).ToList());
        return Ok(payload);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MiscActivityDetailDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var activity = await _activityService.FindAsync(id, cancellationToken);
        if (activity is null)
        {
            return NotFound();
        }

        return Ok(MapDetail(activity));
    }

    [HttpGet("activity-types")]
    public async Task<ActionResult<IReadOnlyList<ActivityTypeOptionDto>>> GetActivityTypes([FromQuery] bool includeInactive, CancellationToken cancellationToken)
    {
        var types = await _activityTypeService.GetAllAsync(includeInactive, cancellationToken);
        var payload = types
            .Select(type => new ActivityTypeOptionDto(type.Id, type.Name, type.IsActive))
            .ToList();

        return Ok(payload);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] MiscActivityListQueryDto query, CancellationToken cancellationToken)
    {
        var options = BuildQueryOptions(query);
        var rows = await _activityService.ExportAsync(options, cancellationToken);
        var csv = BuildCsv(rows);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var fileName = $"misc-activities-{timestamp}.csv";
        var buffer = Encoding.UTF8.GetBytes(csv);

        _logger.LogInformation(
            "User {UserId} exported {RowCount} miscellaneous activities.",
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            rows.Count);

        return File(buffer, "text/csv", fileName);
    }

    [HttpPost]
    [Authorize(Policy = ProjectOfficeReportsPolicies.ManageMiscActivities)]
    public async Task<IActionResult> Create([FromBody] MiscActivityCreateDto dto, CancellationToken cancellationToken)
    {
        if (dto is null)
        {
            return BadRequest();
        }

        var request = new MiscActivityCreateRequest(
            dto.ActivityTypeId,
            dto.OccurrenceDate,
            dto.Nomenclature ?? string.Empty,
            dto.Description,
            dto.ExternalLink);

        var result = await _activityService.CreateAsync(request, cancellationToken);
        if (result.Outcome == MiscActivityMutationOutcome.Success && result.Entity is not null)
        {
            var detail = await _activityService.FindAsync(result.Entity.Id, cancellationToken);
            if (detail is null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }

            return CreatedAtAction(nameof(Get), new { id = detail.Id }, MapDetail(detail));
        }

        return MapMutationFailure(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = ProjectOfficeReportsPolicies.ManageMiscActivities)]
    public async Task<IActionResult> Update(Guid id, [FromBody] MiscActivityUpdateDto dto, CancellationToken cancellationToken)
    {
        if (dto is null)
        {
            return BadRequest();
        }

        if (!TryDecodeRowVersion(dto.RowVersion, out var rowVersion))
        {
            return ValidationProblem(nameof(dto.RowVersion), "A valid row version is required.");
        }

        var request = new MiscActivityUpdateRequest(
            dto.ActivityTypeId,
            dto.OccurrenceDate,
            dto.Nomenclature ?? string.Empty,
            dto.Description,
            dto.ExternalLink,
            rowVersion);

        var result = await _activityService.UpdateAsync(id, request, cancellationToken);
        if (result.Outcome == MiscActivityMutationOutcome.Success && result.Entity is not null)
        {
            var detail = await _activityService.FindAsync(result.Entity.Id, cancellationToken);
            if (detail is null)
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }

            return Ok(MapDetail(detail));
        }

        return MapMutationFailure(result);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = ProjectOfficeReportsPolicies.DeleteMiscActivities)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery(Name = "rowVersion")] string? encodedRowVersion, CancellationToken cancellationToken)
    {
        if (!TryDecodeRowVersion(encodedRowVersion, out var rowVersion))
        {
            return ValidationProblem("rowVersion", "A valid row version is required.");
        }

        var result = await _activityService.DeleteAsync(id, rowVersion, cancellationToken);
        return MapDeletionResult(result);
    }

    [HttpPost("{id:guid}/media")]
    [Authorize(Policy = ProjectOfficeReportsPolicies.ManageMiscActivities)]
    public async Task<IActionResult> UploadMedia(Guid id, [FromForm] MiscActivityMediaUploadDto dto, CancellationToken cancellationToken)
    {
        if (dto.File is null)
        {
            return ValidationProblem(nameof(dto.File), "A file is required.");
        }

        if (!TryDecodeRowVersion(dto.RowVersion, out var activityRowVersion))
        {
            return ValidationProblem(nameof(dto.RowVersion), "A valid row version is required.");
        }

        await using var stream = dto.File.OpenReadStream();
        var request = new ActivityMediaUploadRequest(
            id,
            activityRowVersion,
            stream,
            dto.File.FileName,
            dto.File.ContentType,
            dto.Caption);

        var result = await _activityService.UploadMediaAsync(request, cancellationToken);
        return MapMediaUploadResult(result);
    }

    [HttpDelete("{id:guid}/media/{mediaId:guid}")]
    [Authorize(Policy = ProjectOfficeReportsPolicies.ManageMiscActivities)]
    public async Task<IActionResult> DeleteMedia(
        Guid id,
        Guid mediaId,
        [FromQuery(Name = "activityRowVersion")] string? encodedActivityRowVersion,
        [FromQuery(Name = "mediaRowVersion")] string? encodedMediaRowVersion,
        CancellationToken cancellationToken)
    {
        if (!TryDecodeRowVersion(encodedActivityRowVersion, out var activityRowVersion))
        {
            return ValidationProblem("activityRowVersion", "A valid activity row version is required.");
        }

        if (!TryDecodeRowVersion(encodedMediaRowVersion, out var mediaRowVersion))
        {
            return ValidationProblem("mediaRowVersion", "A valid media row version is required.");
        }

        var request = new ActivityMediaDeletionRequest(id, mediaId, activityRowVersion, mediaRowVersion);
        var result = await _activityService.DeleteMediaAsync(request, cancellationToken);
        return MapMediaDeletionResult(result);
    }

    private static MiscActivityQueryOptions BuildQueryOptions(MiscActivityListQueryDto query)
    {
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        return new MiscActivityQueryOptions(
            query.ActivityTypeId,
            query.StartDate,
            query.EndDate,
            search,
            query.IncludeDeleted,
            query.Sort,
            query.Desc);
    }

    private static MiscActivityListItemDto MapListItem(MiscActivityListItem item)
    {
        return new MiscActivityListItemDto(
            item.Id,
            item.ActivityTypeId,
            item.ActivityTypeName,
            item.Nomenclature,
            item.OccurrenceDate,
            item.Description,
            item.ExternalLink,
            item.MediaCount,
            item.IsDeleted,
            item.CapturedAtUtc,
            item.CapturedByUserId,
            item.LastModifiedAtUtc,
            item.LastModifiedByUserId,
            Convert.ToBase64String(item.RowVersion));
    }

    private static MiscActivityDetailDto MapDetail(MiscActivity activity)
    {
        var media = activity.Media
            .OrderBy(m => m.UploadedAtUtc)
            .ThenBy(m => m.Id)
            .Select(MapMedia)
            .ToList();

        return new MiscActivityDetailDto(
            activity.Id,
            activity.ActivityTypeId,
            activity.ActivityType?.Name,
            activity.Nomenclature,
            activity.OccurrenceDate,
            activity.Description,
            activity.ExternalLink,
            activity.DeletedUtc != null,
            activity.CapturedAtUtc,
            activity.CapturedByUserId,
            activity.LastModifiedAtUtc,
            activity.LastModifiedByUserId,
            activity.DeletedUtc,
            activity.DeletedByUserId,
            Convert.ToBase64String(activity.RowVersion),
            media);
    }

    private static MiscActivityMediaDto MapMedia(ActivityMedia media)
    {
        return new MiscActivityMediaDto(
            media.Id,
            media.OriginalFileName,
            media.MediaType,
            media.FileSize,
            media.Caption,
            media.Width,
            media.Height,
            media.UploadedAtUtc,
            media.UploadedByUserId,
            Convert.ToBase64String(media.RowVersion),
            media.StorageKey);
    }

    private IActionResult MapMutationFailure(MiscActivityMutationResult result)
    {
        return result.Outcome switch
        {
            MiscActivityMutationOutcome.Unauthorized => Forbid(),
            MiscActivityMutationOutcome.Invalid => ValidationProblem(result.Errors),
            MiscActivityMutationOutcome.ActivityTypeNotFound => ValidationProblem(nameof(MiscActivityCreateDto.ActivityTypeId), result.Errors.FirstOrDefault() ?? "The selected activity type could not be found."),
            MiscActivityMutationOutcome.ActivityTypeInactive => ValidationProblem(nameof(MiscActivityCreateDto.ActivityTypeId), result.Errors.FirstOrDefault() ?? "The selected activity type is inactive."),
            MiscActivityMutationOutcome.NotFound => NotFound(new { errors = result.Errors }),
            MiscActivityMutationOutcome.Deleted => Conflict(new { errors = result.Errors }),
            MiscActivityMutationOutcome.ConcurrencyConflict => Conflict(new { errors = result.Errors }),
            _ => BadRequest(new { errors = result.Errors })
        };
    }

    private IActionResult MapDeletionResult(MiscActivityDeletionResult result)
    {
        return result.Outcome switch
        {
            MiscActivityDeletionOutcome.Success => NoContent(),
            MiscActivityDeletionOutcome.Unauthorized => Forbid(),
            MiscActivityDeletionOutcome.NotFound => NotFound(new { errors = result.Errors }),
            MiscActivityDeletionOutcome.AlreadyDeleted => Conflict(new { errors = result.Errors }),
            MiscActivityDeletionOutcome.ConcurrencyConflict => Conflict(new { errors = result.Errors }),
            _ => BadRequest(new { errors = result.Errors })
        };
    }

    private IActionResult MapMediaUploadResult(ActivityMediaUploadResult result)
    {
        return result.Outcome switch
        {
            ActivityMediaUploadOutcome.Success when result.Media is not null && result.ActivityRowVersion is not null
                => Ok(new MiscActivityMediaUploadResponseDto(
                    MapMedia(result.Media),
                    Convert.ToBase64String(result.ActivityRowVersion))),
            ActivityMediaUploadOutcome.Unauthorized => Forbid(),
            ActivityMediaUploadOutcome.ActivityNotFound => NotFound(new { errors = result.Errors }),
            ActivityMediaUploadOutcome.ActivityDeleted => Conflict(new { errors = result.Errors }),
            ActivityMediaUploadOutcome.Invalid => ValidationProblem(result.Errors),
            ActivityMediaUploadOutcome.TooLarge => ValidationProblem(nameof(MiscActivityMediaUploadDto.File), result.Errors.FirstOrDefault() ?? "Uploaded file is too large."),
            ActivityMediaUploadOutcome.UnsupportedType => ValidationProblem(nameof(MiscActivityMediaUploadDto.File), result.Errors.FirstOrDefault() ?? "Unsupported file type."),
            ActivityMediaUploadOutcome.ConcurrencyConflict => Conflict(new { errors = result.Errors }),
            _ => BadRequest(new { errors = result.Errors })
        };
    }

    private IActionResult MapMediaDeletionResult(ActivityMediaDeletionResult result)
    {
        return result.Outcome switch
        {
            ActivityMediaDeletionOutcome.Success when result.ActivityRowVersion is not null
                => Ok(new { activityRowVersion = Convert.ToBase64String(result.ActivityRowVersion) }),
            ActivityMediaDeletionOutcome.Unauthorized => Forbid(),
            ActivityMediaDeletionOutcome.ActivityNotFound => NotFound(new { errors = result.Errors }),
            ActivityMediaDeletionOutcome.ActivityDeleted => Conflict(new { errors = result.Errors }),
            ActivityMediaDeletionOutcome.MediaNotFound => Conflict(new { errors = result.Errors }),
            ActivityMediaDeletionOutcome.ConcurrencyConflict => Conflict(new { errors = result.Errors }),
            _ => BadRequest(new { errors = result.Errors })
        };
    }

    private static bool TryDecodeRowVersion(string? value, out byte[] rowVersion)
    {
        rowVersion = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var buffer = Convert.FromBase64String(value);
            if (buffer.Length == 0)
            {
                return false;
            }

            rowVersion = buffer;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private ActionResult ValidationProblem(IReadOnlyList<string> errors)
    {
        var modelState = new ModelStateDictionary();
        foreach (var error in errors)
        {
            modelState.AddModelError(string.Empty, error);
        }

        return ValidationProblem(modelState);
    }

    private ActionResult ValidationProblem(string key, string message)
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError(key, message);
        return ValidationProblem(modelState);
    }

    private static string BuildCsv(IReadOnlyList<MiscActivityExportRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', new[]
        {
            "Occurrence Date",
            "Nomenclature",
            "Activity Type",
            "Description",
            "External Link",
            "Media Count"
        }));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(',', new[]
            {
                CsvEscape(row.OccurrenceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                CsvEscape(row.Nomenclature),
                CsvEscape(row.ActivityTypeName),
                CsvEscape(row.Description),
                CsvEscape(row.ExternalLink),
                CsvEscape(row.MediaCount.ToString(CultureInfo.InvariantCulture))
            }));
        }

        return builder.ToString();
    }

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ProjectManagement.Areas.ProjectOfficeReports.MiscActivities.ViewModels;

public sealed class MiscActivityMediaUploadViewModel : IValidatableObject
{
    [Display(Name = "File")]
    [Required(ErrorMessage = "Select a file to upload.")]
    public IFormFile? File { get; set; }

    [Display(Name = "Caption")]
    [StringLength(256, ErrorMessage = "Caption must be 256 characters or fewer.")]
    public string? Caption { get; set; }

    public string RowVersion { get; set; } = string.Empty;

    public long MaxFileSizeBytes { get; init; }

    public IReadOnlyList<string> AllowedContentTypes { get; init; } = Array.Empty<string>();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (File is null)
        {
            yield break;
        }

        if (MaxFileSizeBytes > 0 && File.Length > MaxFileSizeBytes)
        {
            yield return new ValidationResult(
                $"Files cannot exceed {MaxFileSizeBytes:N0} bytes.",
                new[] { nameof(File) });
        }

        if (AllowedContentTypes.Count > 0)
        {
            var isAllowed = false;
            foreach (var contentType in AllowedContentTypes)
            {
                if (string.Equals(contentType, File.ContentType, StringComparison.OrdinalIgnoreCase))
                {
                    isAllowed = true;
                    break;
                }
            }

            if (!isAllowed)
            {
                var allowedDisplay = string.Join(", ", AllowedContentTypes);
                yield return new ValidationResult(
                    $"Unsupported file type. Allowed types: {allowedDisplay}.",
                    new[] { nameof(File) });
            }
        }
    }
}

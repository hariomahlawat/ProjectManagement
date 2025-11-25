using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Options;

namespace ProjectManagement.Configuration;

// SECTION: Validation for project document OCR options
public sealed class ProjectDocumentOcrOptionsValidator : IValidateOptions<ProjectDocumentOcrOptions>
{
    // SECTION: Options validator entry point
    public ValidateOptionsResult Validate(string? name, ProjectDocumentOcrOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.WorkRoot))
        {
            failures.Add("ProjectDocuments:Ocr:WorkRoot must be configured.");
        }

        ValidateSubpath(options.InputSubpath, nameof(options.InputSubpath), failures);
        ValidateSubpath(options.OutputSubpath, nameof(options.OutputSubpath), failures);
        ValidateSubpath(options.LogsSubpath, nameof(options.LogsSubpath), failures);

        if (!string.IsNullOrWhiteSpace(options.OcrExecutablePath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(options.OcrExecutablePath);
            var fullPath = Path.GetFullPath(expanded);

            if (!File.Exists(fullPath))
            {
                failures.Add($"ProjectDocuments:Ocr:OcrExecutablePath points to '{fullPath}', which does not exist.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    // SECTION: Subpath guard rails
    private static void ValidateSubpath(string? value, string propertyName, ICollection<string> failures)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            failures.Add($"ProjectDocuments:Ocr:{propertyName} is required.");
            return;
        }

        if (Path.IsPathRooted(trimmed))
        {
            failures.Add($"ProjectDocuments:Ocr:{propertyName} must be a relative subpath.");
        }

        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        if (trimmed.Split(separators, StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            failures.Add($"ProjectDocuments:Ocr:{propertyName} cannot contain '.' or '..' segments.");
        }
    }
}

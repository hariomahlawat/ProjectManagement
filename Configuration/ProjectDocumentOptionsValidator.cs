using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Options;

namespace ProjectManagement.Configuration;

// SECTION: Validation for project document storage options
public sealed class ProjectDocumentOptionsValidator : IValidateOptions<ProjectDocumentOptions>
{
    // SECTION: Options validator entry point
    public ValidateOptionsResult Validate(string? name, ProjectDocumentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        ValidateSubpath(options.ProjectsSubpath, nameof(options.ProjectsSubpath), allowEmpty: false, failures);
        ValidateSubpath(options.StorageSubPath, nameof(options.StorageSubPath), allowEmpty: false, failures);
        ValidateSubpath(options.TempSubPath, nameof(options.TempSubPath), allowEmpty: false, failures);
        ValidateSubpath(options.CommentsSubpath, nameof(options.CommentsSubpath), allowEmpty: false, failures);
        ValidateSubpath(options.VideosSubpath, nameof(options.VideosSubpath), allowEmpty: false, failures);
        ValidateSubpath(options.PhotosSubpath, nameof(options.PhotosSubpath), allowEmpty: true, failures);

        if (options.MaxSizeMb < 0)
        {
            failures.Add("ProjectDocuments:MaxSizeMb cannot be negative.");
        }

        if (options.AllowedMimeTypes is null || options.AllowedMimeTypes.Count == 0)
        {
            failures.Add("ProjectDocuments:AllowedMimeTypes must include at least one content type (for example 'application/pdf' or 'application/vnd.openxmlformats-officedocument.wordprocessingml.document').");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    // SECTION: Subpath guard rails
    private static void ValidateSubpath(string? value, string propertyName, bool allowEmpty, ICollection<string> failures)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(trimmed))
        {
            if (!allowEmpty)
            {
                failures.Add($"ProjectDocuments:{propertyName} is required and cannot be empty.");
            }

            return;
        }

        if (Path.IsPathRooted(trimmed))
        {
            failures.Add($"ProjectDocuments:{propertyName} must be a relative subpath, not an absolute path.");
        }

        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        if (trimmed.Split(separators, StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            failures.Add($"ProjectDocuments:{propertyName} cannot contain '.' or '..' segments.");
        }
    }
}

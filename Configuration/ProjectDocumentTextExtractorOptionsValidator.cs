using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Options;

namespace ProjectManagement.Configuration;

// SECTION: Project document text extraction options validator
public sealed class ProjectDocumentTextExtractorOptionsValidator : IValidateOptions<ProjectDocumentTextExtractorOptions>
{
    // SECTION: Options validator entry point
    public ValidateOptionsResult Validate(string? name, ProjectDocumentTextExtractorOptions options)
    {
        if (options is null)
        {
            return ValidateOptionsResult.Fail("Options are required.");
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.DerivativeStoragePrefix))
        {
            failures.Add("ProjectDocuments:TextExtraction:DerivativeStoragePrefix is required.");
        }
        else
        {
            var trimmed = options.DerivativeStoragePrefix.Trim();
            if (trimmed.StartsWith("/", StringComparison.Ordinal) || trimmed.StartsWith("\\", StringComparison.Ordinal))
            {
                failures.Add("ProjectDocuments:TextExtraction:DerivativeStoragePrefix must be a relative path.");
            }

            var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/' };
            if (trimmed.Split(separators, StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
            {
                failures.Add("ProjectDocuments:TextExtraction:DerivativeStoragePrefix cannot contain traversal segments.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

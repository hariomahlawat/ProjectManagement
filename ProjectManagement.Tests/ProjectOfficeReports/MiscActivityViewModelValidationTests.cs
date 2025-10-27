using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Http;
using ProjectManagement.Areas.ProjectOfficeReports.MiscActivities.ViewModels;
using Xunit;

namespace ProjectManagement.Tests.ProjectOfficeReports;

public sealed class MiscActivityViewModelValidationTests
{
    [Fact]
    public void IndexFilter_StartDateAfterEndDate_ReturnsValidationError()
    {
        var model = new MiscActivityIndexFilterViewModel
        {
            StartDate = new DateOnly(2024, 2, 10),
            EndDate = new DateOnly(2024, 2, 1)
        };

        var results = Validate(model);

        Assert.Contains(results, r => r.ErrorMessage == "Start date must be on or before the end date.");
    }

    [Fact]
    public void Export_StartDateAfterEndDate_ReturnsValidationError()
    {
        var model = new MiscActivityExportViewModel
        {
            StartDate = new DateOnly(2024, 3, 1),
            EndDate = new DateOnly(2024, 2, 28)
        };

        var results = Validate(model);

        Assert.Contains(results, r => r.ErrorMessage == "Start date must be on or before the end date.");
    }

    [Fact]
    public void Form_MissingRequiredFields_ReturnsValidationErrors()
    {
        var model = new MiscActivityFormViewModel
        {
            Nomenclature = string.Empty,
            OccurrenceDate = null
        };

        var results = Validate(model);

        Assert.Contains(results, r => r.ErrorMessage == "Enter the activity nomenclature.");
        Assert.Contains(results, r => r.ErrorMessage == "Select the activity date.");
    }

    [Fact]
    public void MediaUpload_FileTooLarge_ReturnsValidationError()
    {
        var model = new MiscActivityMediaUploadViewModel
        {
            MaxFileSizeBytes = 10,
            AllowedContentTypes = new[] { "image/png" },
            File = CreateFormFile(new byte[32], "image/png")
        };

        var results = Validate(model);

        Assert.Contains(results, r => r.ErrorMessage != null && r.ErrorMessage.Contains("Files cannot exceed"));
    }

    [Fact]
    public void MediaUpload_UnsupportedType_ReturnsValidationError()
    {
        var model = new MiscActivityMediaUploadViewModel
        {
            MaxFileSizeBytes = 1024,
            AllowedContentTypes = new[] { "image/png" },
            File = CreateFormFile(new byte[16], "application/pdf")
        };

        var results = Validate(model);

        Assert.Contains(results, r => r.ErrorMessage == "Unsupported file type. Allowed types: image/png.");
    }

    [Fact]
    public void MediaUpload_ValidFile_PassesValidation()
    {
        var model = new MiscActivityMediaUploadViewModel
        {
            MaxFileSizeBytes = 1024,
            AllowedContentTypes = new[] { "image/png" },
            File = CreateFormFile(new byte[16], "image/png"),
            Caption = new string('a', 10)
        };

        var results = Validate(model);

        Assert.DoesNotContain(results, r => r.MemberNames.Contains(nameof(MiscActivityMediaUploadViewModel.File)));
    }

    private static IFormFile CreateFormFile(byte[] content, string contentType)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", "file.bin")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static IList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }
}

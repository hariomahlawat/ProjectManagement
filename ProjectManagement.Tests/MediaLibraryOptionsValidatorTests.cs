using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Options;
using Xunit;

namespace ProjectManagement.Tests;

public sealed class MediaLibraryOptionsValidatorTests
{
    private readonly MediaLibraryOptionsValidator _validator = new();

    [Fact]
    public void Validate_AllowsNoExternalSources()
    {
        var options = new MediaLibraryOptions
        {
            ExternalSources = new ExternalMediaSourcesOptions
            {
                Enabled = false,
                ScannerWorkerEnabled = false,
                Sources = new List<MediaSourceOptions>()
            }
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.False(result.Failed);
    }

    [Fact]
    public void Validate_IgnoresInvalidDisabledExternalDefinitions()
    {
        var options = new MediaLibraryOptions
        {
            ExternalSources = new ExternalMediaSourcesOptions
            {
                Enabled = false,
                Sources =
                {
                    new MediaSourceOptions
                    {
                        Key = string.Empty,
                        Name = string.Empty,
                        RootPath = "not-a-full-path",
                        Enabled = true
                    }
                }
            }
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.False(result.Failed);
    }

    [Fact]
    public void Validate_AllowsEnabledLocalFolder()
    {
        var options = CreateExternalEnabledOptions();
        options.ExternalSources.Sources.Add(new MediaSourceOptions
        {
            Key = "archive",
            Name = "Local archive",
            RootPath = Path.GetTempPath(),
            Enabled = true
        });

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.False(result.Failed);
    }

    [Fact]
    public void Validate_RejectsRelativeEnabledFolder()
    {
        var options = CreateExternalEnabledOptions();
        options.ExternalSources.Sources.Add(new MediaSourceOptions
        {
            Key = "archive",
            Name = "Invalid archive",
            RootPath = Path.Combine("relative", "photos"),
            Enabled = true
        });

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_RejectsDuplicateEnabledSourceKeys()
    {
        var options = CreateExternalEnabledOptions();
        options.ExternalSources.Sources.AddRange(new[]
        {
            new MediaSourceOptions
            {
                Key = "archive",
                Name = "One",
                RootPath = Path.GetTempPath(),
                Enabled = true
            },
            new MediaSourceOptions
            {
                Key = "ARCHIVE",
                Name = "Two",
                RootPath = Path.GetTempPath(),
                Enabled = true
            }
        });

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
    }



    [Fact]
    public void Validate_RejectsDuplicateEnabledSourcePaths()
    {
        var options = CreateExternalEnabledOptions();
        options.ExternalSources.Sources.AddRange(new[]
        {
            new MediaSourceOptions
            {
                Key = "archive-one",
                Name = "One",
                RootPath = Path.GetTempPath(),
                Enabled = true
            },
            new MediaSourceOptions
            {
                Key = "archive-two",
                Name = "Two",
                RootPath = Path.GetTempPath(),
                Enabled = true
            }
        });

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_AllowsEnabledLegacyFileSystemSourceDuringUpgrade()
    {
        var options = new MediaLibraryOptions
        {
            ScannerWorkerEnabled = true,
            Sources =
            {
                new MediaSourceOptions
                {
                    Key = "legacy-archive",
                    Name = "Legacy archive",
                    Type = "NetworkShare",
                    RootPath = Path.GetTempPath(),
                    Enabled = true
                }
            }
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.False(result.Failed);
        Assert.True(options.IsExternalSourceFeatureEnabled);
        Assert.True(options.IsScannerWorkerEnabled);
    }

    [Fact]
    public void Validate_RejectsPeopleWorkerWhenPeopleFeatureIsDisabled()
    {
        var options = new MediaLibraryOptions
        {
            People = new MediaPeopleOptions
            {
                Enabled = false,
                WorkerEnabled = true
            }
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
    }


    [Fact]
    public void Validate_RejectsPeopleFeatureUntilApprovedPackageIsDeployed()
    {
        var options = new MediaLibraryOptions
        {
            People = new MediaPeopleOptions
            {
                Enabled = true,
                WorkerEnabled = false
            }
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
    }

    private static MediaLibraryOptions CreateExternalEnabledOptions()
        => new()
        {
            ExternalSources = new ExternalMediaSourcesOptions
            {
                Enabled = true,
                ScannerWorkerEnabled = true
            }
        };
}

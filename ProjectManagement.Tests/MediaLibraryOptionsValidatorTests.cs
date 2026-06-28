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


    [Fact]
    public void Validate_AllowsPinnedYuNetAndSFaceProfile()
    {
        var options = CreateApprovedPeopleOptions();

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.False(result.Failed, string.Join(Environment.NewLine, result.Failures ?? Array.Empty<string>()));
        Assert.True(options.IsPeopleWorkerEnabled);
        Assert.True(options.IsAnyProcessingWorkerEnabled);
    }

    [Fact]
    public void Validate_RequiresCacheRootForPeopleFeature()
    {
        var options = CreateApprovedPeopleOptions();
        options.CacheRoot = string.Empty;

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("CacheRoot", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AppliesProcessingLimitsInFaceOnlyWorkerMode()
    {
        var options = CreateApprovedPeopleOptions();
        options.Processing.WorkerEnabled = false;
        options.Processing.MaxImageFileSizeBytes = 0;

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("MaxImageFileSizeBytes", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 0)]
    public void Validate_RejectsInvalidPeopleWorkerCadence(int batchSize, int idleDelaySeconds)
    {
        var options = CreateApprovedPeopleOptions();
        options.People.BatchSize = batchSize;
        options.People.IdleDelaySeconds = idleDelaySeconds;

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_RejectsNonHexadecimalModelChecksum()
    {
        var options = CreateApprovedPeopleOptions();
        options.People.Detector.Sha256 = new string('z', 64);

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("hexadecimal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsAutomaticIdentityConfirmation()
    {
        var options = CreateApprovedPeopleOptions();
        options.People.AutoConfirmEnabled = true;

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("AutoConfirmEnabled", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsUnsupportedFaceModelAdapter()
    {
        var options = CreateApprovedPeopleOptions();
        options.People.Detector.Adapter = "GuessTheTensorLayout";

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, failure => failure.Contains("unsupported", StringComparison.OrdinalIgnoreCase));
    }


    private static MediaLibraryOptions CreateApprovedPeopleOptions()
        => new()
        {
            People = new MediaPeopleOptions
            {
                Enabled = true,
                WorkerEnabled = true,
                Detector = new FaceModelOptions
                {
                    Key = "opencv-yunet",
                    Version = "2026may",
                    Adapter = "YuNet",
                    FileName = "face_detection_yunet_2026may.onnx",
                    Sha256 = new string('a', 64),
                    License = "MIT",
                    SourceUrl = "https://github.com/opencv/opencv_zoo",
                    InputName = "input",
                    ChannelOrder = "BGR"
                },
                Embedder = new FaceModelOptions
                {
                    Key = "opencv-sface",
                    Version = "2021dec",
                    Adapter = "SFace",
                    FileName = "face_recognition_sface_2021dec.onnx",
                    Sha256 = new string('b', 64),
                    License = "Apache-2.0",
                    SourceUrl = "https://github.com/opencv/opencv_zoo",
                    InputName = "data",
                    InputWidth = 112,
                    InputHeight = 112,
                    EmbeddingDimension = 128,
                    ChannelOrder = "RGB"
                }
            }
        };


    [Fact]
    public void Validate_RejectsInvalidFaceClassificationConfidenceThreshold()
    {
        var options = CreateApprovedPeopleOptions();
        options.People.MinimumClassificationConfidence = 1.1;

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

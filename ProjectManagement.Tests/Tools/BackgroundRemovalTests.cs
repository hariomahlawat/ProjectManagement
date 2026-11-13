using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.PhotoTools;
using SkiaSharp;
using Xunit;

namespace ProjectManagement.Tests.Tools;

/// <summary>
/// Remove background on the server. The model used here is red-channel-mask.onnx (made by
/// TestData/PhotoTools/make-channel-mask-models.py): it has a segmentation model's shape and
/// returns the photo's red channel as the mask, so every result can be predicted exactly.
/// </summary>
public sealed class BackgroundRemovalTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("prism-bg-tests").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    // ----- Mask arithmetic -----

    [Fact]
    public void ToMask_AppliesSigmoidAndStretch_AsTheSettingsSay()
    {
        Assert.Equal(new byte[] { 0, 128, 255 }, BackgroundMaskMath.ToMask(new[] { 0f, 0.5f, 1f }, "None", "None"));
        Assert.Equal(new byte[] { 128 }, BackgroundMaskMath.ToMask(new[] { 0f }, "Sigmoid", "None"));
        Assert.Equal(new byte[] { 0, 128, 255 }, BackgroundMaskMath.ToMask(new[] { 0.25f, 0.5f, 0.75f }, "None", "MinMax"));
        Assert.Equal(new byte[] { 0, 0 }, BackgroundMaskMath.ToMask(new[] { 0.3f, 0.3f }, "None", "MinMax"));
        Assert.Equal(new byte[] { 0, 255 }, BackgroundMaskMath.ToMask(new[] { -1f, 2f }, "None", "None"));
    }

    [Fact]
    public void ToMask_Auto_AppliesTheSigmoidOnlyToRawScores()
    {
        Assert.Equal(new byte[] { 0, 128, 255 }, BackgroundMaskMath.ToMask(new[] { 0f, 0.5f, 1f }, "Auto", "None"));
        Assert.Equal(new byte[] { 0, 128, 255 }, BackgroundMaskMath.ToMask(new[] { -20f, 0f, 20f }, "Auto", "None"));
    }

    [Fact]
    public async Task MaskAsync_RefusesAModelWhoseSizeDoesNotMatchItsSettings()
    {
        var options = EnabledOptions();
        options.Model.InputWidth = 64;
        options.Model.InputHeight = 64;
        var result = await ServiceFor(options).MaskAsync(Png(16, 16, (_, _) => SKColors.Red), CancellationToken.None);
        Assert.Equal(BackgroundRemovalOutcome.Failed, result.Outcome);
    }

    [Fact]
    public void Resize_KeepsAnEqualSizeAndBlendsBetweenPixels()
    {
        var mask = new byte[] { 0, 255 };
        Assert.Equal(mask, BackgroundMaskMath.Resize(mask, 2, 1, 2, 1));
        var larger = BackgroundMaskMath.Resize(mask, 2, 1, 4, 1);
        Assert.Equal(new byte[] { 0, 64, 191, 255 }, larger);
    }

    // ----- Readiness -----

    [Fact]
    public void Readiness_IsDisabledByDefault()
    {
        var readiness = ReadinessFor(new BackgroundRemovalOptions());
        Assert.Equal(BackgroundModelState.Disabled, readiness.Check().State);
    }

    [Fact]
    public void Readiness_NeedsTheFileAndItsChecksum()
    {
        var options = EnabledOptions(sha256: new string('0', 64));
        options.Model.FileName = "absent.onnx";
        Assert.Equal(BackgroundModelState.FileMissing, ReadinessFor(options).Check().State);

        var installed = EnabledOptions(sha256: string.Empty);
        Assert.Equal(BackgroundModelState.ChecksumNotConfigured, ReadinessFor(installed).Check().State);

        var wrong = EnabledOptions(sha256: new string('0', 64));
        Assert.Equal(BackgroundModelState.ChecksumMismatch, ReadinessFor(wrong).Check().State);

        var right = EnabledOptions();
        var readiness = ReadinessFor(right).Check();
        Assert.Equal(BackgroundModelState.Ready, readiness.State);
        Assert.True(readiness.IsReady);
    }

    [Fact]
    public void Readiness_WithoutAModelFolder_IsNotConfigured()
    {
        var options = EnabledOptions();
        options.ModelRoot = string.Empty;
        Assert.Equal(BackgroundModelState.NotConfigured, ReadinessFor(options).Check().State);
    }

    // ----- The model through ONNX Runtime -----

    [Fact]
    public async Task MaskAsync_RunsTheModelAndReturnsAMaskTheSizeOfThePhoto()
    {
        var service = ServiceFor(EnabledOptions());
        // 64 x 48: a red block on the left half, black on the right.
        var photo = Png(64, 48, (x, _) => x < 32 ? new SKColor(255, 0, 0) : new SKColor(0, 0, 0));

        var result = await service.MaskAsync(photo, CancellationToken.None);

        Assert.Equal(BackgroundRemovalOutcome.Done, result.Outcome);
        Assert.Equal((64, 48), (result.Width, result.Height));
        using var mask = SKBitmap.Decode(result.MaskPng);
        Assert.Equal((64, 48), (mask.Width, mask.Height));
        Assert.Equal(255, mask.GetPixel(5, 24).Red);
        Assert.Equal(0, mask.GetPixel(58, 24).Red);
    }

    [Fact]
    public async Task MaskAsync_UsesAReplacedModel_WithoutARestart()
    {
        var options = EnabledOptions();
        var service = ServiceFor(options);
        var photo = Png(32, 32, (_, _) => new SKColor(255, 0, 0));
        using (var mask = SKBitmap.Decode((await service.MaskAsync(photo, CancellationToken.None)).MaskPng))
        {
            Assert.Equal(255, mask.GetPixel(16, 16).Red);
        }

        // The administrator puts another model at the same path and approves its checksum.
        var target = Path.Combine(_folder, "red-channel-mask.onnx");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "PhotoTools", "green-channel-mask.onnx"), target, overwrite: true);
        File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddMinutes(1));
        options.Model.Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))).ToLowerInvariant();

        // The green-channel model gives nothing for a red photo: the replacement is in use.
        using var replaced = SKBitmap.Decode((await service.MaskAsync(photo, CancellationToken.None)).MaskPng);
        Assert.Equal(0, replaced.GetPixel(16, 16).Red);
    }

    [Fact]
    public async Task MaskAsync_LaysASeeThroughPhotoOnWhite()
    {
        var service = ServiceFor(EnabledOptions());
        // Fully see-through everywhere: the model sees white, whose red channel is full.
        var photo = Png(32, 32, (_, _) => new SKColor(0, 0, 0, 0));
        var result = await service.MaskAsync(photo, CancellationToken.None);
        using var mask = SKBitmap.Decode(result.MaskPng);
        Assert.Equal(255, mask.GetPixel(16, 16).Red);
    }

    [Fact]
    public async Task MaskAsync_RefusesWhatIsNotAPhoto_OrTooLarge_OrNotSetUp()
    {
        var service = ServiceFor(EnabledOptions());
        Assert.Equal(BackgroundRemovalOutcome.NotAPhoto, (await service.MaskAsync(new byte[] { 1, 2, 3 }, CancellationToken.None)).Outcome);
        Assert.Equal(BackgroundRemovalOutcome.NotAPhoto, (await service.MaskAsync(Array.Empty<byte>(), CancellationToken.None)).Outcome);

        var small = EnabledOptions();
        small.MaximumSide = 40;
        Assert.Equal(
            BackgroundRemovalOutcome.TooLarge,
            (await ServiceFor(small).MaskAsync(Png(64, 32, (_, _) => SKColors.Red), CancellationToken.None)).Outcome);

        var tiny = EnabledOptions();
        tiny.MaximumUploadBytes = 10;
        Assert.Equal(
            BackgroundRemovalOutcome.TooLarge,
            (await ServiceFor(tiny).MaskAsync(Png(8, 8, (_, _) => SKColors.Red), CancellationToken.None)).Outcome);

        var off = ServiceFor(new BackgroundRemovalOptions());
        var refused = await off.MaskAsync(Png(8, 8, (_, _) => SKColors.Red), CancellationToken.None);
        Assert.Equal(BackgroundRemovalOutcome.Unavailable, refused.Outcome);
        Assert.Equal(BackgroundModelState.Disabled, refused.ModelState);
    }

    [Fact]
    public async Task MaskAsync_AnswersBusy_WhenTooManyPhotosWait()
    {
        var options = EnabledOptions();
        options.MaximumConcurrent = 1;
        options.MaximumWaiting = 1;
        var segmenter = new HeldSegmenter();
        var service = new BackgroundRemovalService(
            Monitor(options), ReadinessFor(options), segmenter, NullLogger<BackgroundRemovalService>.Instance);
        var photo = Png(8, 8, (_, _) => SKColors.Red);

        var first = service.MaskAsync(photo, CancellationToken.None);
        await segmenter.Started.Task;
        var second = service.MaskAsync(photo, CancellationToken.None);
        var third = await service.MaskAsync(photo, CancellationToken.None);
        Assert.Equal(BackgroundRemovalOutcome.Busy, third.Outcome);

        segmenter.Release.SetResult();
        Assert.Equal(BackgroundRemovalOutcome.Done, (await first).Outcome);
        Assert.Equal(BackgroundRemovalOutcome.Done, (await second).Outcome);
        Assert.Equal(BackgroundRemovalOutcome.Done, (await service.MaskAsync(photo, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task MaskAsync_ACancelledPhotoGivesUpItsPlaceInTheQueue()
    {
        var options = EnabledOptions();
        options.MaximumConcurrent = 1;
        options.MaximumWaiting = 1;
        var segmenter = new HeldSegmenter();
        var service = new BackgroundRemovalService(
            Monitor(options), ReadinessFor(options), segmenter, NullLogger<BackgroundRemovalService>.Instance);
        var photo = Png(8, 8, (_, _) => SKColors.Red);

        var first = service.MaskAsync(photo, CancellationToken.None);
        await segmenter.Started.Task;
        using var replaced = new CancellationTokenSource();
        var cancelled = service.MaskAsync(photo, replaced.Token);
        replaced.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        // Its place is free again: the next photo waits instead of being told the server is busy.
        var next = service.MaskAsync(photo, CancellationToken.None);
        segmenter.Release.SetResult();
        Assert.Equal(BackgroundRemovalOutcome.Done, (await first).Outcome);
        Assert.Equal(BackgroundRemovalOutcome.Done, (await next).Outcome);
    }

    [Fact]
    public async Task MaskAsync_ChecksTheApprovalAgainAfterWaitingItsTurn()
    {
        var options = EnabledOptions();
        options.MaximumConcurrent = 1;
        var segmenter = new HeldSegmenter();
        var service = new BackgroundRemovalService(
            Monitor(options), ReadinessFor(options), segmenter, NullLogger<BackgroundRemovalService>.Instance);
        var photo = Png(8, 8, (_, _) => SKColors.Red);

        var first = service.MaskAsync(photo, CancellationToken.None);
        await segmenter.Started.Task;
        var queued = service.MaskAsync(photo, CancellationToken.None);

        // While the second photo waits, the model is replaced by a file that is not approved.
        var target = Path.Combine(_folder, "red-channel-mask.onnx");
        File.WriteAllBytes(target, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "PhotoTools", "green-channel-mask.onnx")).Concat(new byte[] { 0 }).ToArray());
        File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddMinutes(1));

        segmenter.Release.SetResult();
        Assert.Equal(BackgroundRemovalOutcome.Done, (await first).Outcome);
        var refused = await queued;
        Assert.Equal(BackgroundRemovalOutcome.Unavailable, refused.Outcome);
        Assert.Equal(BackgroundModelState.ChecksumMismatch, refused.ModelState);
    }

    [Fact]
    public void Segment_RefusesAFileChangedSinceItWasApproved()
    {
        var options = EnabledOptions();
        var approved = ReadinessFor(options).Check();
        Assert.True(approved.IsReady);
        File.SetLastWriteTimeUtc(approved.ModelPath!, DateTime.UtcNow.AddMinutes(5));

        using var segmenter = new OnnxBackgroundSegmenter(Monitor(options), NullLogger<OnnxBackgroundSegmenter>.Instance);
        using var photo = new SKBitmap(8, 8);
        Assert.Throws<BackgroundModelChangedException>(() => segmenter.Segment(photo, approved, CancellationToken.None));
    }

    [Fact]
    public async Task MaskAsync_StopsAPhotoThatTakesTooLong()
    {
        var options = EnabledOptions();
        options.TimeoutSeconds = 5;
        var segmenter = new HeldSegmenter { HonourCancellation = true };
        var service = new BackgroundRemovalService(
            Monitor(options), ReadinessFor(options), segmenter, NullLogger<BackgroundRemovalService>.Instance);

        var result = await service.MaskAsync(Png(8, 8, (_, _) => SKColors.Red), CancellationToken.None);
        Assert.Equal(BackgroundRemovalOutcome.TimedOut, result.Outcome);
    }

    // ----- Endpoints -----

    [Fact]
    public void Endpoints_RequireASignedInUser()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAntiforgery();
        builder.Services.AddAuthorization();
        builder.Services.AddPhotoTools(builder.Configuration);
        var app = builder.Build();
        app.MapPhotoToolsApi();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText is PhotoToolsApi.StatusPath or PhotoToolsApi.MaskPath)
            .ToArray();

        Assert.Equal(2, endpoints.Length);
        foreach (var endpoint in endpoints)
        {
            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
            Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        }
    }

    // ----- Helpers -----

    private BackgroundRemovalOptions EnabledOptions(string? sha256 = null)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "TestData", "PhotoTools", "red-channel-mask.onnx");
        var target = Path.Combine(_folder, "red-channel-mask.onnx");
        if (!File.Exists(target))
        {
            File.Copy(source, target);
        }

        return new BackgroundRemovalOptions
        {
            Enabled = true,
            ModelRoot = _folder,
            TimeoutSeconds = 30,
            Model = new BackgroundModelOptions
            {
                Key = "test-red-channel",
                Version = "1",
                FileName = "red-channel-mask.onnx",
                Sha256 = sha256 ?? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))).ToLowerInvariant(),
                InputWidth = 32,
                InputHeight = 32,
                Mean = new[] { 0f, 0f, 0f },
                Std = new[] { 1f, 1f, 1f }
            }
        };
    }

    private static IOptionsMonitor<BackgroundRemovalOptions> Monitor(BackgroundRemovalOptions options)
        => new FixedMonitor(options);

    private static BackgroundModelReadinessService ReadinessFor(BackgroundRemovalOptions options)
        => new(Monitor(options), new TestEnvironment());

    private static BackgroundRemovalService ServiceFor(BackgroundRemovalOptions options)
    {
        var monitor = Monitor(options);
        return new BackgroundRemovalService(
            monitor,
            new BackgroundModelReadinessService(monitor, new TestEnvironment()),
            new OnnxBackgroundSegmenter(monitor, NullLogger<OnnxBackgroundSegmenter>.Instance),
            NullLogger<BackgroundRemovalService>.Instance);
    }

    private static byte[] Png(int width, int height, Func<int, int, SKColor> colour)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var y = 0; y < height; y += 1)
        {
            for (var x = 0; x < width; x += 1)
            {
                bitmap.SetPixel(x, y, colour(x, y));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class HeldSegmenter : IBackgroundSegmenter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HonourCancellation { get; init; }

        public BackgroundMask Segment(SKBitmap photo, BackgroundModelReadiness model, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (HonourCancellation)
            {
                cancellationToken.WaitHandle.WaitOne();
            }
            else
            {
                Release.Task.Wait(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new BackgroundMask(photo.Width, photo.Height, new byte[photo.Width * photo.Height]);
        }
    }

    private sealed class FixedMonitor(BackgroundRemovalOptions value) : IOptionsMonitor<BackgroundRemovalOptions>
    {
        public BackgroundRemovalOptions CurrentValue => value;
        public BackgroundRemovalOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<BackgroundRemovalOptions, string?> listener) => null;
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "ProjectManagement.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = Environments.Development;
    }
}

using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ProjectManagement.Data;
using ProjectManagement.Features.Collage;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Imaging;
using ProjectManagement.Services.Storage;
using ProjectManagement.Tests.Fakes;
using SkiaSharp;
using Xunit;

namespace ProjectManagement.Tests.Tools;

/// <summary>
/// Collage Studio "Crests and logos": uploads are checked and stored as PRISM's own PNG, the
/// picker order and retiring work, and the read API serves only what it should.
/// </summary>
public sealed class CollageStickerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "collage-stickers-" + Guid.NewGuid().ToString("N"));
    private readonly ApplicationDbContext _db;
    private readonly RecordingAudit _audit = new();
    private readonly CollageStickerService _service;

    public CollageStickerTests()
    {
        Directory.CreateDirectory(_root);
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var roots = new TestUploadRootProvider(_root);
        _service = new CollageStickerService(
            _db,
            roots,
            new UploadPathResolver(roots),
            _audit,
            FakeClock.AtUtc(new DateTimeOffset(2026, 11, 15, 6, 0, 0, TimeSpan.Zero)));
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task An_upload_is_stored_as_a_fresh_png_no_larger_than_1024_with_its_clear_background()
    {
        var outcome = await AddAsync("  Signals   crest ", Png(2000, 1000, transparent: true), "Signals Corps badge");

        Assert.True(outcome.Succeeded, outcome.Message);
        var sticker = Assert.Single(await _service.ListActiveAsync());
        Assert.Equal("Signals crest", sticker.Name);
        Assert.Equal("Signals Corps badge", sticker.SearchWords);
        Assert.Equal((1024, 512), (sticker.Width, sticker.Height));
        Assert.StartsWith(CollageStickerService.Folder + "/", sticker.StorageKey);

        var path = _service.PicturePathOf(sticker);
        Assert.NotNull(path);
        var stored = await File.ReadAllBytesAsync(path!);
        var info = PrismImageCodec.Identify(stored);
        Assert.Equal("image/png", info!.MimeType);
        using var decoded = PrismImageCodec.DecodeOriented(stored);
        Assert.True(PrismImageCodec.HasTransparency(decoded));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, CollageStickerService.Folder), "*.tmp"));
        Assert.Equal("CollageStickerAdded", Assert.Single(_audit.Entries).Action);
    }

    [Fact]
    public async Task Files_that_are_not_png_jpeg_or_webp_or_are_too_large_are_refused_and_nothing_is_kept()
    {
        var notAPicture = await AddAsync("Crest", Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"));
        var tooLarge = await AddAsync("Crest", new byte[CollageStickerService.MaxFileBytes + 1]);
        var empty = await AddAsync("Crest", Array.Empty<byte>());
        var noName = await AddAsync("   ", Png(10, 10));
        var longName = await AddAsync(new string('x', CollageSticker.NameMaxLength + 1), Png(10, 10));

        Assert.All(new[] { notAPicture, tooLarge, empty, noName, longName }, o => Assert.False(o.Succeeded));
        Assert.Contains("PNG, JPEG or WebP", notAPicture.Message);
        Assert.Contains("5 MB", tooLarge.Message);
        Assert.Empty(await _service.ListAllAsync());
        Assert.False(Directory.Exists(Path.Combine(_root, CollageStickerService.Folder))
            && Directory.EnumerateFiles(Path.Combine(_root, CollageStickerService.Folder)).Any());
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public async Task No_more_than_sixty_are_in_use_and_a_retired_one_comes_back_only_when_there_is_room()
    {
        for (var i = 0; i < CollageStickerService.MaxActive; i++)
        {
            _db.CollageStickers.Add(new CollageSticker { Name = $"S{i}", StorageKey = $"{CollageStickerService.Folder}/s{i}.png", SortOrder = i + 1 });
        }
        var retired = new CollageSticker { Name = "Old", StorageKey = $"{CollageStickerService.Folder}/old.png", IsRetired = true };
        _db.CollageStickers.Add(retired);
        await _db.SaveChangesAsync();

        var add = await AddAsync("One more", Png(10, 10));
        var restore = await _service.RestoreAsync(retired.Id, "admin");
        Assert.False(add.Succeeded);
        Assert.False(restore.Succeeded);

        var first = (await _service.ListActiveAsync())[0];
        Assert.True((await _service.RetireAsync(first.Id, "admin")).Succeeded);
        Assert.True((await _service.RestoreAsync(retired.Id, "admin")).Succeeded);
        var active = await _service.ListActiveAsync();
        Assert.Equal(CollageStickerService.MaxActive, active.Count);
        Assert.Equal("Old", active[^1].Name);
        Assert.DoesNotContain(active, s => s.Id == first.Id);
    }

    [Fact]
    public async Task Moving_swaps_neighbours_and_renumbers_the_picker()
    {
        var a = await SeedAsync("A", 5);
        var b = await SeedAsync("B", 5);
        var c = await SeedAsync("C", 9);

        await _service.MoveAsync(c.Id, up: true, "admin");
        Assert.Equal(new[] { "A", "C", "B" }, (await _service.ListActiveAsync()).Select(s => s.Name));
        Assert.Equal(new[] { 1, 2, 3 }, (await _service.ListActiveAsync()).Select(s => s.SortOrder));

        var atTop = await _service.MoveAsync(a.Id, up: true, "admin");
        Assert.True(atTop.Succeeded);
        Assert.Equal("A", (await _service.ListActiveAsync())[0].Name);

        await _service.RenameAsync(b.Id, " Bravo  badge ", "  b  ", "admin");
        var renamed = await _service.FindAsync(b.Id);
        Assert.Equal(("Bravo badge", "b"), (renamed!.Name, renamed.SearchWords));
    }

    [Fact]
    public async Task The_list_shows_active_stickers_in_order_with_versioned_picture_addresses()
    {
        await SeedAsync("Second", 2);
        await SeedAsync("First", 1);
        await SeedAsync("Gone", 3, retired: true);

        var context = new DefaultHttpContext();
        var result = await CollageApi.StickersAsync(context, _service, CancellationToken.None);

        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        var json = JsonSerializer.Serialize(Assert.IsAssignableFrom<IValueHttpResult>(result).Value);
        using var doc = JsonDocument.Parse(json);
        var names = doc.RootElement.GetProperty("stickers").EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToArray();
        Assert.Equal(new[] { "First", "Second" }, names);
        Assert.Matches(@"^/api/collage/stickers/\d+/image\?v=1$", doc.RootElement.GetProperty("stickers")[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task A_retired_sticker_is_still_served_a_missing_one_is_not_and_an_unchanged_one_is_not_sent_again()
    {
        var outcome = await AddAsync("Crest", Png(20, 20));
        var sticker = outcome.Sticker!;
        await _service.RetireAsync(sticker.Id, "admin");

        var context = new DefaultHttpContext();
        var served = await CollageApi.PictureAsync(sticker.Id, context, _service, CancellationToken.None);
        var file = Assert.IsType<PhysicalFileHttpResult>(served);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal("private, max-age=604800", context.Response.Headers.CacheControl.ToString());
        var etag = context.Response.Headers.ETag.ToString();

        var again = new DefaultHttpContext();
        again.Request.Headers.IfNoneMatch = etag;
        var notModified = await CollageApi.PictureAsync(sticker.Id, again, _service, CancellationToken.None);
        Assert.Equal(StatusCodes.Status304NotModified, Assert.IsType<StatusCodeHttpResult>(notModified).StatusCode);

        File.Delete(_service.PicturePathOf(sticker)!);
        Assert.IsType<NotFound>(await CollageApi.PictureAsync(sticker.Id, new DefaultHttpContext(), _service, CancellationToken.None));
        Assert.IsType<NotFound>(await CollageApi.PictureAsync(9999, new DefaultHttpContext(), _service, CancellationToken.None));
    }

    [Fact]
    public void Every_sticker_action_has_a_plain_title_in_the_audit_log()
    {
        foreach (var action in new[] { "CollageStickerAdded", "CollageStickerUpdated", "CollageStickerRetired", "CollageStickerRestored" })
        {
            var presentation = new AuditActionPresentationCatalog().Describe(action);
            Assert.StartsWith("Collage sticker", presentation.Label);
        }
    }

    private Task<CollageStickerOutcome> AddAsync(string name, byte[] bytes, string? words = null) =>
        _service.AddAsync(new MemoryStream(bytes), "crest.png", name, words, "admin");

    private async Task<CollageSticker> SeedAsync(string name, int order, bool retired = false)
    {
        var sticker = new CollageSticker { Name = name, StorageKey = $"{CollageStickerService.Folder}/{name}.png", SortOrder = order, IsRetired = retired };
        _db.CollageStickers.Add(sticker);
        await _db.SaveChangesAsync();
        return sticker;
    }

    private static byte[] Png(int width, int height, bool transparent = false)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(transparent ? SKColors.Transparent : SKColors.White);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, new SKPaint { Color = SKColors.DarkRed });
        }
        return PrismImageCodec.EncodePng(bitmap);
    }

    private sealed class RecordingAudit : IAdminAuditService
    {
        public List<AdminAuditEntry> Entries { get; } = new();

        public Task RecordAsync(AdminAuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class TestUploadRootProvider(string root) : IUploadRootProvider
    {
        public string RootPath { get; } = root;
        public string GetProjectRoot(int projectId) => RootPath;
        public string GetProjectPhotosRoot(int projectId) => RootPath;
        public string GetProjectDocumentsRoot(int projectId) => RootPath;
        public string GetProjectCommentsRoot(int projectId) => RootPath;
        public string GetProjectVideosRoot(int projectId) => RootPath;
        public string GetSocialMediaRoot(string storagePrefix, Guid eventId) => RootPath;
    }
}

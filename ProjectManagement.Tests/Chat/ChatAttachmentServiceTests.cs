using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectManagement.Configuration;
using ProjectManagement.Contracts.Chat;
using ProjectManagement.Data;
using ProjectManagement.Models;
using ProjectManagement.Services;
using ProjectManagement.Services.Chat;
using ProjectManagement.Services.Storage;
using SkiaSharp;
using Xunit;

namespace ProjectManagement.Tests.Chat;

public sealed class ChatAttachmentServiceTests
{
    [Fact]
    public async Task Upload_StoresUnderARandomName_AndMakesAThumbnailForImages()
    {
        await using var scope = await FileScope.CreateAsync();

        var result = await scope.Files.UploadAsync("alice", Png(64, 40), "holiday photo.png");

        Assert.Equal(ChatAttachmentUploadStatus.Uploaded, result.Status);
        var dto = result.Attachment!;
        Assert.Equal("holiday photo.png", dto.FileName);
        Assert.Equal("image/png", dto.ContentType);
        Assert.True(dto.IsImage);
        Assert.Equal((64, 40), (dto.Width, dto.Height));

        var stored = await scope.Db.ChatAttachments.SingleAsync();
        Assert.StartsWith("chat/", stored.StorageKey);
        Assert.DoesNotContain("holiday", stored.StorageKey);
        Assert.EndsWith(".png", stored.StorageKey);
        Assert.Equal(64, stored.Sha256.Length);
        Assert.True(File.Exists(scope.PathOf(stored.StorageKey)));
        Assert.True(File.Exists(scope.PathOf(stored.ThumbnailKey!)));
        Assert.Empty(Directory.EnumerateFiles(scope.PathOf("chat/tmp")));
    }

    [Theory]
    [InlineData("report.pdf", "pdf")]
    [InlineData("minutes.docx", "docx")]
    [InlineData("figures.xlsx", "xlsx")]
    [InlineData("notes.txt", "text")]
    [InlineData("data.csv", "text")]
    public async Task Upload_AcceptsEachAllowedKind(string name, string kind)
    {
        await using var scope = await FileScope.CreateAsync();

        var result = await scope.Files.UploadAsync("alice", Content(kind), name);

        Assert.Equal(ChatAttachmentUploadStatus.Uploaded, result.Status);
        Assert.False(result.Attachment!.IsImage);
        Assert.Null((await scope.Db.ChatAttachments.SingleAsync()).ThumbnailKey);
    }

    [Theory]
    [InlineData("setup.exe", "pdf", "Only PDF")]
    [InlineData("archive.zip", "docx", "Only PDF")]
    [InlineData("old.doc", "pdf", "Only PDF")]
    [InlineData("macros.docm", "docx", "Only PDF")]
    [InlineData("renamed.pdf", "exe", "not a PDF")]
    [InlineData("renamed.docx", "pdf", "not a valid Office")]
    [InlineData("hidden-macros.docx", "docx-macros", "macros")]
    [InlineData("wrong.docx", "xlsx", "not a valid Office")]
    [InlineData("fake.png", "jpeg", "does not match")]
    [InlineData("fake.jpg", "pdf", "not an image")]
    [InlineData("binary.txt", "nul", "not a text file")]
    [InlineData("latin1.csv", "latin1", "UTF-8")]
    [InlineData("empty.txt", "empty", "empty")]
    public async Task Upload_RefusesByNameAndByContent(string name, string kind, string expected)
    {
        await using var scope = await FileScope.CreateAsync();

        var result = await scope.Files.UploadAsync("alice", Content(kind), name);

        Assert.Equal(ChatAttachmentUploadStatus.Invalid, result.Status);
        Assert.Contains(expected, result.Error!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(scope.Db.ChatAttachments);
        Assert.False(Directory.Exists(scope.PathOf("chat/tmp")) && Directory.EnumerateFiles(scope.PathOf("chat/tmp")).Any());
    }

    [Fact]
    public async Task Upload_TooLarge_IsRefusedWhileStreaming()
    {
        await using var scope = await FileScope.CreateAsync(maxFileMb: 1);

        var result = await scope.Files.UploadAsync("alice", Text(new string('a', 1024 * 1024 + 1)), "big.txt");

        Assert.Equal(ChatAttachmentUploadStatus.TooLarge, result.Status);
        Assert.Contains("1 MB", result.Error!);
        Assert.Empty(Directory.EnumerateFiles(scope.PathOf("chat/tmp")));
    }

    [Fact]
    public async Task Allowance_CountsKeptFiles_AndRemovingOneMakesRoom()
    {
        await using var scope = await FileScope.CreateAsync(allowanceMb: 1);
        var sevenHundredKb = new string('a', 700 * 1024);

        var first = await scope.Files.UploadAsync("alice", Text(sevenHundredKb), "one.txt");
        var second = await scope.Files.UploadAsync("alice", Text(sevenHundredKb), "two.txt");

        Assert.Equal(ChatAttachmentUploadStatus.Uploaded, first.Status);
        Assert.Equal(ChatAttachmentUploadStatus.AllowanceFull, second.Status);
        Assert.Contains("allowance", second.Error!);
        Assert.Single(Directory.EnumerateFiles(scope.PathOf("chat"), "*.txt", SearchOption.AllDirectories));

        // Bob has his own allowance.
        Assert.Equal(ChatAttachmentUploadStatus.Uploaded, (await scope.Files.UploadAsync("bob", Text(sevenHundredKb), "b.txt")).Status);

        Assert.NotNull(await scope.Files.RemoveAsync("alice", first.Attachment!.Id));
        Assert.Equal(ChatAttachmentUploadStatus.Uploaded, (await scope.Files.UploadAsync("alice", Text(sevenHundredKb), "three.txt")).Status);
        Assert.Equal(700 * 1024, (await scope.Files.GetUsageAsync("alice")).UsedBytes);
    }

    [Fact]
    public async Task Upload_IsRefusedWhileTheDiskIsBelowTheFloor()
    {
        await using var scope = await FileScope.CreateAsync();
        scope.Disk.Free = 19L * 1024 * 1024 * 1024;

        var result = await scope.Files.UploadAsync("alice", Content("pdf"), "report.pdf");

        Assert.Equal(ChatAttachmentUploadStatus.StorageFull, result.Status);
        Assert.Empty(scope.Db.ChatAttachments);
    }

    [Fact]
    public async Task Sending_CarriesTheFiles_AndAMessageMayBeOnlyFiles()
    {
        await using var scope = await FileScope.CreateAsync();
        var pdf = (await scope.Files.UploadAsync("alice", Content("pdf"), "report.pdf")).Attachment!;
        var png = (await scope.Files.UploadAsync("alice", Png(10, 10), "chart.png")).Attachment!;

        var sent = await scope.Chat.SendAsync("alice", new ChatSendRequest("bob", Guid.NewGuid(), "  ", new[] { pdf.Id, png.Id }));

        Assert.Equal(ChatSendStatus.Sent, sent.Status);
        Assert.Equal(new[] { "report.pdf", "chart.png" }, sent.Message!.Attachments!.Select(a => a.FileName));
        var history = await scope.Chat.GetHistoryAsync("bob", "alice", null, 40);
        Assert.Equal(2, history.Messages.Single().Attachments!.Count);

        // A file goes with one message only, and only its sender can send it.
        var again = await scope.Chat.SendAsync("alice", new ChatSendRequest("bob", Guid.NewGuid(), "again", new[] { pdf.Id }));
        Assert.Equal(ChatSendStatus.Invalid, again.Status);
        var bobs = (await scope.Files.UploadAsync("bob", Content("pdf"), "bob.pdf")).Attachment!;
        Assert.Equal(ChatSendStatus.Invalid, (await scope.Chat.SendAsync("alice", new ChatSendRequest("bob", Guid.NewGuid(), "x", new[] { bobs.Id }))).Status);
        Assert.Null((await scope.Db.ChatAttachments.SingleAsync(a => a.Id == bobs.Id)).MessageId);
    }

    [Fact]
    public async Task Sending_RefusesTooManyFiles_AndFilesWhileSwitchedOff()
    {
        await using var scope = await FileScope.CreateAsync();
        var ids = new List<long>();
        for (var i = 0; i < 6; i++)
        {
            ids.Add((await scope.Files.UploadAsync("alice", Content("pdf"), $"f{i}.pdf")).Attachment!.Id);
        }

        var tooMany = await scope.Chat.SendAsync("alice", new ChatSendRequest("bob", Guid.NewGuid(), "x", ids));
        Assert.Contains("at most 5", tooMany.Error!);

        scope.Options.CurrentValue.Attachments.Enabled = false;
        var off = await scope.Chat.SendAsync("alice", new ChatSendRequest("bob", Guid.NewGuid(), "x", ids.Take(1).ToList()));
        Assert.Equal(ChatSendStatus.Invalid, off.Status);
        // Text alone still works with attachments off.
        Assert.Equal(ChatSendStatus.Sent, (await scope.Chat.SendAsync("alice", new ChatSendRequest("bob", Guid.NewGuid(), "plain"))).Status);
    }

    [Fact]
    public async Task Download_FollowsWhoMayReadTheMessage()
    {
        await using var scope = await FileScope.CreateAsync();
        var upload = (await scope.Files.UploadAsync("alice", Png(20, 20), "plan.png")).Attachment!;

        // Before sending: only its sender.
        Assert.NotNull(await scope.Files.OpenAsync("alice", upload.Id, thumbnail: false));
        Assert.Null(await scope.Files.OpenAsync("bob", upload.Id, thumbnail: false));

        var groupId = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob" })).Group!.Id;
        await scope.Groups.SendAsync("alice", groupId, new ChatGroupSendRequest(Guid.NewGuid(), "plan", new[] { upload.Id }));
        await scope.Groups.AddMembersAsync("alice", groupId, new[] { "carol" });

        var forBob = await scope.Files.OpenAsync("bob", upload.Id, thumbnail: true);
        Assert.NotNull(forBob);
        Assert.Equal("image/webp", forBob!.ContentType);
        Assert.True(forBob.IsThumbnail);
        Assert.Null(await scope.Files.OpenAsync("carol", upload.Id, thumbnail: false)); // joined after it was sent
        Assert.Null(await scope.Files.OpenAsync("dave", upload.Id, thumbnail: false)); // not in the group
        Assert.Null(await scope.Files.OpenAsync("bob", upload.Id + 999, thumbnail: false));

        scope.Options.CurrentValue.Attachments.Enabled = false;
        Assert.Null(await scope.Files.OpenAsync("bob", upload.Id, thumbnail: false));
    }

    [Fact]
    public async Task ASenderWhoLeftAGroup_LosesItsFilesToo_EvenAfterRejoining()
    {
        await using var scope = await FileScope.CreateAsync();
        var groupId = (await scope.Groups.CreateAsync("bob", "Desk", new[] { "alice" })).Group!.Id;
        var upload = (await scope.Files.UploadAsync("alice", Content("pdf"), "mine.pdf")).Attachment!;
        await scope.Groups.SendAsync("alice", groupId, new ChatGroupSendRequest(Guid.NewGuid(), "file", new[] { upload.Id }));
        Assert.NotNull(await scope.Files.OpenAsync("alice", upload.Id, thumbnail: false));

        await scope.Groups.LeaveAsync("alice", groupId);
        Assert.Null(await scope.Files.OpenAsync("alice", upload.Id, thumbnail: false));

        scope.Db.ChangeTracker.Clear(); // separate requests (leaving deletes the membership directly)
        await scope.Groups.AddMembersAsync("bob", groupId, new[] { "alice" });
        Assert.Null(await scope.Files.OpenAsync("alice", upload.Id, thumbnail: false)); // sent before she rejoined
        Assert.NotNull(await scope.Files.OpenAsync("bob", upload.Id, thumbnail: false));
    }

    [Fact]
    public async Task Claiming_TakesOnlyUploadsStillAvailable()
    {
        await using var scope = await FileScope.CreateAsync();
        var first = (await scope.Files.UploadAsync("alice", Content("pdf"), "a.pdf")).Attachment!;
        var second = (await scope.Files.UploadAsync("alice", Content("pdf"), "b.pdf")).Attachment!;
        var sent = await scope.Chat.SendAsync("alice", new ChatSendRequest("bob", Guid.NewGuid(), "one", new[] { first.Id }));

        // As if a clean-up sweep (or another send) took a file between the check and the claim.
        Assert.Equal(ChatAttachmentService.FileGoneError, await ChatAttachmentService.ClaimAsync(scope.Db, "alice", new[] { first.Id }, sent.Message!.Id + 1, default));
        DateTimeOffset? removedAt = DateTimeOffset.UtcNow;
        await scope.Db.ChatAttachments.Where(a => a.Id == second.Id).ExecuteUpdateAsync(s => s.SetProperty(a => a.RemovedUtc, removedAt));
        Assert.Equal(ChatAttachmentService.FileGoneError, await ChatAttachmentService.ClaimAsync(scope.Db, "alice", new[] { second.Id }, sent.Message.Id, default));

        // The first file still belongs to the first message only.
        Assert.Equal(sent.Message.Id, (await scope.Db.ChatAttachments.AsNoTracking().SingleAsync(a => a.Id == first.Id)).MessageId);
    }

    [Fact]
    public async Task CleanUp_RetriesAFileItCouldNotDelete()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // an open file can be deleted on Linux, so there is nothing to retry there
        }

        await using var scope = await FileScope.CreateAsync();
        var upload = (await scope.Files.UploadAsync("alice", Content("pdf"), "busy.pdf")).Attachment!;
        await scope.Files.RemoveAsync("alice", upload.Id);
        var key = (await scope.Db.ChatAttachments.AsNoTracking().SingleAsync()).StorageKey;

        using (new FileStream(scope.PathOf(key), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(0, await scope.Files.CleanUpAsync()); // held open: not deleted, not marked done
        }

        Assert.Null((await scope.Db.ChatAttachments.AsNoTracking().SingleAsync()).PurgedUtc);
        Assert.Equal(1, await scope.Files.CleanUpAsync());
        Assert.False(File.Exists(scope.PathOf(key)));
    }

    [Fact]
    public async Task Unsending_RemovesTheFiles_AndGivesTheSpaceBack()
    {
        await using var scope = await FileScope.CreateAsync();
        var upload = (await scope.Files.UploadAsync("alice", Content("pdf"), "oops.pdf")).Attachment!;
        var sent = await scope.Chat.SendAsync("alice", new ChatSendRequest("bob", Guid.NewGuid(), "oops", new[] { upload.Id }));

        await scope.Chat.UnsendAsync("alice", sent.Message!.Id);

        Assert.Null(await scope.Files.OpenAsync("bob", upload.Id, thumbnail: false));
        Assert.Equal(0, (await scope.Files.GetUsageAsync("alice")).UsedBytes);
        var shown = (await scope.Chat.GetHistoryAsync("bob", "alice", null, 40)).Messages.Single().Attachments!.Single();
        Assert.True(shown.IsRemoved);
        Assert.Equal(string.Empty, shown.FileName);
    }

    [Fact]
    public async Task Removing_IsForTheSenderOnly_AndTellsWhoCanSeeTheMessage()
    {
        await using var scope = await FileScope.CreateAsync();
        var upload = (await scope.Files.UploadAsync("alice", Content("pdf"), "draft.pdf")).Attachment!;
        var sent = await scope.Chat.SendAsync("alice", new ChatSendRequest("bob", Guid.NewGuid(), "draft", new[] { upload.Id }));

        Assert.Null(await scope.Files.RemoveAsync("bob", upload.Id));

        var removal = await scope.Files.RemoveAsync("alice", upload.Id);
        Assert.Equal(sent.Message!.Id, removal!.Removed.MessageId);
        Assert.Null(removal.Removed.GroupId);
        Assert.Equal(new[] { "alice", "bob" }, removal.NotifyUserIds.OrderBy(id => id));
        Assert.Null(await scope.Files.RemoveAsync("alice", upload.Id)); // already removed
        // The message itself stays.
        Assert.Equal("draft", (await scope.Chat.GetHistoryAsync("bob", "alice", null, 40)).Messages.Single().Body);
    }

    [Fact]
    public async Task CleanUp_DeletesRemovedFilesAndUnsentUploads_AndCanRunAgain()
    {
        await using var scope = await FileScope.CreateAsync();
        var removed = (await scope.Files.UploadAsync("alice", Png(10, 10), "a.png")).Attachment!;
        var kept = (await scope.Files.UploadAsync("alice", Content("pdf"), "b.pdf")).Attachment!;
        await scope.Chat.SendAsync("alice", new ChatSendRequest("bob", Guid.NewGuid(), "b", new[] { kept.Id }));
        var abandoned = (await scope.Files.UploadAsync("alice", Content("pdf"), "c.pdf")).Attachment!;
        await scope.Files.RemoveAsync("alice", removed.Id);
        var removedRow = await scope.Db.ChatAttachments.AsNoTracking().SingleAsync(a => a.Id == removed.Id);

        scope.Clock.Advance(TimeSpan.FromMinutes(61));
        var deleted = await scope.Files.CleanUpAsync();

        Assert.Equal(2, deleted); // the removed image and the upload never sent
        Assert.False(File.Exists(scope.PathOf(removedRow.StorageKey)));
        Assert.False(File.Exists(scope.PathOf(removedRow.ThumbnailKey!)));
        Assert.NotNull(await scope.Files.OpenAsync("bob", kept.Id, thumbnail: false));
        Assert.Null(await scope.Files.OpenAsync("alice", abandoned.Id, thumbnail: false));
        Assert.Equal(0, await scope.Files.CleanUpAsync());
    }

    [Fact]
    public async Task Totals_ShowSizesAndPeople_NeverFileNames()
    {
        await using var scope = await FileScope.CreateAsync();
        await scope.Files.UploadAsync("alice", Content("pdf"), "secret-plans.pdf");

        var totals = await scope.Files.GetTotalsAsync();

        Assert.Equal(1, totals.Files);
        Assert.True(totals.TotalBytes > 0);
        Assert.Equal("Alice Anand", Assert.Single(totals.LargestUse).DisplayName);
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(totals));
    }

    [Theory]
    [InlineData("C:\\Users\\a\\report.pdf", "report.pdf")]
    [InlineData("../../etc/report.pdf", "report.pdf")]
    [InlineData("  bad\u0001name.pdf ", "badname.pdf")]
    public void FileNames_LoseFoldersAndControlCharacters(string given, string expected)
    {
        Assert.Equal(expected, ChatAttachmentService.CleanFileName(given));
    }

    // ------------------------------------------------------------------ test files

    private static MemoryStream Text(string text) => new(Encoding.UTF8.GetBytes(text));

    private static MemoryStream Png(int width, int height) => Image(width, height, SKEncodedImageFormat.Png);

    private static MemoryStream Image(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SteelBlue);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return new MemoryStream(data.ToArray());
    }

    private static MemoryStream Content(string kind) => kind switch
    {
        "pdf" => Text("%PDF-1.4\n1 0 obj\n<<>>\nendobj\n%%EOF"),
        "exe" => new MemoryStream(new byte[] { (byte)'M', (byte)'Z', 0x90, 0, 3, 0, 0, 0 }),
        "docx" => Zip(("[Content_Types].xml", "<Types/>"), ("word/document.xml", "<w:document/>")),
        "xlsx" => Zip(("[Content_Types].xml", "<Types/>"), ("xl/workbook.xml", "<workbook/>")),
        "docx-macros" => Zip(("[Content_Types].xml", "<Types/>"), ("word/document.xml", "<w:document/>"), ("word/vbaProject.bin", "x")),
        "jpeg" => Image(8, 8, SKEncodedImageFormat.Jpeg),
        "text" => Text("name,value\nalpha,1\n"),
        "nul" => new MemoryStream(new byte[] { (byte)'a', 0, (byte)'b' }),
        "latin1" => new MemoryStream(new byte[] { (byte)'c', (byte)'a', (byte)'f', 0xE9 }),
        "empty" => new MemoryStream(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static MemoryStream Zip(params (string Name, string Text)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(text);
            }
        }

        stream.Position = 0;
        return stream;
    }

    // ------------------------------------------------------------------ scope

    private sealed class FileScope : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _root;

        private FileScope(SqliteConnection connection, ApplicationDbContext db, string root, int allowanceMb, int maxFileMb)
        {
            _connection = connection;
            _root = root;
            Db = db;
            Options.CurrentValue.Attachments.AllowanceMb = allowanceMb;
            Options.CurrentValue.Attachments.MaxFileMb = maxFileMb;
            var rootProvider = new FixedRoot(root);
            Files = new ChatAttachmentService(db, rootProvider, new UploadPathResolver(rootProvider), Options, Clock, Disk, NullLogger<ChatAttachmentService>.Instance);
            Chat = new ChatService(db, Clock, new ChatPresenceTracker(), Options);
            Groups = new ChatGroupService(db, Clock, Options);
        }

        public ApplicationDbContext Db { get; }

        public ChatAttachmentService Files { get; }

        public ChatService Chat { get; }

        public ChatGroupService Groups { get; }

        public MutableOptions Options { get; } = new();

        public ManualClock Clock { get; } = new();

        public FakeDisk Disk { get; } = new();

        public string PathOf(string storageKey) => Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar));

        public static async Task<FileScope> CreateAsync(int allowanceMb = 1024, int maxFileMb = 100)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            foreach (var (id, name) in new[] { ("alice", "Alice Anand"), ("bob", "Bob Bhatia"), ("carol", "Carol Chauhan"), ("dave", "Dave Dutta") })
            {
                db.Users.Add(new ApplicationUser { Id = id, UserName = id, FullName = name, AccountKind = UserAccountKind.Human });
            }

            await db.SaveChangesAsync();
            var root = Path.Combine(Path.GetTempPath(), "prism-chat-files-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new FileScope(connection, db, root, allowanceMb, maxFileMb);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class FixedRoot : IUploadRootProvider
    {
        public FixedRoot(string root) => RootPath = root;

        public string RootPath { get; }

        public string GetProjectRoot(int projectId) => throw new NotSupportedException();

        public string GetProjectPhotosRoot(int projectId) => throw new NotSupportedException();

        public string GetProjectDocumentsRoot(int projectId) => throw new NotSupportedException();

        public string GetProjectCommentsRoot(int projectId) => throw new NotSupportedException();

        public string GetProjectVideosRoot(int projectId) => throw new NotSupportedException();

        public string GetSocialMediaRoot(string storagePrefix, Guid eventId) => throw new NotSupportedException();
    }

    public sealed class FakeDisk : IChatDiskSpace
    {
        public long? Free { get; set; } = 500L * 1024 * 1024 * 1024;

        public long? AvailableBytes(string path) => Free;
    }

    public sealed class ManualClock : IClock
    {
        private DateTimeOffset _now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

        public DateTimeOffset UtcNow => _now = _now.AddMilliseconds(10);

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    public sealed class MutableOptions : IOptionsMonitor<ChatOptions>
    {
        public ChatOptions CurrentValue { get; } = new() { Attachments = new ChatAttachmentOptions { Enabled = true } };

        public ChatOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<ChatOptions, string?> listener) => null;
    }
}

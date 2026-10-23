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
    public async Task RemovedSince_ListsRemovalsInMessagesTheCallerMayRead()
    {
        await using var scope = await FileScope.CreateAsync();
        var since = scope.Clock.UtcNow.AddMinutes(-5);
        var upload = (await scope.Files.UploadAsync("alice", Content("pdf"), "plan.pdf")).Attachment!;
        var groupId = (await scope.Groups.CreateAsync("alice", "Desk", new[] { "bob" })).Group!.Id;
        await scope.Groups.SendAsync("alice", groupId, new ChatGroupSendRequest(Guid.NewGuid(), "plan", new[] { upload.Id }));
        await scope.Groups.AddMembersAsync("alice", groupId, new[] { "carol" });
        var unsent = (await scope.Files.UploadAsync("alice", Content("pdf"), "draft.pdf")).Attachment!;

        Assert.Empty((await scope.Files.ListRemovedSinceAsync("bob", since)).Files);
        await scope.Files.RemoveAsync("alice", upload.Id);
        await scope.Files.RemoveAsync("alice", unsent.Id); // never sent: nobody else saw it

        var forBob = Assert.Single((await scope.Files.ListRemovedSinceAsync("bob", since)).Files);
        Assert.Equal(upload.Id, forBob.AttachmentId);
        Assert.Equal(groupId, forBob.GroupId);
        Assert.Empty((await scope.Files.ListRemovedSinceAsync("carol", since)).Files); // joined after it was sent
        Assert.Empty((await scope.Files.ListRemovedSinceAsync("dave", since)).Files);
        Assert.Empty((await scope.Files.ListRemovedSinceAsync("bob", scope.Clock.UtcNow.AddHours(1))).Files);
        Assert.Empty((await scope.Files.ListRemovedSinceAsync("bob", since, beforeAttachmentId: upload.Id)).Files);
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
        scope.Clock.Advance(TimeSpan.FromHours(25)); // past the undo window

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

        // Within the day a removal can be undone, nothing the sender removed is deleted yet.
        scope.Clock.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(1, await scope.Files.CleanUpAsync()); // only the upload never sent (final)
        Assert.True(File.Exists(scope.PathOf(removedRow.StorageKey)));

        scope.Clock.Advance(TimeSpan.FromHours(24));
        var deleted = await scope.Files.CleanUpAsync();

        Assert.Equal(1, deleted); // now the removed image too
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
    [InlineData("/mnt/data/uploads/chat", "/mnt/data")]
    [InlineData("/mnt/database/uploads", "/mnt/database")]
    [InlineData("/mnt/datastore/x", "/")]
    [InlineData("/var/pm/uploads", "/")]
    public void FreeSpace_IsMeasuredOnTheMountHoldingTheUploads(string path, string expected)
    {
        Assert.Equal(expected, ChatDiskSpace.MountFor(path, new[] { "/", "/mnt/data", "/mnt/database" }, ignoreCase: false));
    }

    [Fact]
    public void FreeSpace_OnWindows_UsesTheUploadDrive()
    {
        Assert.Equal("E:\\", ChatDiskSpace.MountFor("e:\\PrismData\\uploads", new[] { "C:\\", "E:\\" }, ignoreCase: true));
    }

    [Theory]
    [InlineData("C:\\Users\\a\\report.pdf", "report.pdf")]
    [InlineData("../../etc/report.pdf", "report.pdf")]
    [InlineData("  bad\u0001name.pdf ", "badname.pdf")]
    public void FileNames_LoseFoldersAndControlCharacters(string given, string expected)
    {
        Assert.Equal(expected, ChatAttachmentService.CleanFileName(given));
    }

    // ------------------------------------------------------------------ storage management

    private static async Task<ChatAttachmentDto> SendFileAsync(FileScope scope, string from, string to, string name, int sizeKb = 1)
    {
        var upload = (await scope.Files.UploadAsync(from, Text(new string('a', sizeKb * 1024)), name)).Attachment!;
        var sent = await scope.Chat.SendAsync(from, new ChatSendRequest(to, Guid.NewGuid(), name, new[] { upload.Id }));
        Assert.Equal(ChatSendStatus.Sent, sent.Status);
        return upload;
    }

    [Fact]
    public async Task MyFiles_ListRemoveAndRestoreWithinTheDay()
    {
        await using var scope = await FileScope.CreateAsync();
        var a = await SendFileAsync(scope, "alice", "bob", "a.txt", 10);
        var b = await SendFileAsync(scope, "alice", "bob", "b.txt", 20);

        var mine = await scope.Storage.ListMineAsync("alice");
        Assert.Equal(new[] { "b.txt", "a.txt" }, mine.Files.Select(f => f.FileName));
        Assert.All(mine.Files, f => Assert.Equal("Bob Bhatia", f.ConversationName));
        Assert.All(mine.Files, f => Assert.Equal("bob", f.ConversationKey));
        Assert.Equal(30 * 1024, mine.UsedBytes);
        Assert.Empty(await scope.Storage.ListMineAsync("bob") is var bobs ? bobs.Files : null!);

        var removed = await scope.Storage.RemoveMineAsync("alice", new[] { a.Id, b.Id, 9999 });
        Assert.Equal(2, removed.Change.Files);
        Assert.NotNull(removed.Change.BatchId);
        Assert.Equal(2, removed.Updates.Count);
        Assert.All(removed.Updates, u => Assert.Equal(new[] { "alice", "bob" }, u.NotifyUserIds.OrderBy(x => x)));
        Assert.Empty(scope.Outbox.Queued); // removing your own files tells nobody
        Assert.Equal(0, (await scope.Storage.ListMineAsync("alice")).UsedBytes);
        Assert.Null(await scope.Files.OpenAsync("bob", a.Id, thumbnail: false));

        var listed = (await scope.Storage.ListMineAsync("alice")).Files.Single(f => f.Id == a.Id);
        Assert.NotNull(listed.RemovedUtc);
        Assert.NotNull(listed.RestorableUntil);

        var restored = await scope.Storage.RestoreMineAsync("alice", new[] { a.Id });
        Assert.Equal(1, restored.Change.Files);
        Assert.Equal("a.txt", restored.Updates.Single().Restored!.FileName);
        Assert.NotNull(await scope.Files.OpenAsync("bob", a.Id, thumbnail: false));

        // A day later it is too late, and the file no longer appears.
        scope.Clock.Advance(TimeSpan.FromHours(25));
        Assert.NotNull((await scope.Storage.RestoreMineAsync("alice", new[] { b.Id })).Change.Error);
        Assert.DoesNotContain((await scope.Storage.ListMineAsync("alice")).Files, f => f.Id == b.Id);
    }

    [Fact]
    public async Task MyFiles_RemoveOlderThan_TakesOnlyOldFiles_AndOthersFilesAreUntouchable()
    {
        await using var scope = await FileScope.CreateAsync();
        var old = await SendFileAsync(scope, "alice", "bob", "old.txt");
        scope.Clock.Advance(TimeSpan.FromDays(40));
        var recent = await SendFileAsync(scope, "alice", "bob", "recent.txt");
        var bobs = await SendFileAsync(scope, "bob", "alice", "bobs.txt");

        var result = await scope.Storage.RemoveMineOlderThanAsync("alice", 30);

        Assert.Equal(1, result.Change.Files);
        Assert.Null(await scope.Files.OpenAsync("bob", old.Id, thumbnail: false));
        Assert.NotNull(await scope.Files.OpenAsync("bob", recent.Id, thumbnail: false));
        Assert.Equal(0, (await scope.Storage.RemoveMineAsync("alice", new[] { bobs.Id })).Change.Files);
        Assert.NotNull((await scope.Storage.RemoveMineOlderThanAsync("alice", 0)).Change.Error);
    }

    [Fact]
    public async Task MyFiles_RestoringMustFitTheAllowance()
    {
        await using var scope = await FileScope.CreateAsync(allowanceMb: 1);
        var first = await SendFileAsync(scope, "alice", "bob", "first.txt", 700);
        await scope.Storage.RemoveMineAsync("alice", new[] { first.Id });
        await SendFileAsync(scope, "alice", "bob", "second.txt", 700);

        var result = await scope.Storage.RestoreMineAsync("alice", new[] { first.Id });

        Assert.Contains("allowance", result.Change.Error!);
    }

    [Fact]
    public async Task Administrator_PreviewsRemovesAndUndoes_TellingOwnersAndAuditing()
    {
        await using var scope = await FileScope.CreateAsync();
        var alicesOld = await SendFileAsync(scope, "alice", "bob", "a-old.txt", 4);
        await SendFileAsync(scope, "bob", "alice", "b-old.txt", 2);
        scope.Clock.Advance(TimeSpan.FromDays(40));
        var alicesNew = await SendFileAsync(scope, "alice", "bob", "a-new.txt", 1);

        Assert.Equal(new ChatStoragePreviewDto(3, 7 * 1024, 2), await scope.Storage.PreviewAsync(new ChatStorageScope(null, null)));
        Assert.Equal(new ChatStoragePreviewDto(2, 6 * 1024, 2), await scope.Storage.PreviewAsync(new ChatStorageScope(null, 30)));
        Assert.Equal(new ChatStoragePreviewDto(2, 5 * 1024, 1), await scope.Storage.PreviewAsync(new ChatStorageScope("alice", null)));
        var people = await scope.Storage.ListPeopleAsync();
        Assert.Equal(new[] { "Alice Anand", "Bob Bhatia" }, people.Select(p => p.DisplayName));

        var removal = await scope.Storage.RemoveAsAdministratorAsync("dave", "Dave Dutta", new ChatStorageScope(null, 30));

        Assert.Equal(2, removal.Change.Files);
        Assert.Equal(new[] { "alice", "bob" }, scope.Outbox.Queued.Select(q => q.Recipient).OrderBy(x => x));
        Assert.All(scope.Outbox.Queued, q => Assert.Contains("An administrator removed 1 of your chat files", q.Summary));
        Assert.All(scope.Outbox.Queued, q => Assert.Equal("/Chat?view=files", q.Route));
        var audit = Assert.Single(scope.Audit.Entries);
        Assert.Equal("Chat.AttachmentsRemoved", audit.Action);
        Assert.DoesNotContain("a-old", System.Text.Json.JsonSerializer.Serialize(audit));
        Assert.NotNull(await scope.Files.OpenAsync("bob", alicesNew.Id, thumbnail: false));
        var shown = (await scope.Chat.GetHistoryAsync("bob", "alice", null, 40)).Messages.SelectMany(m => m.Attachments ?? Array.Empty<ChatAttachmentDto>()).Single(f => f.Id == alicesOld.Id);
        Assert.Equal("Admin", shown.RemovalReason);

        // The owner cannot restore an administrator's removal; an administrator can, within the day.
        Assert.NotNull((await scope.Storage.RestoreMineAsync("alice", new[] { alicesOld.Id })).Change.Error);
        var batch = Assert.Single(await scope.Storage.ListBatchesAsync());
        Assert.True(batch.CanUndo);
        Assert.Equal((2, 2), (batch.Files, batch.People));
        var undone = await scope.Storage.UndoBatchAsync("dave", "Dave Dutta", batch.BatchId);
        Assert.Equal(2, undone.Change.Files);
        Assert.NotNull(await scope.Files.OpenAsync("bob", alicesOld.Id, thumbnail: false));
        Assert.Equal("Chat.AttachmentsRestored", scope.Audit.Entries.Last().Action);
        Assert.NotNull((await scope.Storage.UndoBatchAsync("dave", null, batch.BatchId)).Change.Error);
    }

    [Fact]
    public async Task AnAdministratorsRemoval_IsDeletedFromDiskOnlyAfterTheUndoWindow()
    {
        await using var scope = await FileScope.CreateAsync();
        await SendFileAsync(scope, "alice", "bob", "a.txt");
        var key = (await scope.Db.ChatAttachments.AsNoTracking().SingleAsync()).StorageKey;
        await scope.Storage.RemoveAsAdministratorAsync("dave", null, new ChatStorageScope(null, null));

        Assert.Equal(0, await scope.Files.CleanUpAsync());
        Assert.True(File.Exists(scope.PathOf(key)));
        scope.Clock.Advance(TimeSpan.FromHours(25));
        Assert.Equal(1, await scope.Files.CleanUpAsync());
        Assert.False(File.Exists(scope.PathOf(key)));
        var batch = Assert.Single(await scope.Storage.ListBatchesAsync());
        Assert.False(batch.CanUndo);
    }

    [Fact]
    public async Task Retention_RemovesFilesOlderThanTheAge_OwnersAreToldAndMayRestore()
    {
        await using var scope = await FileScope.CreateAsync();
        var old = await SendFileAsync(scope, "alice", "bob", "old.txt");
        scope.Clock.Advance(TimeSpan.FromDays(366));
        var recent = await SendFileAsync(scope, "alice", "bob", "recent.txt");

        var result = await scope.Storage.ApplyRetentionAsync();

        Assert.Equal(1, result.Change.Files);
        Assert.Null(await scope.Files.OpenAsync("bob", old.Id, thumbnail: false));
        Assert.NotNull(await scope.Files.OpenAsync("bob", recent.Id, thumbnail: false));
        var told = Assert.Single(scope.Outbox.Queued);
        Assert.Equal("alice", told.Recipient);
        Assert.Contains("more than 365 days old", told.Summary);
        Assert.Equal("Chat.AttachmentsRemovedByRetention", Assert.Single(scope.Audit.Entries).Action);
        Assert.Equal(0, (await scope.Storage.ApplyRetentionAsync()).Change.Files); // nothing more to do

        Assert.Equal(1, (await scope.Storage.RestoreMineAsync("alice", new[] { old.Id })).Change.Files);

        scope.Options.CurrentValue.Attachments.RetentionDays = 0;
        Assert.NotNull((await scope.Storage.ApplyRetentionAsync()).Change.Error);
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(1572864, "1.5 MB")]
    [InlineData(104857600, "100 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(2576980378, "2.4 GB")]
    public void Sizes_ReadNaturally(long bytes, string expected)
    {
        Assert.Equal(expected, ChatAttachmentService.FormatSize(bytes));
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
            Storage = new ChatStorageService(db, Options, Clock, Outbox, Audit, NullLogger<ChatStorageService>.Instance);
        }

        public ApplicationDbContext Db { get; }

        public ChatAttachmentService Files { get; }

        public ChatService Chat { get; }

        public ChatGroupService Groups { get; }

        public ChatStorageService Storage { get; }

        public RecordingOutbox Outbox { get; } = new();

        public RecordingAudit Audit { get; } = new();

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

    public sealed class RecordingOutbox : ProjectManagement.Services.Notifications.INotificationOutboxWriter
    {
        public List<(string Recipient, string? Title, string? Summary, string? Route)> Queued { get; } = new();

        public Task<int> QueueAsync(
            ProjectManagement.Models.Notifications.NotificationKind kind,
            IReadOnlyCollection<string> recipientUserIds,
            object payload,
            string? module,
            string? eventType,
            string? scopeType,
            string? scopeId,
            int? projectId,
            string? actorUserId,
            string? route,
            string? title,
            string? summary,
            string? fingerprint,
            System.Threading.CancellationToken cancellationToken = default)
        {
            Assert.Equal(ProjectManagement.Models.Notifications.NotificationKind.ChatFilesRemoved, kind);
            foreach (var recipient in recipientUserIds)
            {
                Queued.Add((recipient, title, summary, route));
            }

            return Task.FromResult(recipientUserIds.Count);
        }
    }

    public sealed class RecordingAudit : ProjectManagement.Services.Admin.IAdminAuditService
    {
        public List<ProjectManagement.Services.Admin.AdminAuditEntry> Entries { get; } = new();

        public Task RecordAsync(ProjectManagement.Services.Admin.AdminAuditEntry entry, System.Threading.CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
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

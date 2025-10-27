using System;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProjectManagement.Contracts.Activities;
using ProjectManagement.Data;
using ProjectManagement.Infrastructure.Activities;
using ProjectManagement.Models.Activities;
using ProjectManagement.Services;
using ProjectManagement.Services.Activities;
using ProjectManagement.Services.Storage;
using Xunit;

namespace ProjectManagement.Tests.Activities;

public class ActivityInputValidatorTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IActivityRepository _activityRepository;
    private readonly IActivityTypeRepository _activityTypeRepository;
    private readonly ActivityInputValidator _validator;

    public ActivityInputValidatorTests()
    {
        _context = ActivityTestHelpers.CreateContext();
        _activityRepository = new ActivityRepository(_context);
        _activityTypeRepository = new ActivityTypeRepository(_context);
        _validator = new ActivityInputValidator(_activityRepository, _activityTypeRepository);
    }

    [Fact]
    public async Task ValidateAsync_ThrowsWhenTitleMissing()
    {
        var input = new ActivityInput(string.Empty, null, null, 0, null, null);
        await Assert.ThrowsAsync<ActivityValidationException>(() => _validator.ValidateAsync(input, null, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_ThrowsWhenTypeInactive()
    {
        var type = new ActivityType
        {
            Name = "Briefings",
            CreatedByUserId = "user",
            IsActive = false
        };
        _context.ActivityTypes.Add(type);
        await _context.SaveChangesAsync();

        var input = new ActivityInput("Kickoff", null, null, type.Id, null, null);
        var ex = await Assert.ThrowsAsync<ActivityValidationException>(() => _validator.ValidateAsync(input, null, CancellationToken.None));
        Assert.Contains(nameof(input.ActivityTypeId), ex.Errors.Keys);
    }

    [Fact]
    public async Task ValidateAsync_ThrowsWhenDuplicateTitle()
    {
        var type = new ActivityType
        {
            Name = "Training",
            CreatedByUserId = "user",
            IsActive = true
        };
        _context.ActivityTypes.Add(type);
        await _context.SaveChangesAsync();

        var activity = new Activity
        {
            Title = "Safety Brief",
            ActivityTypeId = type.Id,
            CreatedByUserId = "owner"
        };
        await _activityRepository.AddAsync(activity);

        var input = new ActivityInput("Safety Brief", null, null, type.Id, null, null);
        var ex = await Assert.ThrowsAsync<ActivityValidationException>(() => _validator.ValidateAsync(input, null, CancellationToken.None));
        Assert.Contains(nameof(input.Title), ex.Errors.Keys);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}

public class ActivityTypeValidatorTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ActivityTypeValidator _validator;
    private readonly IActivityTypeRepository _activityTypeRepository;

    public ActivityTypeValidatorTests()
    {
        _context = ActivityTestHelpers.CreateContext();
        _activityTypeRepository = new ActivityTypeRepository(_context);
        _validator = new ActivityTypeValidator(_activityTypeRepository);
    }

    [Fact]
    public async Task ValidateAsync_ThrowsWhenNameDuplicate()
    {
        _context.ActivityTypes.Add(new ActivityType
        {
            Name = "Engagement",
            CreatedByUserId = "user"
        });
        await _context.SaveChangesAsync();

        var input = new ActivityTypeInput("Engagement", null, true);
        var ex = await Assert.ThrowsAsync<ActivityValidationException>(() => _validator.ValidateAsync(input, null, CancellationToken.None));
        Assert.Contains(nameof(input.Name), ex.Errors.Keys);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}

public class ActivityAttachmentValidatorTests
{
    private readonly ActivityAttachmentValidator _validator = new();

    [Fact]
    public void Validate_ThrowsForUnsupportedContentType()
    {
        var upload = new ActivityAttachmentUpload(new MemoryStream(new byte[] { 1 }), "file.exe", "application/octet-stream", 1);
        var ex = Assert.Throws<ActivityValidationException>(() => _validator.Validate(upload));
        Assert.Contains(nameof(upload.ContentType), ex.Errors.Keys);
    }
}

public class ActivityServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ActivityRepository _activityRepository;
    private readonly ActivityTypeRepository _activityTypeRepository;
    private readonly ActivityInputValidator _inputValidator;
    private readonly ActivityTypeValidator _typeValidator;
    private readonly ActivityAttachmentValidator _attachmentValidator;
    private readonly ActivityAttachmentManager _attachmentManager;
    private readonly TestUserContext _userContext;
    private readonly TestClock _clock = new();
    private readonly TestUploadRootProvider _uploadRoot;
    private readonly ActivityService _service;

    public ActivityServiceTests()
    {
        _context = ActivityTestHelpers.CreateContext();
        _activityRepository = new ActivityRepository(_context);
        _activityTypeRepository = new ActivityTypeRepository(_context);
        _inputValidator = new ActivityInputValidator(_activityRepository, _activityTypeRepository);
        _typeValidator = new ActivityTypeValidator(_activityTypeRepository);
        _attachmentValidator = new ActivityAttachmentValidator();
        _userContext = new TestUserContext("owner");
        _uploadRoot = new TestUploadRootProvider();
        var storage = new FileSystemActivityAttachmentStorage(_uploadRoot, NullLogger<FileSystemActivityAttachmentStorage>.Instance);
        _attachmentManager = new ActivityAttachmentManager(_activityRepository, storage, _attachmentValidator, _clock);
        _service = new ActivityService(_activityRepository,
            _inputValidator,
            _attachmentManager,
            _userContext,
            _clock,
            NullLogger<ActivityService>.Instance);
    }

    [Fact]
    public async Task CreateAsync_PersistsActivity()
    {
        var type = await EnsureActivityTypeAsync();

        var input = new ActivityInput("Project Kickoff", "Initial meeting", "HQ", type.Id, _clock.UtcNow, _clock.UtcNow.AddHours(2));
        var activity = await _service.CreateAsync(input);

        Assert.Equal("Project Kickoff", activity.Title);
        Assert.Equal("owner", activity.CreatedByUserId);
        Assert.NotEqual(default, activity.Id);
    }

    [Fact]
    public async Task UpdateAsync_ThrowsForUnauthorizedUser()
    {
        var type = await EnsureActivityTypeAsync();
        var created = await _service.CreateAsync(new ActivityInput("Workshop", null, null, type.Id, null, null));

        var otherUser = new TestUserContext("other");
        var otherService = new ActivityService(_activityRepository,
            _inputValidator,
            _attachmentManager,
            otherUser,
            _clock,
            NullLogger<ActivityService>.Instance);

        await Assert.ThrowsAsync<ActivityAuthorizationException>(() => otherService.UpdateAsync(created.Id, new ActivityInput("Workshop", null, null, type.Id, null, null)));
    }

    [Fact]
    public async Task UpdateAsync_AllowsHoDOverride()
    {
        var type = await EnsureActivityTypeAsync();
        var created = await _service.CreateAsync(new ActivityInput("Seminar", null, null, type.Id, null, null));

        var hodContext = new TestUserContext("hod-user", isHoD: true);
        var hodService = new ActivityService(_activityRepository,
            _inputValidator,
            _attachmentManager,
            hodContext,
            _clock,
            NullLogger<ActivityService>.Instance);

        var updated = await hodService.UpdateAsync(created.Id, new ActivityInput("Seminar Updated", null, null, type.Id, null, null));
        Assert.Equal("Seminar Updated", updated.Title);
    }

    [Fact]
    public async Task DeleteAsync_MarksActivityDeleted()
    {
        var type = await EnsureActivityTypeAsync();
        var created = await _service.CreateAsync(new ActivityInput("Review", null, null, type.Id, null, null));

        await _service.DeleteAsync(created.Id);

        var stored = await _activityRepository.GetByIdAsync(created.Id);
        Assert.True(stored!.IsDeleted);
        Assert.NotNull(stored.DeletedAtUtc);
    }

    [Fact]
    public async Task DeleteAsync_RemovesStoredAttachments()
    {
        var type = await EnsureActivityTypeAsync();
        var created = await _service.CreateAsync(new ActivityInput("Archive", null, null, type.Id, null, null));

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        var upload = new ActivityAttachmentUpload(stream, "notes.pdf", "application/pdf", stream.Length);
        var attachment = await _service.AddAttachmentAsync(created.Id, upload);
        var path = Path.Combine(_uploadRoot.RootPath, attachment.StorageKey.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path));

        await _service.DeleteAsync(created.Id);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task AddAttachmentAsync_SavesFile()
    {
        var type = await EnsureActivityTypeAsync();
        var created = await _service.CreateAsync(new ActivityInput("Brief", null, null, type.Id, null, null));

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        var upload = new ActivityAttachmentUpload(stream, "notes.pdf", "application/pdf", stream.Length);

        var attachment = await _service.AddAttachmentAsync(created.Id, upload);

        Assert.Equal("notes.pdf", attachment.OriginalFileName);
        var path = Path.Combine(_uploadRoot.RootPath, attachment.StorageKey.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task RemoveAttachmentAsync_AllowsUploader()
    {
        var type = await EnsureActivityTypeAsync();
        var created = await _service.CreateAsync(new ActivityInput("Drill", null, null, type.Id, null, null));

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        var upload = new ActivityAttachmentUpload(stream, "notes.pdf", "application/pdf", stream.Length);
        var attachment = await _service.AddAttachmentAsync(created.Id, upload);

        await _service.RemoveAttachmentAsync(attachment.Id);

        var stored = await _activityRepository.GetAttachmentByIdAsync(attachment.Id);
        Assert.Null(stored);
    }

    [Fact]
    public async Task AddAttachmentAsync_EnforcesAttachmentLimit()
    {
        var type = await EnsureActivityTypeAsync();
        var created = await _service.CreateAsync(new ActivityInput("Briefing", null, null, type.Id, null, null));

        for (var i = 0; i < ActivityAttachmentManager.MaxAttachmentsPerActivity; i++)
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"file-{i}"));
            var upload = new ActivityAttachmentUpload(stream, $"file-{i}.pdf", "application/pdf", stream.Length);
            await _service.AddAttachmentAsync(created.Id, upload);
        }

        await using var overflowStream = new MemoryStream(Encoding.UTF8.GetBytes("overflow"));
        var overflowUpload = new ActivityAttachmentUpload(overflowStream, "overflow.pdf", "application/pdf", overflowStream.Length);

        var ex = await Assert.ThrowsAsync<ActivityValidationException>(() => _service.AddAttachmentAsync(created.Id, overflowUpload));
        Assert.Contains(nameof(Activity.Attachments), ex.Errors.Keys);
    }

    [Fact]
    public async Task GetAttachmentMetadataAsync_ReturnsDownloadLink()
    {
        var type = await EnsureActivityTypeAsync();
        var created = await _service.CreateAsync(new ActivityInput("Review", null, null, type.Id, null, null));

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        var upload = new ActivityAttachmentUpload(stream, "  diagram?.pdf  ", "application/pdf", stream.Length);
        var attachment = await _service.AddAttachmentAsync(created.Id, upload);

        var metadata = await _service.GetAttachmentMetadataAsync(created.Id);
        var item = Assert.Single(metadata);
        Assert.Equal(ActivityAttachmentValidator.SanitizeFileName(upload.FileName), item.FileName);
        Assert.Equal($"/files/{attachment.StorageKey}", item.DownloadUrl);
        Assert.Equal(attachment.Id, item.Id);
    }

    [Fact]
    public async Task ActivityTypeService_RestrictsNonPrivilegedUsers()
    {
        var typeService = new ActivityTypeService(_activityTypeRepository, _typeValidator, _userContext, _clock);
        await Assert.ThrowsAsync<ActivityAuthorizationException>(() => typeService.CreateAsync(new ActivityTypeInput("Operations", null, true)));
    }

    [Fact]
    public async Task ActivityTypeService_AllowsAdmin()
    {
        var adminContext = new TestUserContext("admin", isAdmin: true);
        var typeService = new ActivityTypeService(_activityTypeRepository, _typeValidator, adminContext, _clock);

        var type = await typeService.CreateAsync(new ActivityTypeInput("Logistics", null, true));
        Assert.Equal("Logistics", type.Name);
    }

    [Fact]
    public async Task ActivityExportService_ProducesCsv()
    {
        var type = await EnsureActivityTypeAsync();
        await _service.CreateAsync(new ActivityInput("Planning", null, "HQ", type.Id, _clock.UtcNow, _clock.UtcNow.AddHours(1)));

        var exportService = new ActivityExportService(_activityRepository, _activityTypeRepository, _attachmentManager);
        var result = await exportService.ExportByTypeAsync(type.Id);

        Assert.Equal("text/csv", result.ContentType);
        var csv = Encoding.UTF8.GetString(result.Content);
        Assert.Contains("Planning", csv);
    }

    private async Task<ActivityType> EnsureActivityTypeAsync()
    {
        var type = new ActivityType
        {
            Name = "Operations",
            IsActive = true,
            CreatedByUserId = "seed"
        };
        await _activityTypeRepository.AddAsync(type);
        return type;
    }

    public void Dispose()
    {
        _uploadRoot.Dispose();
        _context.Dispose();
    }
}

internal sealed class TestUserContext : IUserContext
{
    public TestUserContext(string userId, bool isAdmin = false, bool isHoD = false)
    {
        UserId = userId;
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
        if (isAdmin)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
        }
        if (isHoD)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, "HoD"));
        }

        User = new ClaimsPrincipal(identity);
    }

    public ClaimsPrincipal User { get; }

    public string? UserId { get; }
}

internal sealed class TestClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class TestUploadRootProvider : IUploadRootProvider, IDisposable
{
    public TestUploadRootProvider()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "pm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string GetProjectRoot(int projectId) => throw new NotSupportedException();

    public string GetProjectPhotosRoot(int projectId) => throw new NotSupportedException();

    public string GetProjectDocumentsRoot(int projectId) => throw new NotSupportedException();

    public string GetProjectCommentsRoot(int projectId) => throw new NotSupportedException();

    public string GetProjectVideosRoot(int projectId) => throw new NotSupportedException();

    public string GetSocialMediaRoot(string storagePrefix, Guid eventId) => throw new NotSupportedException();

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}

internal static class ActivityTestHelpers
{
    public static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}

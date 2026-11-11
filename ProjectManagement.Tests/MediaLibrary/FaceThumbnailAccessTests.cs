using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Features.MediaLibrary.Data;
using ProjectManagement.Features.MediaLibrary.Domain;
using ProjectManagement.Features.MediaLibrary.Options;
using ProjectManagement.Features.MediaLibrary.Services;
using ProjectManagement.Pages.Photos;
using ProjectManagement.Tests.Fakes;

namespace ProjectManagement.Tests.MediaLibrary;

/// <summary>
/// "Find more photos of me" lets a staff member linked to a person confirm or reject that
/// person's suggested faces, so they must be able to see those crops, and nothing else.
/// </summary>
public sealed class FaceThumbnailAccessTests : IDisposable
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid SomeoneElse = Guid.NewGuid();
    private static readonly Guid FaceId = Guid.NewGuid();
    private readonly string _contentRoot = Path.Combine(Path.GetTempPath(), $"face-access-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Admin_can_view_any_visible_face()
        => Assert.IsType<PhysicalFileResult>(await GetAsync(Principal("Admin"), personId: null, linkedPerson: null, eligible: false));

    [Fact]
    public async Task Unlinked_staff_cannot_view_faces()
        => Assert.IsType<ForbidResult>(await GetAsync(Principal(), personId: Me, linkedPerson: null, eligible: true));

    [Fact]
    public async Task Linked_staff_can_view_their_current_suggestions()
        => Assert.IsType<PhysicalFileResult>(await GetAsync(Principal(), personId: Me, linkedPerson: Me, eligible: true));

    [Fact]
    public async Task Linked_staff_cannot_view_faces_that_are_not_their_suggestions()
        => Assert.IsType<ForbidResult>(await GetAsync(Principal(), personId: Me, linkedPerson: Me, eligible: false));

    [Fact]
    public async Task Linked_staff_cannot_view_another_persons_suggestions()
        => Assert.IsType<ForbidResult>(await GetAsync(Principal(), personId: SomeoneElse, linkedPerson: Me, eligible: true));

    private async Task<IActionResult> GetAsync(ClaimsPrincipal user, Guid? personId, Guid? linkedPerson, bool eligible)
    {
        var options = new MediaLibraryOptions
        {
            Enabled = true,
            CacheRoot = "cache",
            Catalogue = new MediaCatalogueOptions { Enabled = true },
            People = new MediaPeopleOptions { Enabled = true }
        };
        await using var db = new MediaLibraryDbContext(new DbContextOptionsBuilder<MediaLibraryDbContext>()
            .UseInMemoryDatabase($"face-access-{Guid.NewGuid():N}")
            .Options);
        var source = ReclassifySeed.Source();
        db.Sources.Add(source);
        db.Assets.Add(ReclassifySeed.Asset(source, 1, null));
        db.Faces.Add(new MediaFace
        {
            Id = FaceId,
            MediaAssetId = 1,
            SequenceNumber = 1,
            DetectorModelKey = "yunet",
            DetectorModelVersion = "1",
            ReviewThumbnailPath = "faces/ab/face.webp",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var file = Path.Combine(_contentRoot, "cache", "faces", "ab", "face.webp");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, new byte[] { 1, 2, 3 });

        var links = Stub<IMediaPersonUserLinkService>.Create(method => method == nameof(IMediaPersonUserLinkService.TryGetPhotoIdentityForUserAsync)
            ? Task.FromResult<MediaUserPhotoIdentityLink?>(linkedPerson is { } person
                ? new MediaUserPhotoIdentityLink(person, "Me", false, false, false, null, null)
                : null)
            : throw new NotSupportedException(method));
        var discovery = Stub<IPersonPhotoDiscoveryQueryService>.Create(method => method == nameof(IPersonPhotoDiscoveryQueryService.GetEligibleCandidatesAsync)
            ? Task.FromResult<IReadOnlyDictionary<Guid, PersonPhotoCandidate>>(eligible
                ? new Dictionary<Guid, PersonPhotoCandidate> { [FaceId] = null! }
                : new Dictionary<Guid, PersonPhotoCandidate>())
            : throw new NotSupportedException(method));

        var page = new FaceThumbnailModel(
            db,
            new MediaAssetVisibilityPolicy(Options.Create(options)),
            links,
            discovery,
            Options.Create(options),
            new TestWebHostEnvironment { ContentRootPath = _contentRoot })
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = user } }
        };

        return await page.OnGetAsync(FaceId, personId, CancellationToken.None);
    }

    private static ClaimsPrincipal Principal(params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "user-1") };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    /// <summary>Minimal interface stub: one delegate answers every call by method name.</summary>
    public class Stub<T> : DispatchProxy where T : class
    {
        private Func<string, object?> _handler = _ => throw new NotSupportedException();

        public static T Create(Func<string, object?> handler)
        {
            var proxy = DispatchProxy.Create<T, Stub<T>>();
            ((Stub<T>)(object)proxy)._handler = handler;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => _handler(targetMethod!.Name);
    }
}

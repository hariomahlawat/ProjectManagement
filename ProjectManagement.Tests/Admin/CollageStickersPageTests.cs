using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using ProjectManagement.Areas.Admin.Pages.CollageStickers;
using ProjectManagement.Configuration;
using ProjectManagement.Features.Collage;
using ProjectManagement.Models;
using ProjectManagement.Services.Admin;
using ProjectManagement.Services.Navigation.ModuleNav;
using Xunit;

namespace ProjectManagement.Tests.Admin;

/// <summary>Administration › Crests and logos: who may open it, where it is listed and what it shows.</summary>
public sealed class CollageStickersPageTests
{
    [Fact]
    public void The_page_needs_the_crests_and_logos_permission_which_only_admins_hold()
    {
        var attribute = typeof(IndexModel).GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(AdminPolicies.CollageStickersManage, attribute?.Policy);

        var capability = new AdminCapabilityCatalog().FindByPolicy(AdminPolicies.CollageStickersManage);
        Assert.NotNull(capability);
        Assert.Equal(new[] { "Admin" }, capability!.PermittedRoles);
    }

    [Fact]
    public void The_page_is_in_every_admin_page_list_and_found_by_crest_or_logo()
    {
        var item = AdminNavigationCatalog.Get(AdminNavigationKeys.CollageStickers);
        Assert.Equal("Crests and logos", item.Text);
        var entry = Assert.Single(AdminNavigationCatalog.Entries, e => e.Key == AdminNavigationKeys.CollageStickers);
        Assert.Contains("crest", entry.Keywords);
        Assert.Contains("logo", entry.Keywords);
    }

    [Fact]
    public async Task It_lists_stickers_in_use_and_retired_ones_apart_and_an_empty_upload_is_refused()
    {
        var service = new StubStickers(
            new CollageSticker { Id = 1, Name = "Crest", StorageKey = "collage-stickers/a.png" },
            new CollageSticker { Id = 2, Name = "Old", StorageKey = "collage-stickers/b.png", IsRetired = true });
        var page = CreatePage(service);

        await page.OnGetAsync(CancellationToken.None);
        Assert.Equal("Crest", Assert.Single(page.Active).Name);
        Assert.Equal("Old", Assert.Single(page.Retired).Name);
        Assert.Equal("/api/collage/stickers/1/image?v=1", IndexModel.PictureUrl(page.Active[0]));

        var result = await page.OnPostAddAsync(null, "Crest", null, CancellationToken.None);
        Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("Choose a picture to upload.", page.TempData[FlashMessageKeys.AdminCollageStickersError]);
        Assert.False(service.AddCalled);
    }

    private static IndexModel CreatePage(ICollageStickerService service)
    {
        var httpContext = new DefaultHttpContext();
        return new IndexModel(service)
        {
            PageContext = new Microsoft.AspNetCore.Mvc.RazorPages.PageContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new MemoryTempData())
        };
    }

    private sealed class MemoryTempData : ITempDataProvider
    {
        private IDictionary<string, object> _values = new Dictionary<string, object>();
        public IDictionary<string, object> LoadTempData(HttpContext context) => _values;
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) => _values = values;
    }

    private sealed class StubStickers(params CollageSticker[] stickers) : ICollageStickerService
    {
        public bool AddCalled { get; private set; }
        public Task<IReadOnlyList<CollageSticker>> ListActiveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CollageSticker>>(stickers.Where(s => !s.IsRetired).ToList());
        public Task<IReadOnlyList<CollageSticker>> ListAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CollageSticker>>(stickers.ToList());
        public Task<CollageSticker?> FindAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(stickers.FirstOrDefault(s => s.Id == id));
        public string? PicturePathOf(CollageSticker sticker) => null;
        public Task<CollageStickerOutcome> AddAsync(Stream content, string fileName, string name, string? searchWords, string? actorUserId, CancellationToken cancellationToken = default)
        {
            AddCalled = true;
            return Task.FromResult(CollageStickerOutcome.Done("Added."));
        }
        public Task<CollageStickerOutcome> RenameAsync(int id, string name, string? searchWords, string? actorUserId, CancellationToken cancellationToken = default) => Task.FromResult(CollageStickerOutcome.Done("Saved."));
        public Task<CollageStickerOutcome> MoveAsync(int id, bool up, string? actorUserId, CancellationToken cancellationToken = default) => Task.FromResult(CollageStickerOutcome.Done("Moved."));
        public Task<CollageStickerOutcome> RetireAsync(int id, string? actorUserId, CancellationToken cancellationToken = default) => Task.FromResult(CollageStickerOutcome.Done("Retired."));
        public Task<CollageStickerOutcome> RestoreAsync(int id, string? actorUserId, CancellationToken cancellationToken = default) => Task.FromResult(CollageStickerOutcome.Done("Back."));
    }
}

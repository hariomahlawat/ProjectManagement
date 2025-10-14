using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProjectManagement.Areas.ProjectOfficeReports.Application;
using ProjectManagement.Areas.ProjectOfficeReports.Pages.SocialMedia;
using ProjectManagement.Data;

namespace ProjectManagement.Tests;

public sealed class SocialMediaViewPhotoPageTests
{
    [Theory]
    [InlineData(null, "thumb")]
    [InlineData("thumb", "thumb")]
    [InlineData("feed", "feed")]
    [InlineData("story", "story")]
    public async Task OnGet_ReturnsFile_ForAllowedDerivative(string? requestedSize, string expectedSize)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var db = new ApplicationDbContext(optionsBuilder);

        var eventType = SocialMediaTestData.CreateEventType();
        var eventId = Guid.NewGuid();
        var photoId = Guid.NewGuid();
        var socialEvent = SocialMediaTestData.CreateEvent(eventType.Id, id: eventId);

        db.SocialMediaEventTypes.Add(eventType);
        db.SocialMediaEvents.Add(socialEvent);
        await db.SaveChangesAsync();

        var stubService = new StubSocialMediaEventPhotoService(
            assets: new Dictionary<(Guid, Guid), byte[]>
            {
                [(eventId, photoId)] = new byte[] { 0x01, 0x02, 0x03 }
            });

        var options = Options.Create(new SocialMediaPhotoOptions
        {
            Derivatives = new Dictionary<string, SocialMediaPhotoDerivativeOptions>(StringComparer.OrdinalIgnoreCase)
            {
                ["thumb"] = new SocialMediaPhotoDerivativeOptions { Width = 600, Height = 600, Quality = 80 },
                ["feed"] = new SocialMediaPhotoDerivativeOptions { Width = 1200, Height = 1200, Quality = 85 },
                ["story"] = new SocialMediaPhotoDerivativeOptions { Width = 1080, Height = 1920, Quality = 85 }
            }
        });

        var model = new ViewPhotoModel(db, stubService, options);
        ConfigurePageContext(model);

        var result = await model.OnGetAsync(eventId, photoId, requestedSize, CancellationToken.None);

        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/jpeg", fileResult.ContentType);
        Assert.True(fileResult.EnableRangeProcessing);

        var request = Assert.Single(stubService.OpenRequests);
        Assert.Equal(eventId, request.EventId);
        Assert.Equal(photoId, request.PhotoId);
        Assert.Equal(expectedSize, request.Size);
    }

    private static void ConfigurePageContext(PageModel page)
    {
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(httpContext, new RouteData(), new PageActionDescriptor());
        page.PageContext = new PageContext(actionContext);
    }
}

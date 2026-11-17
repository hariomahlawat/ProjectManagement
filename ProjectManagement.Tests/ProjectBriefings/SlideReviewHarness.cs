using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectManagement.Data;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings.Presentation;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

// A developer tool, not a test (docs/ux/briefing-slides-review.md): writes one deck per project
// slide design, every slide switched on, from a real local database, so the slides can be exported
// through PowerPoint and reviewed. It does nothing unless PRISM_SLIDE_REVIEW_CONNECTION is set, so
// CI never runs it. Never point it at a production database.
//
//   PRISM_SLIDE_REVIEW_CONNECTION  connection string of a local (throwaway) database
//   PRISM_SLIDE_REVIEW_OUT         folder for the .pptx files
//   PRISM_SLIDE_REVIEW_DECK        deck id (default 1)
//   PRISM_SLIDE_REVIEW_USER        user name allowed to open the deck (default: the deck's owner)
//   PRISM_SLIDE_REVIEW_THEME       "light" for every design in the light theme (default: the deck's)
public sealed class SlideReviewHarness
{
    [Fact]
    public async Task WriteReviewDecks()
    {
        var connection = Environment.GetEnvironmentVariable("PRISM_SLIDE_REVIEW_CONNECTION");
        var output = Environment.GetEnvironmentVariable("PRISM_SLIDE_REVIEW_OUT");
        if (string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        var deckId = long.TryParse(Environment.GetEnvironmentVariable("PRISM_SLIDE_REVIEW_DECK"), out var parsed) ? parsed : 1;
        var userName = Environment.GetEnvironmentVariable("PRISM_SLIDE_REVIEW_USER") ?? string.Empty;
        Directory.CreateDirectory(output);

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connection);
        });
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ApplicationDbContext>();
        // The named user, else the deck's owner.
        var userId = string.IsNullOrWhiteSpace(userName)
            ? await db.Set<ProjectBriefingDeck>().IgnoreQueryFilters().Where(deck => deck.Id == deckId).Select(deck => deck.OwnerUserId).SingleAsync()
            : await db.Users.Where(user => user.UserName == userName).Select(user => user.Id).SingleAsync();
        var data = await services.GetRequiredService<IProjectBriefingDataService>()
            .BuildPresentationDataAsync(deckId, userId, CancellationToken.None);

        // Photos as the export attaches them.
        var photos = services.GetRequiredService<IProjectBriefingPhotoLoader>();
        foreach (var project in data.Projects.Where(item => item.CoverPhotoId.HasValue))
        {
            var loaded = await photos.LoadAsync(project.ProjectId, project.CoverPhotoId!.Value, CancellationToken.None);
            if (loaded is not null)
            {
                project.CoverPhoto = loaded.Content;
                project.CoverPhotoContentType = loaded.ContentType;
            }
        }

        var composer = services.GetRequiredService<IProjectBriefingSlideComposer>();
        // PRISM_SLIDE_REVIEW_THEME=light composes every design in the light theme, its FFC map
        // redrawn in light colours.
        if (string.Equals(Environment.GetEnvironmentVariable("PRISM_SLIDE_REVIEW_THEME"), "light", StringComparison.OrdinalIgnoreCase))
        {
            data = Clone(data);
            Set(data, nameof(ProjectBriefingPresentationData.PresentationTheme), ProjectBriefingPresentationTheme.EditorialLight);
            if (data.FfcGlobalFootprint is { } lightFootprint)
            {
                var mapRenderer = services.GetRequiredService<ProjectManagement.Services.Ffc.Presentation.IFfcPresentationMapRenderer>();
                Set(data, nameof(ProjectBriefingPresentationData.FfcGlobalFootprint), new ProjectBriefingFfcGlobalFootprintData
                {
                    Title = lightFootprint.Title,
                    Layout = lightFootprint.Layout,
                    IncludeCountryWiseBreakdown = lightFootprint.IncludeCountryWiseBreakdown,
                    Summary = lightFootprint.Summary,
                    Countries = lightFootprint.Countries,
                    MaximumCountryRows = lightFootprint.MaximumCountryRows,
                    MapImage = mapRenderer.RenderBriefingBand(
                        lightFootprint.Countries.Select((country, index) => new ProjectManagement.Services.Ffc.Presentation.FfcPresentationCountry(
                            index + 1, country.CountryName, country.IsoCode, 0, country.ProjectCount,
                            country.InstalledUnits, country.DeliveredNotInstalledUnits, country.PlannedUnits,
                            DateTimeOffset.UtcNow, Array.Empty<ProjectManagement.Services.Ffc.Presentation.FfcPresentationRecord>())).ToArray(),
                        ProjectBriefingSlideComposer.FfcMapBandPixelWidth,
                        ProjectBriefingSlideComposer.FfcMapBandPixelHeight,
                        dark: false),
                    DataAsOnUtc = lightFootprint.DataAsOnUtc
                });
            }
        }

        var allBlocks = Enum.GetValues<ProjectBriefingDeckBlockType>()
            .Where(type => (data.Blocks ?? Array.Empty<ProjectBriefingDeckBlock>()).Any(block => block.Type == type))
            .Select(type => new ProjectBriefingDeckBlock(type, Enabled: true))
            .ToArray();
        foreach (var design in new[]
        {
            ProjectBriefingProjectSlideDesign.Capability,
            ProjectBriefingProjectSlideDesign.KeyPoints,
            ProjectBriefingProjectSlideDesign.Brief,
            ProjectBriefingProjectSlideDesign.KeyPointsOnly,
            ProjectBriefingProjectSlideDesign.FactSheet
        })
        {
            var settings = ProjectBriefingProjectSlideDesigns.Settings(
                design,
                data.NarrativeMode,
                data.StandardSlideOptions.KeyPoints,
                showKeyPoints: true,
                ProjectBriefingKeyPointPlacement.ProjectBriefSlide);
            var variant = Clone(data);
            Set(variant, nameof(ProjectBriefingPresentationData.Layout), settings.Layout);
            Set(variant, nameof(ProjectBriefingPresentationData.NarrativeMode), settings.NarrativeMode);
            Set(variant, nameof(ProjectBriefingPresentationData.PresentationMode), ProjectBriefingPresentationMode.Combined);
            Set(variant, nameof(ProjectBriefingPresentationData.StandardSlideOptions), data.StandardSlideOptions with
            {
                KeyPoints = settings.KeyPoints,
                KeyPointsOnly = settings.KeyPointsOnly
            });
            Set(variant, nameof(ProjectBriefingPresentationData.Blocks), allBlocks);
            var composition = composer.ComposeWithOutline(variant);
            await File.WriteAllBytesAsync(Path.Combine(output, $"{design}.pptx"), composition.Content);
            if (design == ProjectBriefingProjectSlideDesign.Brief)
            {
                // The builder's preview of the chart slides, to compare with the PowerPoint export.
                var preview = ProjectBriefingSlidePreviewRenderer.Render(composition.Content, DateTimeOffset.UtcNow);
                var css = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "wwwroot", "css", "pages", "project-briefing-decks.css"));
                var charts = preview.Slides.Where(slide => slide.Number is 4 or 60);
                await File.WriteAllTextAsync(
                    Path.Combine(output, "BriefPreview.html"),
                    $"<!doctype html><html><head><meta charset=\"utf-8\"><link rel=\"stylesheet\" href=\"file:///{css.Replace('\\', '/')}\"></head><body style=\"background:#888\">"
                    + string.Join("<hr>", charts.Select(slide => $"<div class=\"pbd-preview-slide\" style=\"position:relative\">{slide.Html}</div>"))
                    + "</body></html>");
            }
        }

        // The category slides alone in the light theme: each distribution on its own, then both.
        var light = Clone(data);
        Set(light, nameof(ProjectBriefingPresentationData.PresentationTheme), ProjectBriefingPresentationTheme.EditorialLight);
        Set(light, nameof(ProjectBriefingPresentationData.Blocks), new[]
        {
            new ProjectBriefingDeckBlock(ProjectBriefingDeckBlockType.ProjectCategorySummary, Enabled: true)
        });
        var single = composer.ComposeWithOutline(light).Content;
        await File.WriteAllBytesAsync(Path.Combine(output, "CategoriesLightSingle.pptx"), single);
        Set(light, nameof(ProjectBriefingPresentationData.Blocks), new[]
        {
            new ProjectBriefingDeckBlock(ProjectBriefingDeckBlockType.TechnicalCategorySummary, Enabled: true),
            new ProjectBriefingDeckBlock(ProjectBriefingDeckBlockType.ProjectCategorySummary, Enabled: true)
        });
        await File.WriteAllBytesAsync(Path.Combine(output, "CategoriesLightBoth.pptx"), composer.ComposeWithOutline(light).Content);

        // The FFC slide in the light theme, its map drawn in light colours.
        if (data.FfcGlobalFootprint is { } footprint)
        {
            var renderer = services.GetRequiredService<ProjectManagement.Services.Ffc.Presentation.IFfcPresentationMapRenderer>();
            var mapCountries = footprint.Countries
                .Select((country, index) => new ProjectManagement.Services.Ffc.Presentation.FfcPresentationCountry(
                    index + 1, country.CountryName, country.IsoCode, 0, country.ProjectCount,
                    country.InstalledUnits, country.DeliveredNotInstalledUnits, country.PlannedUnits,
                    DateTimeOffset.UtcNow, Array.Empty<ProjectManagement.Services.Ffc.Presentation.FfcPresentationRecord>()))
                .ToArray();
            Set(light, nameof(ProjectBriefingPresentationData.FfcGlobalFootprint), new ProjectBriefingFfcGlobalFootprintData
            {
                Title = footprint.Title,
                Layout = footprint.Layout,
                IncludeCountryWiseBreakdown = false,
                Summary = footprint.Summary,
                Countries = footprint.Countries,
                MaximumCountryRows = footprint.MaximumCountryRows,
                MapImage = renderer.RenderBriefingBand(
                    mapCountries,
                    ProjectBriefingSlideComposer.FfcMapBandPixelWidth,
                    ProjectBriefingSlideComposer.FfcMapBandPixelHeight,
                    dark: false),
                DataAsOnUtc = footprint.DataAsOnUtc
            });
            Set(light, nameof(ProjectBriefingPresentationData.Blocks), new[]
            {
                new ProjectBriefingDeckBlock(ProjectBriefingDeckBlockType.Cover, Enabled: true),
                new ProjectBriefingDeckBlock(ProjectBriefingDeckBlockType.FfcGlobalFootprint, Enabled: true)
            });
            await File.WriteAllBytesAsync(Path.Combine(output, "FfcLight.pptx"), composer.ComposeWithOutline(light).Content);
        }
    }

    private static ProjectBriefingPresentationData Clone(ProjectBriefingPresentationData data)
        => (ProjectBriefingPresentationData)typeof(object)
            .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(data, null)!;

    private static void Set(ProjectBriefingPresentationData data, string property, object? value)
        => typeof(ProjectBriefingPresentationData).GetProperty(property)!.SetValue(data, value);
}

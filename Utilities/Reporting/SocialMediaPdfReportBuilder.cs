using System;
using System.Collections.Generic;
using System.Globalization;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ProjectManagement.Utilities;

namespace ProjectManagement.Utilities.Reporting;

public interface ISocialMediaPdfReportBuilder
{
    byte[] Build(SocialMediaPdfReportContext context);
}

public sealed record SocialMediaPdfReportContext(
    IReadOnlyList<SocialMediaPdfReportSection> Sections,
    DateTimeOffset GeneratedAtUtc,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? PlatformFilter);

public sealed record SocialMediaPdfReportSection(
    Guid EventId,
    DateOnly DateOfEvent,
    string EventTypeName,
    string Title,
    string? Platform,
    int Reach,
    int PhotoCount,
    string? Description,
    byte[]? CoverPhoto);

public sealed class SocialMediaPdfReportBuilder : ISocialMediaPdfReportBuilder
{
    static SocialMediaPdfReportBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Build(SocialMediaPdfReportContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.Sections is null)
        {
            throw new ArgumentNullException(nameof(context.Sections));
        }

        var generatedAtIst = TimeZoneInfo.ConvertTime(context.GeneratedAtUtc, TimeZoneHelper.GetIst());
        var generatedAtText = generatedAtIst.ToString("MMMM d, yyyy 'at' HH:mm 'IST'", CultureInfo.InvariantCulture);
        var rangeText = BuildRangeText(context.StartDate, context.EndDate);
        var platformText = string.IsNullOrWhiteSpace(context.PlatformFilter)
            ? "All platforms"
            : $"Platform: {context.PlatformFilter}";

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(35);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(11).FontColor("#1F2933"));
                page.PageColor("#FFFFFF");

                page.Header().Column(header =>
                {
                    header.Item().Text(text =>
                    {
                        text.Span("SDD : Social Media Highlights")
                            .FontSize(24)
                            .SemiBold()
                            .FontColor("#0F172A");
                    });

                    header.Item().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(10).FontColor("#64748B"));
                        text.Span("Generated on ");
                        text.Span(generatedAtText)
                            .SemiBold();
                    });

                    if (!string.IsNullOrEmpty(rangeText))
                    {
                        header.Item().Text(text =>
                        {
                            text.DefaultTextStyle(style => style.FontSize(10).FontColor("#2563EB"));
                            text.Span(rangeText);
                        });
                    }

                    header.Item().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(10).FontColor("#2563EB"));
                        text.Span(platformText);
                    });
                });

                page.Content().PaddingTop(15).Column(content =>
                {
                    if (context.Sections.Count == 0)
                    {
                        content.Item().PaddingTop(50).AlignCenter().Text(text =>
                        {
                            text.Span("No social media events matched the selected filters.")
                                .FontSize(14)
                                .FontColor("#64748B");
                        });
                        return;
                    }

                    content.Spacing(20);

                    for (var index = 0; index < context.Sections.Count; index++)
                    {
                        var section = context.Sections[index];
                        var sequenceNumber = index + 1;
                        content.Item().Element(container => ComposeSection(container, section, sequenceNumber));
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(9).FontColor("#94A3B8"));
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }

    private static void ComposeSection(IContainer container, SocialMediaPdfReportSection section, int sequenceNumber)
    {
        var dateText = section.DateOfEvent.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture);
        var platformText = string.IsNullOrWhiteSpace(section.Platform) ? "(not recorded)" : section.Platform;
        var reachText = section.Reach.ToString(CultureInfo.InvariantCulture);
        var photoCountText = section.PhotoCount.ToString(CultureInfo.InvariantCulture);
        var description = section.Description;
        var coverPhoto = section.CoverPhoto;
        var typeName = section.EventTypeName;
        var title = section.Title;

        container
            .Border(1)
            .BorderColor("#E2E8F0")
            .Background("#F8FAFC")
            .Padding(20)
            .Column(column =>
            {
                column.Spacing(12);

                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(content =>
                    {
                        content.Spacing(14);

                        content.Item().Column(title =>
                        {
                            title.Item().Text(text =>
                            {
                                text.Span($"Story {sequenceNumber:00} · {typeName}")
                                    .FontSize(16)
                                    .SemiBold()
                                    .FontColor("#1E3A8A");
                            });

                            title.Item().Text(text =>
                            {
                                text.DefaultTextStyle(style => style.FontSize(11));
                                text.Span(dateText).FontColor("#475569");
                                text.Span("  •  ");
                                text.Span(title).SemiBold();
                            });
                        });

                        content.Item().Column(details =>
                        {
                            details.Spacing(6);
                            details.Item().Element(c => ComposeMetric(c, "Platform", platformText));
                            details.Item().Element(c => ComposeMetric(c, "Reach", reachText));
                            details.Item().Element(c => ComposeMetric(c, "Photos captured", photoCountText));
                        });
                    });

                    if (coverPhoto is not null && coverPhoto.Length > 0)
                    {
                        row.ConstantItem(190).Height(120).PaddingLeft(10).Element(imageContainer =>
                        {
                            imageContainer
                                .Border(1)
                                .BorderColor("#CBD5F5")
                                .Background("#FFFFFF")
                                .Padding(4)
                                .AlignCenter()
                                .AlignMiddle()
                                .Image(coverPhoto)
                                .FitArea();
                        });
                    }
                });

                if (!string.IsNullOrWhiteSpace(description))
                {
                    column.Item().Background("#FFFFFF")
                        .Border(1)
                        .BorderColor("#E2E8F0")
                        .Padding(12)
                        .Column(desc =>
                        {
                            desc.Spacing(6);
                            desc.Item().Text("Key highlights")
                                .FontSize(12)
                                .SemiBold()
                                .FontColor("#1D4ED8");
                            desc.Item().Text(description)
                                .FontSize(11)
                                .FontColor("#475569");
                        });
                }
                else
                {
                    column.Item().Text("No description was provided for this story.")
                        .FontSize(10)
                        .FontColor("#64748B");
                }
            });
    }

    private static void ComposeMetric(IContainer container, string label, string value)
    {
        container.Row(row =>
        {
            row.ConstantItem(140).Text(label)
                .FontSize(10)
                .FontColor("#64748B");

            row.RelativeItem().Text(text =>
            {
                text.Span(value)
                    .FontSize(11)
                    .SemiBold()
                    .FontColor("#1F2933");
            });
        });
    }

    private static string? BuildRangeText(DateOnly? start, DateOnly? end)
    {
        if (!start.HasValue && !end.HasValue)
        {
            return null;
        }

        if (start.HasValue && end.HasValue)
        {
            return $"Reporting period: {start.Value:dd MMM yyyy} – {end.Value:dd MMM yyyy}";
        }

        if (start.HasValue)
        {
            return $"Stories from {start.Value:dd MMM yyyy}";
        }

        return $"Stories until {end!.Value:dd MMM yyyy}";
    }
}

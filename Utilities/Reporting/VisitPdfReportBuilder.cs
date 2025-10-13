using System;
using System.Collections.Generic;
using System.Globalization;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ProjectManagement.Utilities.Reporting;

public interface IVisitPdfReportBuilder
{
    byte[] Build(VisitPdfReportContext context);
}

public sealed record VisitPdfReportContext(
    IReadOnlyList<VisitPdfReportSection> Sections,
    DateTimeOffset GeneratedAtUtc,
    DateOnly? StartDate,
    DateOnly? EndDate);

public sealed record VisitPdfReportSection(
    Guid VisitId,
    DateOnly DateOfVisit,
    string VisitTypeName,
    string VisitorName,
    int Strength,
    int PhotoCount,
    string? Remarks,
    byte[]? CoverPhoto);

public sealed class VisitPdfReportBuilder : IVisitPdfReportBuilder
{
    static VisitPdfReportBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Build(VisitPdfReportContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.Sections is null)
        {
            throw new ArgumentNullException(nameof(context.Sections));
        }

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
                        text.Span("Visit Experience Report")
                            .FontSize(24)
                            .SemiBold()
                            .FontColor("#1D4ED8");
                    });

                    header.Item().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(10).FontColor("#64748B"));
                        text.Span("Generated on ");
                        text.Span(context.GeneratedAtUtc.ToString("MMMM d, yyyy 'at' HH:mm 'UTC'", CultureInfo.InvariantCulture))
                            .SemiBold();
                    });

                    var rangeText = BuildRangeText(context.StartDate, context.EndDate);
                    if (!string.IsNullOrEmpty(rangeText))
                    {
                        header.Item().Text(text =>
                        {
                            text.DefaultTextStyle(style => style.FontSize(10).FontColor("#2563EB"));
                            text.Span(rangeText);
                        });
                    }
                });

                page.Content().PaddingTop(15).Column(content =>
                {
                    if (context.Sections.Count == 0)
                    {
                        content.Item().PaddingTop(50).AlignCenter().Text(text =>
                        {
                            text.Span("No visits matched the selected filters.")
                                .FontSize(14)
                                .FontColor("#64748B");
                        });
                        return;
                    }

                    content.Spacing(20);

                    for (var index = 0; index < context.Sections.Count; index++)
                    {
                        var section = context.Sections[index];
                        var sectionCopy = section;
                        var sequenceNumber = index + 1;

                        content.Item().Element(container => ComposeVisit(container, sectionCopy, sequenceNumber));
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

    private static void ComposeVisit(IContainer container, VisitPdfReportSection section, int sequenceNumber)
    {
        var visitTypeName = section.VisitTypeName;
        var dateOfVisit = section.DateOfVisit;
        var visitorName = section.VisitorName;
        var strengthText = section.Strength.ToString(CultureInfo.InvariantCulture);
        var photoCountText = section.PhotoCount.ToString(CultureInfo.InvariantCulture);
        var remarksText = section.Remarks;
        var coverPhoto = section.CoverPhoto;

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
                    row.RelativeItem().Column(title =>
                    {
                        title.Item().Text(text =>
                        {
                            text.Span($"Visit {sequenceNumber:00} · {visitTypeName}")
                                .FontSize(16)
                                .SemiBold()
                                .FontColor("#1E3A8A");
                        });

                        title.Item().Text(text =>
                        {
                            text.DefaultTextStyle(style => style.FontSize(11));
                            text.Span(dateOfVisit.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture))
                                .FontColor("#475569");
                            text.Span("  •  ");
                            text.Span(visitorName)
                                .SemiBold();
                        });
                    });

                    if (coverPhoto is not null && coverPhoto.Length > 0)
                    {
                        row.ConstantItem(190).Height(120).PaddingLeft(10).Element(imageContainer =>
                        {
                            imageContainer.Border(1);
                            imageContainer.BorderColor("#CBD5F5");
                            imageContainer.Background("#FFFFFF");
                            imageContainer.Padding(4);

                            imageContainer
                                .AlignCenter()
                                .AlignMiddle()
                                .Image(coverPhoto!)
                                .FitArea();
                        });
                    }
                });

                column.Item().Row(row =>
                {
                    row.Spacing(12);

                    row.RelativeItem().Column(details =>
                    {
                        details.Spacing(6);

                        details.Item().Element(c => ComposeMetric(c, "Visitor", visitorName));
                        details.Item().Element(c => ComposeMetric(c, "Team strength", strengthText));
                        details.Item().Element(c => ComposeMetric(c, "Photos captured", photoCountText));
                    });
                });

                if (!string.IsNullOrWhiteSpace(remarksText))
                {
                    column.Item().Background("#FFFFFF")
                        .Border(1)
                        .BorderColor("#E2E8F0")
                        .Padding(12)
                        .Column(remarks =>
                        {
                            remarks.Spacing(6);
                            remarks.Item().Text("Highlights")
                                .FontSize(12)
                                .SemiBold()
                                .FontColor("#1D4ED8");
                            remarks.Item().Text(remarksText)
                                .FontSize(11)
                                .FontColor("#475569");
                        });
                }
                else
                {
                    column.Item().Text("No remarks were recorded for this visit.")
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
            return $"Visits from {start.Value:dd MMM yyyy}";
        }

        return $"Visits until {end!.Value:dd MMM yyyy}";
    }
}

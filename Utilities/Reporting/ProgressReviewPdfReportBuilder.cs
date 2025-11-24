using System;
using System.Globalization;
using System.Linq;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ProjectManagement.Services.Reports.ProgressReview;
using ProjectManagement.Utilities;

namespace ProjectManagement.Utilities.Reporting;

// =========================================================
// SECTION: Contracts and context models
// =========================================================
public interface IProgressReviewPdfReportBuilder
{
    byte[] Build(ProgressReviewPdfReportContext context);
}

public sealed record ProgressReviewPdfReportContext(
    ProgressReviewVm Report,
    DateTimeOffset GeneratedAtUtc,
    DateOnly RangeFrom,
    DateOnly RangeTo);

// =========================================================
// SECTION: Builder implementation
// =========================================================
public sealed class ProgressReviewPdfReportBuilder : IProgressReviewPdfReportBuilder
{
    static ProgressReviewPdfReportBuilder()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Build(ProgressReviewPdfReportContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.Report is null)
        {
            throw new ArgumentNullException(nameof(context.Report));
        }

        var generatedAtIst = TimeZoneInfo.ConvertTime(context.GeneratedAtUtc, TimeZoneHelper.GetIst());
        var generatedAtText = generatedAtIst.ToString("MMMM d, yyyy 'at' HH:mm 'IST'", CultureInfo.InvariantCulture);
        var rangeText = $"{context.RangeFrom:dd MMM yyyy} – {context.RangeTo:dd MMM yyyy}";

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(35);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(11).FontColor("#1F2933"));
                page.PageColor(Colors.White);

                page.Header().Column(header =>
                {
                    header.Item().Text(text =>
                    {
                        text.Span("SDD : Progress Review")
                            .FontSize(24)
                            .SemiBold()
                            .FontColor("#1D4ED8");
                    });

                    header.Item().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(10).FontColor("#64748B"));
                        text.Span("Generated on ");
                        text.Span(generatedAtText).SemiBold();
                    });

                    header.Item().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(10).FontColor("#2563EB"));
                        text.Span($"Reporting period: {rangeText}");
                    });
                });

                page.Content().PaddingTop(15).Column(content =>
                {
                    content.Spacing(16);

                    content.Item().Element(c => ComposeOverview(c, context.Report));
                    content.Item().Element(c => ComposeProjects(c, context.Report));
                    content.Item().Element(c => ComposeVisits(c, context.Report));
                    content.Item().Element(c => ComposeSocialMedia(c, context.Report));
                    content.Item().Element(c => ComposeTraining(c, context.Report));
                    content.Item().Element(c => ComposeProliferationAndFfc(c, context.Report));
                    content.Item().Element(c => ComposeMisc(c, context.Report));
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

    // =========================================================
    // SECTION: Composition helpers
    // =========================================================
    private static void ComposeOverview(IContainer container, ProgressReviewVm report)
    {
        var totals = report.Totals;

        container
            .Border(1)
            .BorderColor("#E2E8F0")
            .Background("#F8FAFC")
            .Padding(16)
            .Column(column =>
            {
                column.Spacing(8);

                column.Item().Text(text =>
                {
                    text.Span("Overview")
                        .FontSize(16)
                        .SemiBold()
                        .FontColor("#0F172A");
                });

                column.Item().Row(row =>
                {
                    row.RelativeItem().Element(c => ComposeMetric(c, "Projects moved", totals.ProjectsMoved.ToString(CultureInfo.InvariantCulture)));
                    row.RelativeItem().Element(c => ComposeMetric(c, "Remarks captured", totals.ProjectsWithRemarks.ToString(CultureInfo.InvariantCulture)));
                    row.RelativeItem().Element(c => ComposeMetric(c, "Visits", totals.VisitsCount.ToString(CultureInfo.InvariantCulture)));
                    row.RelativeItem().Element(c => ComposeMetric(c, "Social posts", totals.SocialPostsCount.ToString(CultureInfo.InvariantCulture)));
                });

                column.Item().Row(row =>
                {
                    row.RelativeItem().Element(c => ComposeMetric(c, "TOT changes", totals.TotChangesCount.ToString(CultureInfo.InvariantCulture)));
                    row.RelativeItem().Element(c => ComposeMetric(c, "IPR updates", totals.IprChangesCount.ToString(CultureInfo.InvariantCulture)));
                    row.RelativeItem().Element(c => ComposeMetric(c, "Training (Sim)", totals.SimulatorTrainees.ToString(CultureInfo.InvariantCulture)));
                    row.RelativeItem().Element(c => ComposeMetric(c, "Training (Drone)", totals.DroneTrainees.ToString(CultureInfo.InvariantCulture)));
                });

                column.Item().Row(row =>
                {
                    row.RelativeItem().Element(c => ComposeMetric(c, "Proliferation", totals.ProliferationsCount.ToString(CultureInfo.InvariantCulture)));
                    row.RelativeItem().Element(c => ComposeMetric(c, "FFC items", totals.FfcItemsChanged.ToString(CultureInfo.InvariantCulture)));
                    row.RelativeItem().Element(c => ComposeMetric(c, "Misc activities", totals.MiscCount.ToString(CultureInfo.InvariantCulture)));
                    row.RelativeItem().Element(c => ComposeMetric(c, "Non movers", totals.NonMovers.ToString(CultureInfo.InvariantCulture)));
                });
            });
    }

    private static void ComposeProjects(IContainer container, ProgressReviewVm report)
    {
        var projects = report.Projects;

        container
            .Border(1)
            .BorderColor("#E2E8F0")
            .Padding(16)
            .Column(column =>
            {
                column.Spacing(10);

                column.Item().Text(text =>
                {
                    text.Span("Ongoing projects")
                        .FontSize(15)
                        .SemiBold()
                        .FontColor("#1E3A8A");
                    text.Span("  ·  Stage progress and remarks")
                        .FontSize(11)
                        .FontColor("#475569");
                });

                column.Item().Column(highlights =>
                {
                    highlights.Spacing(6);

                    AppendHighlightBlock(highlights, "Front runners", projects.FrontRunners.Select(r =>
                        $"{r.ProjectName} · {r.FromStatus ?? ""} → {r.ToStatus ?? ""} on {r.ChangeDate:dd MMM}").ToList());

                    AppendHighlightBlock(highlights, "Remark-only updates", projects.WorkInProgress.Select(r =>
                        $"{r.ProjectName} · {r.RemarkSummary.LatestRemarkSummary ?? "No summary"}").ToList());

                    AppendHighlightBlock(highlights, "Non movers", projects.NonMovers.Select(r =>
                        $"{r.ProjectName} · {r.StageName} · {r.DaysSinceActivity} days since activity").ToList());
                });

                column.Item().Column(groups =>
                {
                    groups.Spacing(8);
                    groups.Item().Text("Category breakdown")
                        .FontSize(12)
                        .SemiBold()
                        .FontColor("#0F172A");

                    if (!projects.CategoryGroups.Any())
                    {
                        groups.Item().Text("No qualifying projects in this range.")
                            .FontColor("#94A3B8")
                            .FontSize(10);
                        return;
                    }

                    foreach (var group in projects.CategoryGroups)
                    {
                        var summary = $"{group.Projects.Count} projects";
                        groups.Item().Element(c => ComposeCategoryGroup(c, group.CategoryName, summary, group.Projects));
                    }
                });
            });
    }

    private static void ComposeVisits(IContainer container, ProgressReviewVm report)
    {
        var visits = report.Visits;

        container
            .Border(1)
            .BorderColor("#E2E8F0")
            .Padding(16)
            .Column(column =>
            {
                column.Spacing(8);
                column.Item().Text("Visits and outreach")
                    .FontSize(15)
                    .SemiBold()
                    .FontColor("#1E3A8A");

                if (!visits.Items.Any())
                {
                    column.Item().Text("No visits recorded in this period.")
                        .FontColor("#94A3B8");
                    return;
                }

                foreach (var visit in visits.Items.Take(10))
                {
                    column.Item().Element(c => ComposeVisitRow(c, visit));
                }

                if (visits.Items.Count > 10)
                {
                    column.Item().Text($"Showing top 10 of {visits.Items.Count} visits.")
                        .FontSize(9)
                        .FontColor("#94A3B8");
                }
            });
    }

    private static void ComposeSocialMedia(IContainer container, ProgressReviewVm report)
    {
        var social = report.SocialMedia;

        container
            .Border(1)
            .BorderColor("#E2E8F0")
            .Padding(16)
            .Column(column =>
            {
                column.Spacing(8);
                column.Item().Text("Social media highlights")
                    .FontSize(15)
                    .SemiBold()
                    .FontColor("#1E3A8A");

                if (!social.Items.Any())
                {
                    column.Item().Text("No social media highlights in this period.")
                        .FontColor("#94A3B8");
                    return;
                }

                foreach (var post in social.Items.Take(10))
                {
                    column.Item().Element(c => ComposeSocialRow(c, post));
                }

                if (social.Items.Count > 10)
                {
                    column.Item().Text($"Showing top 10 of {social.Items.Count} highlights.")
                        .FontSize(9)
                        .FontColor("#94A3B8");
                }
            });
    }

    private static void ComposeTraining(IContainer container, ProgressReviewVm report)
    {
        var training = report.Training;

        container
            .Border(1)
            .BorderColor("#E2E8F0")
            .Padding(16)
            .Column(column =>
            {
                column.Spacing(10);
                column.Item().Text("Training sessions")
                    .FontSize(15)
                    .SemiBold()
                    .FontColor("#1E3A8A");

                column.Item().Element(c => ComposeTrainingBlock(c, "Simulator training", training.Simulator));
                column.Item().Element(c => ComposeTrainingBlock(c, "Drone training", training.Drone));
            });
    }

    private static void ComposeProliferationAndFfc(IContainer container, ProgressReviewVm report)
    {
        var proliferation = report.Proliferation;
        var ffcGroups = report.FfcDetailedIncompleteGroups;

        container
            .Border(1)
            .BorderColor("#E2E8F0")
            .Padding(16)
            .Column(column =>
            {
                column.Spacing(8);
                column.Item().Text("Proliferation & FFC simulators")
                    .FontSize(15)
                    .SemiBold()
                    .FontColor("#1E3A8A");

                column.Item().Column(prolo =>
                {
                    prolo.Spacing(6);
                    prolo.Item().Text("Proliferation movements")
                        .FontSize(12)
                        .SemiBold();

                    if (!proliferation.Rows.Any())
                    {
                        prolo.Item().Text("No proliferation entries for this range.")
                            .FontColor("#94A3B8");
                    }
                    else
                    {
                        foreach (var row in proliferation.Rows.Take(8))
                        {
                            var line = $"{row.Date:dd MMM yyyy} · {row.ProjectName} · {row.UnitOrCountry} · Qty {row.Quantity} · {row.Source}";
                            prolo.Item().Text(line);
                        }

                        if (proliferation.Rows.Count > 8)
                        {
                            prolo.Item().Text($"Showing top 8 of {proliferation.Rows.Count} entries.")
                                .FontSize(9)
                                .FontColor("#94A3B8");
                        }
                    }
                });

                column.Item().Column(ffc =>
                {
                    ffc.Spacing(6);
                    ffc.Item().Text("FFC country-year progress (incomplete)")
                        .FontSize(12)
                        .SemiBold();

                    if (ffcGroups is null || ffcGroups.Length == 0)
                    {
                        ffc.Item().Text("No in-progress FFC records right now.")
                            .FontColor("#94A3B8");
                        return;
                    }

                    foreach (var group in ffcGroups)
                    {
                        ffc.Item().Element(c => ComposeFfcGroup(c, group));
                    }
                });
            });
    }

    private static void ComposeMisc(IContainer container, ProgressReviewVm report)
    {
        var misc = report.Misc;

        container
            .Border(1)
            .BorderColor("#E2E8F0")
            .Padding(16)
            .Column(column =>
            {
                column.Spacing(8);
                column.Item().Text("Miscellaneous activities")
                    .FontSize(15)
                    .SemiBold()
                    .FontColor("#1E3A8A");

                if (!misc.Rows.Any())
                {
                    column.Item().Text("No activities captured in this range.")
                        .FontColor("#94A3B8");
                    return;
                }

                foreach (var item in misc.Rows.Take(8))
                {
                    var summary = string.IsNullOrWhiteSpace(item.Summary) ? "No summary provided." : item.Summary;
                    var location = string.IsNullOrWhiteSpace(item.Location) ? "(location not recorded)" : item.Location;
                    column.Item().Text($"{item.Date:dd MMM yyyy} · {item.Title} · {location} · {summary}");
                }

                if (misc.Rows.Count > 8)
                {
                    column.Item().Text($"Showing top 8 of {misc.Rows.Count} activities.")
                        .FontSize(9)
                        .FontColor("#94A3B8");
                }
            });
    }

    private static void ComposeCategoryGroup(IContainer container, string categoryName, string summary, System.Collections.Generic.IReadOnlyList<ProjectProgressRowVm> projects)
    {
        container
            .Border(1)
            .BorderColor("#CBD5E1")
            .Background("#FFFFFF")
            .Padding(12)
            .Column(column =>
            {
                column.Spacing(6);

                column.Item().Row(row =>
                {
                    row.RelativeItem().Text(categoryName)
                        .FontSize(12)
                        .SemiBold();
                    row.ConstantItem(120).AlignRight().Text(summary)
                        .FontSize(10)
                        .FontColor("#64748B");
                });

                foreach (var project in projects.Take(5))
                {
                    var remark = project.RemarkSummary.LatestRemarkSummary ?? "No remarks";
                    var currentStage = project.PresentStage.CurrentStageName ?? "(stage not recorded)";
                    column.Item().Text($"{project.ProjectName} · {currentStage} · {remark}")
                        .FontSize(10);
                }

                if (projects.Count > 5)
                {
                    column.Item().Text($"+ {projects.Count - 5} more projects")
                        .FontSize(9)
                        .FontColor("#94A3B8");
                }
            });
    }

    private static void ComposeVisitRow(IContainer container, VisitSummaryVm visit)
    {
        var remark = string.IsNullOrWhiteSpace(visit.Remarks) ? "No remarks recorded." : visit.Remarks;
        var detail = $"{visit.Date:dd MMM yyyy} · {visit.VisitType} · {visit.VisitorName} · Team {visit.Strength}";

        container
            .Border(1)
            .BorderColor("#CBD5E1")
            .Background("#F8FAFC")
            .Padding(10)
            .Column(column =>
            {
                column.Item().Text(detail)
                    .SemiBold();
                column.Item().Text(remark)
                    .FontSize(10)
                    .FontColor("#475569");
            });
    }

    private static void ComposeSocialRow(IContainer container, SocialMediaPostVm post)
    {
        var platform = string.IsNullOrWhiteSpace(post.Platform) ? "(platform not recorded)" : post.Platform;
        var description = string.IsNullOrWhiteSpace(post.Description) ? "No description provided." : post.Description;
        var detail = $"{post.Date:dd MMM yyyy} · {platform}";

        container
            .Border(1)
            .BorderColor("#CBD5E1")
            .Background("#F8FAFC")
            .Padding(10)
            .Column(column =>
            {
                column.Item().Text(text =>
                {
                    text.Span(post.Title).SemiBold();
                    text.Span("  ·  ");
                    text.Span(detail).FontColor("#475569");
                });

                column.Item().Text(description)
                    .FontSize(10)
                    .FontColor("#475569");
            });
    }

    private static void ComposeTrainingBlock(IContainer container, string title, TrainingBlockVm block)
    {
        container
            .Border(1)
            .BorderColor("#CBD5E1")
            .Background("#FFFFFF")
            .Padding(12)
            .Column(column =>
            {
                column.Spacing(6);
                column.Item().Text(text =>
                {
                    text.Span(title).SemiBold();
                    text.Span($" · {block.Rows.Count} sessions · {block.TotalPersons} participants")
                        .FontSize(10)
                        .FontColor("#475569");
                });

                if (!block.Rows.Any())
                {
                    column.Item().Text("No sessions recorded.")
                        .FontColor("#94A3B8");
                    return;
                }

                foreach (var session in block.Rows.Take(6))
                {
                    var line = $"{session.Date:dd MMM yyyy} · {session.Title} · {session.UnitOrOrg} · {session.Persons} persons";
                    column.Item().Text(line)
                        .FontSize(10);
                }

                if (block.Rows.Count > 6)
                {
                    column.Item().Text($"+ {block.Rows.Count - 6} more sessions")
                        .FontSize(9)
                        .FontColor("#94A3B8");
                }
            });
    }

    private static void ComposeFfcGroup(IContainer container, ProjectManagement.Services.Ffc.FfcDetailedGroupVm group)
    {
        container
            .Border(1)
            .BorderColor("#CBD5E1")
            .Background("#FFFFFF")
            .Padding(12)
            .Column(column =>
            {
                column.Spacing(6);
                column.Item().Text(text =>
                {
                    text.Span($"{group.CountryName} ({group.CountryCode}) {group.Year}")
                        .SemiBold();
                    if (!string.IsNullOrWhiteSpace(group.OverallRemarks))
                    {
                        text.Span(" · ");
                        text.Span(group.OverallRemarks)
                            .FontSize(10)
                            .FontColor("#475569");
                    }
                });

                foreach (var row in group.Rows.Take(4))
                {
                    var progress = string.IsNullOrWhiteSpace(row.Progress) ? "Progress not recorded" : row.Progress;
                    var line = $"{row.Serial}. {row.ProjectName} · Qty {row.Quantity} · {row.Status} · {progress}";
                    column.Item().Text(line)
                        .FontSize(10);
                }

                if (group.Rows.Count > 4)
                {
                    column.Item().Text($"+ {group.Rows.Count - 4} more line items")
                        .FontSize(9)
                        .FontColor("#94A3B8");
                }
            });
    }

    private static void ComposeMetric(IContainer container, string title, string value)
    {
        container.Column(column =>
        {
            column.Item().Text(title)
                .FontSize(9)
                .FontColor("#64748B");
            column.Item().Text(value)
                .FontSize(13)
                .SemiBold()
                .FontColor("#111827");
        });
    }

    private static void AppendHighlightBlock(ColumnDescriptor column, string title, System.Collections.Generic.IReadOnlyCollection<string> rows)
    {
        column.Item().Border(1).BorderColor("#CBD5E1").Background("#FFFFFF").Padding(10).Column(block =>
        {
            block.Spacing(4);
            block.Item().Text(title)
                .SemiBold();

            if (rows.Count == 0)
            {
                block.Item().Text("None recorded in this period.")
                    .FontSize(10)
                    .FontColor("#94A3B8");
                return;
            }

            foreach (var row in rows.Take(4))
            {
                block.Item().Text(row)
                    .FontSize(10)
                    .FontColor("#0F172A");
            }

            if (rows.Count > 4)
            {
                block.Item().Text($"+ {rows.Count - 4} more entries")
                    .FontSize(9)
                    .FontColor("#94A3B8");
            }
        });
    }
}

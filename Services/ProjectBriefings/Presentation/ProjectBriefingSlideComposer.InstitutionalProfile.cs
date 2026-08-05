using System.Globalization;
using ProjectManagement.Models.ProjectBriefings;
using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer
{
    private static void RenderInstitutionalProfile(
        SlideCanvas canvas,
        ProjectBriefingInstitutionalProfileData profile)
    {
        var theme = canvas.Theme;
        canvas.AddRect(0, 0, SlideWidth, SlideHeight, theme.Canvas, name: "SDD profile canvas");
        canvas.AddRect(0, 0, SlideWidth, .10, theme.HeaderAccent, name: "SDD profile top accent");
        canvas.AddBrandingImages(HeaderVariant.Standard);

        var titleX = canvas.ShowBranding ? 1.28 : .62;
        var titleWidth = canvas.ShowBranding ? 10.78 : 12.10;
        canvas.AddRoundedRect(
            titleX,
            .22,
            titleWidth,
            .62,
            theme.HeaderAccent,
            theme.HeaderAccent,
            .04,
            "SDD profile title band");
        canvas.AddText(
            titleX + .24,
            .30,
            titleWidth - .48,
            .43,
            Truncate(profile.Title, 110),
            23.5,
            theme.TextOnAccent,
            true,
            "ctr",
            name: "SDD profile title");

        var hasHistory = profile.HistoryMilestones.Count > 0;
        var hasCitation = profile.UnitCitationCount.HasValue
            && !string.IsNullOrWhiteSpace(profile.UnitCitationLabel);
        var cardsTop = hasHistory ? 2.48 : 1.10;
        var dataLineY = hasCitation ? 6.59 : 6.75;
        var cardsBottom = hasCitation ? 6.24 : 6.52;

        if (hasHistory)
        {
            RenderInstitutionalHistory(canvas, profile.HistoryMilestones);
        }

        RenderInstitutionalModules(
            canvas,
            profile.Modules,
            cardsTop,
            cardsBottom - cardsTop);

        if (hasCitation)
        {
            RenderInstitutionalCitation(
                canvas,
                profile.UnitCitationLabel!,
                profile.UnitCitationCount!.Value);
        }

        var dataAsOn = profile.DataAsOnUtc.ToUniversalTime().ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        canvas.AddText(
            .76,
            dataLineY,
            11.82,
            .18,
            $"Data as on {dataAsOn} · Source: PRISM ERP",
            7.8,
            theme.TextMuted,
            false,
            "ctr",
            name: "SDD profile data source");
    }

    private static void RenderInstitutionalHistory(
        SlideCanvas canvas,
        IReadOnlyList<ProjectBriefingInstitutionalHistoryMilestone> history)
    {
        var items = history
            .OrderBy(item => item.Year)
            .ThenBy(item => item.Text, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
        if (items.Length == 0) return;

        const double left = .82;
        const double right = 12.52;
        const double lineY = 1.62;
        var span = right - left;
        canvas.AddLine(left, lineY, right, lineY, canvas.Theme.Divider, 1.05);

        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            var x = items.Length == 1
                ? (left + right) / 2
                : left + ((span * index) / (items.Length - 1));
            var labelWidth = items.Length >= 7 ? 1.43 : 1.66;
            var labelX = Math.Clamp(x - (labelWidth / 2), .56, 12.77 - labelWidth);
            var above = index % 2 == 0;
            var textY = above ? .98 : 1.73;
            var yearY = above ? .91 : 1.68;

            canvas.AddText(
                labelX,
                yearY,
                labelWidth,
                .24,
                item.Year.ToString(CultureInfo.InvariantCulture),
                10.8,
                canvas.Theme.HeaderAccent,
                true,
                "ctr",
                name: $"SDD milestone year {item.Year}");
            canvas.AddText(
                labelX,
                textY + .22,
                labelWidth,
                .43,
                Truncate(item.Text, 48),
                items.Length >= 7 ? 8.0 : 8.6,
                canvas.Theme.TextPrimary,
                true,
                "ctr",
                verticalAnchor: "t",
                name: $"SDD milestone {item.Year}");
            canvas.AddText(
                x - .10,
                lineY - .12,
                .20,
                .24,
                "●",
                10.5,
                canvas.Theme.Warning,
                true,
                "ctr",
                name: $"SDD milestone marker {item.Year}");
        }
    }

    private static void RenderInstitutionalModules(
        SlideCanvas canvas,
        IReadOnlyList<ProjectBriefingInstitutionalModuleData> modules,
        double top,
        double height)
    {
        if (modules.Count == 0)
        {
            // A history-only profile is valid. Do not expose configuration guidance in the exported deck.
            return;
        }

        var visible = modules.Take(5).ToArray();
        const double left = .60;
        const double right = 12.73;
        const double gap = .12;
        var cardWidth = (right - left - ((visible.Length - 1) * gap)) / visible.Length;

        for (var index = 0; index < visible.Length; index++)
        {
            var module = visible[index];
            var x = left + (index * (cardWidth + gap));
            RenderInstitutionalModuleCard(canvas, module, x, top, cardWidth, height);
        }
    }

    private static void RenderInstitutionalModuleCard(
        SlideCanvas canvas,
        ProjectBriefingInstitutionalModuleData module,
        double x,
        double y,
        double width,
        double height)
    {
        var accent = InstitutionalModuleAccent(canvas.Theme, module.Module);
        var soft = InstitutionalModuleSoftFill(canvas.Theme, module.Module);
        canvas.AddRoundedRect(x, y, width, height, canvas.Theme.Surface, canvas.Theme.Border, .055, $"{module.Title} module");
        canvas.AddRect(x, y, width, .075, accent, name: $"{module.Title} accent");
        canvas.AddRect(x, y + .075, width, .62, soft, name: $"{module.Title} header fill");

        canvas.AddText(
            x + .16,
            y + .15,
            width - .32,
            .42,
            Truncate(module.Title, 48),
            width < 2.25 ? 9.0 : 9.7,
            accent,
            true,
            "ctr",
            name: $"{module.Title} heading");

        var cursorY = y + .78;
        if (!string.IsNullOrWhiteSpace(module.Headline))
        {
            canvas.AddText(
                x + .14,
                cursorY,
                width - .28,
                .48,
                module.Headline!,
                width < 2.25 ? 19.0 : 21.0,
                canvas.Theme.TextPrimary,
                true,
                "ctr",
                name: $"{module.Title} headline");
            cursorY += .53;
        }
        else
        {
            cursorY += .12;
        }

        var highlightReserve = string.IsNullOrWhiteSpace(module.Highlight) ? 0d : .48;
        var availableRowsHeight = Math.Max(.72, (y + height - .16) - cursorY - highlightReserve);
        var rowCount = Math.Max(1, module.Rows.Count);
        var rowHeight = Math.Clamp(availableRowsHeight / rowCount, .25, .41);
        var maximumVisibleRows = Math.Max(1, (int)Math.Floor(availableRowsHeight / rowHeight));
        var rows = module.Rows.Take(maximumVisibleRows).ToArray();
        var labelWidth = module.Module == ProjectBriefingInstitutionalProfileModule.Partnerships
            ? width - .34
            : Math.Max(.82, width - .72);

        foreach (var row in rows)
        {
            var label = module.Module == ProjectBriefingInstitutionalProfileModule.Partnerships
                ? $"• {row.Label}"
                : row.Label;
            canvas.AddText(
                x + .16,
                cursorY,
                labelWidth,
                rowHeight,
                Truncate(label, width < 2.25 ? 28 : 36),
                width < 2.25 ? 7.9 : 8.5,
                canvas.Theme.TextSecondary,
                false,
                "l",
                name: $"{module.Title} row label");
            if (!string.IsNullOrWhiteSpace(row.Value))
            {
                canvas.AddText(
                    x + width - .58,
                    cursorY,
                    .42,
                    rowHeight,
                    row.Value,
                    width < 2.25 ? 8.0 : 8.7,
                    canvas.Theme.TextPrimary,
                    true,
                    "r",
                    name: $"{module.Title} row value");
            }
            cursorY += rowHeight;
        }

        if (!string.IsNullOrWhiteSpace(module.Highlight))
        {
            var highlightY = y + height - .49;
            canvas.AddRoundedRect(
                x + .12,
                highlightY,
                width - .24,
                .34,
                soft,
                null,
                .035,
                $"{module.Title} highlight");
            canvas.AddText(
                x + .20,
                highlightY + .03,
                width - .40,
                .28,
                Truncate(module.Highlight, width < 2.25 ? 54 : 68),
                width < 2.25 ? 7.1 : 7.7,
                accent,
                true,
                "l",
                name: $"{module.Title} highlight text");
        }
    }

    private static void RenderInstitutionalCitation(
        SlideCanvas canvas,
        string label,
        int count)
    {
        canvas.AddRoundedRect(
            4.35,
            6.33,
            4.63,
            .38,
            canvas.Theme.SurfaceRaised,
            canvas.Theme.HeaderAccent,
            .04,
            "Institutional recognition strip");
        canvas.AddText(
            4.56,
            6.385,
            4.21,
            .25,
            $"{Truncate(label, 58)} — {count:00}",
            11.2,
            canvas.Theme.TextPrimary,
            true,
            "ctr",
            name: "Institutional recognition text");
    }

    private static string InstitutionalModuleAccent(
        ProjectBriefingThemeDefinition theme,
        ProjectBriefingInstitutionalProfileModule module)
        => module switch
        {
            ProjectBriefingInstitutionalProfileModule.ProjectsDeveloped => theme.SecondaryAccent,
            ProjectBriefingInstitutionalProfileModule.Proliferation => theme.Accent,
            ProjectBriefingInstitutionalProfileModule.TrainingSupport => theme.HeaderAccent,
            ProjectBriefingInstitutionalProfileModule.IntellectualProperty => theme.Warning,
            ProjectBriefingInstitutionalProfileModule.Partnerships => theme.Positive,
            _ => theme.Accent
        };

    private static string InstitutionalModuleSoftFill(
        ProjectBriefingThemeDefinition theme,
        ProjectBriefingInstitutionalProfileModule module)
        => module switch
        {
            ProjectBriefingInstitutionalProfileModule.ProjectsDeveloped => theme.SecondaryAccentSoft,
            ProjectBriefingInstitutionalProfileModule.Proliferation => theme.AccentSoft,
            ProjectBriefingInstitutionalProfileModule.TrainingSupport => theme.CriticalSoft,
            ProjectBriefingInstitutionalProfileModule.IntellectualProperty => theme.WarningSoft,
            ProjectBriefingInstitutionalProfileModule.Partnerships => theme.PositiveSoft,
            _ => theme.SurfaceMuted
        };
}

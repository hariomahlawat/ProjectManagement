using ProjectManagement.Services.ProjectBriefings;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

public sealed partial class ProjectBriefingSlideComposer
{
    private static void RenderStageIconBadge(
        SlideCanvas canvas,
        ProjectBriefingSummaryPoint point,
        double x,
        double y,
        double size,
        string badgeFill,
        string iconColor,
        double fallbackFontSize,
        string name)
    {
        canvas.AddEllipse(x, y, size, size, badgeFill, null, name: name);

        var rendered = point.Order switch
        {
            ProjectBriefingStageOrder.Development => RenderDevelopmentStageIcon(canvas, x, y, size, badgeFill, iconColor, name),
            ProjectBriefingStageOrder.TechnicalEvaluation => RenderTechnicalEvaluationStageIcon(canvas, x, y, size, badgeFill, iconColor, name),
            ProjectBriefingStageOrder.BiddingTendering => RenderBiddingStageIcon(canvas, x, y, size, badgeFill, iconColor, name),
            ProjectBriefingStageOrder.AcceptanceOfNecessity => RenderApprovedDocumentStageIcon(canvas, x, y, size, badgeFill, iconColor, name),
            ProjectBriefingStageOrder.SowVetting => RenderDocumentReviewStageIcon(canvas, x, y, size, badgeFill, iconColor, name),
            ProjectBriefingStageOrder.InPrincipleApproval => RenderApprovalSealStageIcon(canvas, x, y, size, badgeFill, iconColor, name),
            ProjectBriefingStageOrder.FeasibilityStudy => RenderFeasibilityStageIcon(canvas, x, y, size, badgeFill, iconColor, name),
            ProjectBriefingStageOrder.Benchmarking => RenderBenchmarkStageIcon(canvas, x, y, size, badgeFill, iconColor, name),
            ProjectBriefingStageOrder.CommercialBidOpening => RenderCommercialOpeningStageIcon(canvas, x, y, size, badgeFill, iconColor, name),
            ProjectBriefingStageOrder.Pnc => RenderNegotiationStageIcon(canvas, x, y, size, badgeFill, iconColor, name),
            ProjectBriefingStageOrder.EasApproval => RenderApprovedDocumentStageIcon(canvas, x, y, size, badgeFill, iconColor, name),
            ProjectBriefingStageOrder.SupplyOrder => RenderSupplyOrderStageIcon(canvas, x, y, size, badgeFill, iconColor, name),
            ProjectBriefingStageOrder.AcceptanceTesting => RenderAcceptanceTestingStageIcon(canvas, x, y, size, badgeFill, iconColor, name),
            ProjectBriefingStageOrder.Payment => RenderPaymentStageIcon(canvas, x, y, size, badgeFill, iconColor, name),
            ProjectBriefingStageOrder.TransferOfTechnology => RenderTransferStageIcon(canvas, x, y, size, badgeFill, iconColor, name),
            _ => false
        };

        if (!rendered)
        {
            AddOpticallyCenteredBadgeText(
                canvas,
                x,
                y,
                size,
                ResolveStageBadge(point),
                fallbackFontSize,
                iconColor,
                $"{name} text");
        }
    }

    private static void RenderStageAbbreviationBadge(
        SlideCanvas canvas,
        ProjectBriefingSummaryPoint point,
        double x,
        double y,
        double size,
        string badgeFill,
        string textColor,
        string name)
    {
        canvas.AddEllipse(x, y, size, size, badgeFill, null, name: name);
        var abbreviation = ResolveStageBadge(point);
        AddOpticallyCenteredBadgeText(
            canvas,
            x,
            y,
            size,
            abbreviation,
            ResolveBadgeFontSize(abbreviation, size),
            textColor,
            $"{name} text");
    }

    private static void AddOpticallyCenteredBadgeText(
        SlideCanvas canvas,
        double x,
        double y,
        double size,
        string text,
        double fontSize,
        string color,
        string name)
    {
        var containsLowerCase = text.Any(char.IsLower);
        var verticalNudge = containsLowerCase ? .004 : .007;
        canvas.AddRichTextBox(
            x,
            y + verticalNudge,
            size,
            size,
            new[]
            {
                new RichTextParagraph(
                    new[]
                    {
                        new RichTextRun(text, fontSize, color, Bold: true)
                    },
                    Align: "ctr",
                    LineSpacingPoints: fontSize * 1.02)
            },
            name,
            verticalAnchor: "ctr",
            allowAutoFit: false,
            leftInset: 0,
            rightInset: 0,
            topInset: 0,
            bottomInset: 0);
    }

    private static double ResolveBadgeFontSize(string abbreviation, double size)
    {
        var scale = size / .40;
        var baseSize = abbreviation.Length switch
        {
            <= 2 => 10.4,
            3 when abbreviation.Any(char.IsLower) => 8.8,
            3 => 9.5,
            4 => 8.1,
            _ => 7.6
        };
        return baseSize * scale;
    }

    private static bool RenderDevelopmentStageIcon(
        SlideCanvas canvas,
        double x,
        double y,
        double size,
        string background,
        string color,
        string name)
    {
        var centreX = x + (size / 2d);
        var centreY = y + (size / 2d);
        var outerRadius = size * .205;
        var innerRadius = size * .080;
        canvas.AddEllipse(
            centreX - outerRadius,
            centreY - outerRadius,
            outerRadius * 2,
            outerRadius * 2,
            background,
            color,
            1.05,
            $"{name} gear rim");
        canvas.AddEllipse(
            centreX - innerRadius,
            centreY - innerRadius,
            innerRadius * 2,
            innerRadius * 2,
            background,
            color,
            .90,
            $"{name} gear hub");

        for (var index = 0; index < 8; index++)
        {
            var angle = (Math.PI * 2d * index / 8d) - (Math.PI / 2d);
            var startRadius = outerRadius * .86;
            var endRadius = outerRadius * 1.30;
            canvas.AddLine(
                centreX + (Math.Cos(angle) * startRadius),
                centreY + (Math.Sin(angle) * startRadius),
                centreX + (Math.Cos(angle) * endRadius),
                centreY + (Math.Sin(angle) * endRadius),
                color,
                1.00);
        }

        return true;
    }

    private static bool RenderTechnicalEvaluationStageIcon(
        SlideCanvas canvas,
        double x,
        double y,
        double size,
        string background,
        string color,
        string name)
    {
        var documentX = x + (size * .22);
        var documentY = y + (size * .18);
        var documentWidth = size * .42;
        var documentHeight = size * .55;
        canvas.AddRoundedRect(documentX, documentY, documentWidth, documentHeight, background, color, .02, $"{name} clipboard");
        canvas.AddRoundedRect(
            documentX + (documentWidth * .27),
            documentY - (size * .025),
            documentWidth * .46,
            size * .09,
            background,
            color,
            .02,
            $"{name} clipboard clip");
        canvas.AddLine(documentX + size * .08, documentY + size * .18, documentX + size * .27, documentY + size * .18, color, .80);
        canvas.AddLine(documentX + size * .08, documentY + size * .29, documentX + size * .24, documentY + size * .29, color, .80);
        RenderMagnifier(canvas, x + (size * .50), y + (size * .48), size * .19, background, color, name);
        return true;
    }

    private static bool RenderBiddingStageIcon(
        SlideCanvas canvas,
        double x,
        double y,
        double size,
        string background,
        string color,
        string name)
    {
        canvas.AddRoundedRect(x + size * .19, y + size * .24, size * .36, size * .15, background, color, .02, $"{name} gavel head");
        canvas.AddLine(x + size * .48, y + size * .39, x + size * .68, y + size * .64, color, 1.25);
        canvas.AddLine(x + size * .26, y + size * .70, x + size * .68, y + size * .70, color, 1.05);
        canvas.AddLine(x + size * .32, y + size * .64, x + size * .62, y + size * .64, color, .85);
        return true;
    }

    private static bool RenderApprovedDocumentStageIcon(
        SlideCanvas canvas,
        double x,
        double y,
        double size,
        string background,
        string color,
        string name)
    {
        RenderDocumentOutline(canvas, x, y, size, background, color, name);
        canvas.AddLine(x + size * .35, y + size * .56, x + size * .44, y + size * .65, color, 1.15);
        canvas.AddLine(x + size * .44, y + size * .65, x + size * .67, y + size * .40, color, 1.15);
        return true;
    }

    private static bool RenderDocumentReviewStageIcon(
        SlideCanvas canvas,
        double x,
        double y,
        double size,
        string background,
        string color,
        string name)
    {
        RenderDocumentOutline(canvas, x, y, size, background, color, name);
        canvas.AddLine(x + size * .31, y + size * .35, x + size * .51, y + size * .35, color, .80);
        canvas.AddLine(x + size * .31, y + size * .46, x + size * .48, y + size * .46, color, .80);
        RenderMagnifier(canvas, x + size * .51, y + size * .51, size * .16, background, color, name);
        return true;
    }

    private static bool RenderApprovalSealStageIcon(
        SlideCanvas canvas,
        double x,
        double y,
        double size,
        string background,
        string color,
        string name)
    {
        var sealSize = size * .46;
        var sealX = x + ((size - sealSize) / 2d);
        var sealY = y + (size * .20);
        canvas.AddEllipse(sealX, sealY, sealSize, sealSize, background, color, 1.0, $"{name} approval seal");
        canvas.AddLine(sealX + sealSize * .25, sealY + sealSize * .53, sealX + sealSize * .43, sealY + sealSize * .70, color, 1.05);
        canvas.AddLine(sealX + sealSize * .43, sealY + sealSize * .70, sealX + sealSize * .76, sealY + sealSize * .31, color, 1.05);
        canvas.AddLine(x + size * .37, y + size * .66, x + size * .29, y + size * .79, color, .95);
        canvas.AddLine(x + size * .63, y + size * .66, x + size * .71, y + size * .79, color, .95);
        return true;
    }

    private static bool RenderFeasibilityStageIcon(
        SlideCanvas canvas,
        double x,
        double y,
        double size,
        string background,
        string color,
        string name)
    {
        RenderDocumentOutline(canvas, x - size * .04, y, size, background, color, name);
        RenderMagnifier(canvas, x + size * .48, y + size * .48, size * .20, background, color, name);
        return true;
    }

    private static bool RenderBenchmarkStageIcon(
        SlideCanvas canvas,
        double x,
        double y,
        double size,
        string background,
        string color,
        string name)
    {
        var baseY = y + size * .72;
        canvas.AddRect(x + size * .22, baseY - size * .22, size * .10, size * .22, color, color, .6, $"{name} benchmark bar 1");
        canvas.AddRect(x + size * .42, baseY - size * .36, size * .10, size * .36, color, color, .6, $"{name} benchmark bar 2");
        canvas.AddRect(x + size * .62, baseY - size * .50, size * .10, size * .50, color, color, .6, $"{name} benchmark bar 3");
        canvas.AddLine(x + size * .18, baseY, x + size * .78, baseY, color, .85);
        return true;
    }

    private static bool RenderCommercialOpeningStageIcon(
        SlideCanvas canvas,
        double x,
        double y,
        double size,
        string background,
        string color,
        string name)
    {
        var envelopeX = x + size * .18;
        var envelopeY = y + size * .31;
        var envelopeWidth = size * .64;
        var envelopeHeight = size * .42;
        canvas.AddRoundedRect(envelopeX, envelopeY, envelopeWidth, envelopeHeight, background, color, .02, $"{name} envelope");
        canvas.AddLine(envelopeX, envelopeY, envelopeX + envelopeWidth / 2d, envelopeY + envelopeHeight * .52, color, .85);
        canvas.AddLine(envelopeX + envelopeWidth, envelopeY, envelopeX + envelopeWidth / 2d, envelopeY + envelopeHeight * .52, color, .85);
        canvas.AddLine(envelopeX + envelopeWidth * .26, envelopeY - size * .11, envelopeX + envelopeWidth * .50, envelopeY - size * .02, color, 1.0);
        canvas.AddLine(envelopeX + envelopeWidth * .74, envelopeY - size * .11, envelopeX + envelopeWidth * .50, envelopeY - size * .02, color, 1.0);
        return true;
    }

    private static bool RenderNegotiationStageIcon(
        SlideCanvas canvas,
        double x,
        double y,
        double size,
        string background,
        string color,
        string name)
    {
        var bubbleWidth = size * .38;
        var bubbleHeight = size * .24;
        canvas.AddRoundedRect(x + size * .16, y + size * .25, bubbleWidth, bubbleHeight, background, color, .02, $"{name} negotiation bubble 1");
        canvas.AddRoundedRect(x + size * .46, y + size * .49, bubbleWidth, bubbleHeight, background, color, .02, $"{name} negotiation bubble 2");
        canvas.AddLine(x + size * .30, y + size * .49, x + size * .25, y + size * .60, color, .85);
        canvas.AddLine(x + size * .70, y + size * .73, x + size * .75, y + size * .82, color, .85);
        canvas.AddLine(x + size * .25, y + size * .36, x + size * .45, y + size * .36, color, .75);
        canvas.AddLine(x + size * .55, y + size * .60, x + size * .75, y + size * .60, color, .75);
        return true;
    }

    private static bool RenderSupplyOrderStageIcon(
        SlideCanvas canvas,
        double x,
        double y,
        double size,
        string background,
        string color,
        string name)
    {
        var boxX = x + size * .18;
        var boxY = y + size * .30;
        var boxWidth = size * .42;
        var boxHeight = size * .38;
        canvas.AddRect(boxX, boxY, boxWidth, boxHeight, background, color, .85, $"{name} supply package");
        canvas.AddLine(boxX, boxY, boxX + boxWidth / 2d, boxY + size * .10, color, .75);
        canvas.AddLine(boxX + boxWidth, boxY, boxX + boxWidth / 2d, boxY + size * .10, color, .75);
        canvas.AddLine(x + size * .56, y + size * .49, x + size * .80, y + size * .49, color, 1.05);
        canvas.AddLine(x + size * .72, y + size * .41, x + size * .80, y + size * .49, color, 1.05);
        canvas.AddLine(x + size * .72, y + size * .57, x + size * .80, y + size * .49, color, 1.05);
        return true;
    }

    private static bool RenderAcceptanceTestingStageIcon(
        SlideCanvas canvas,
        double x,
        double y,
        double size,
        string background,
        string color,
        string name)
    {
        var clipboardX = x + size * .22;
        var clipboardY = y + size * .18;
        var clipboardWidth = size * .56;
        var clipboardHeight = size * .62;
        canvas.AddRoundedRect(clipboardX, clipboardY, clipboardWidth, clipboardHeight, background, color, .02, $"{name} testing clipboard");
        canvas.AddRoundedRect(clipboardX + clipboardWidth * .30, clipboardY - size * .025, clipboardWidth * .40, size * .09, background, color, .02, $"{name} testing clipboard clip");
        canvas.AddLine(x + size * .34, y + size * .52, x + size * .43, y + size * .61, color, 1.05);
        canvas.AddLine(x + size * .43, y + size * .61, x + size * .65, y + size * .38, color, 1.05);
        return true;
    }

    private static bool RenderPaymentStageIcon(
        SlideCanvas canvas,
        double x,
        double y,
        double size,
        string background,
        string color,
        string name)
    {
        RenderDocumentOutline(canvas, x, y, size, background, color, name);
        AddOpticallyCenteredBadgeText(
            canvas,
            x + size * .28,
            y + size * .29,
            size * .36,
            "₹",
            11.0 * (size / .46),
            color,
            $"{name} rupee symbol");
        return true;
    }

    private static bool RenderTransferStageIcon(
        SlideCanvas canvas,
        double x,
        double y,
        double size,
        string background,
        string color,
        string name)
    {
        var nodeSize = size * .16;
        canvas.AddEllipse(x + size * .16, y + size * .42, nodeSize, nodeSize, background, color, .90, $"{name} transfer source");
        canvas.AddEllipse(x + size * .68, y + size * .42, nodeSize, nodeSize, background, color, .90, $"{name} transfer target");
        canvas.AddLine(x + size * .34, y + size * .41, x + size * .67, y + size * .41, color, 1.0);
        canvas.AddLine(x + size * .59, y + size * .33, x + size * .67, y + size * .41, color, 1.0);
        canvas.AddLine(x + size * .59, y + size * .49, x + size * .67, y + size * .41, color, 1.0);
        canvas.AddLine(x + size * .66, y + size * .64, x + size * .33, y + size * .64, color, 1.0);
        canvas.AddLine(x + size * .41, y + size * .56, x + size * .33, y + size * .64, color, 1.0);
        canvas.AddLine(x + size * .41, y + size * .72, x + size * .33, y + size * .64, color, 1.0);
        return true;
    }

    private static void RenderDocumentOutline(
        SlideCanvas canvas,
        double x,
        double y,
        double size,
        string background,
        string color,
        string name)
    {
        var documentX = x + size * .24;
        var documentY = y + size * .16;
        var documentWidth = size * .52;
        var documentHeight = size * .67;
        canvas.AddRoundedRect(documentX, documentY, documentWidth, documentHeight, background, color, .02, $"{name} document");
        canvas.AddLine(documentX + documentWidth * .62, documentY, documentX + documentWidth, documentY + documentHeight * .22, color, .75);
        canvas.AddLine(documentX + documentWidth * .62, documentY, documentX + documentWidth * .62, documentY + documentHeight * .22, color, .75);
        canvas.AddLine(documentX + documentWidth * .62, documentY + documentHeight * .22, documentX + documentWidth, documentY + documentHeight * .22, color, .75);
    }

    private static void RenderMagnifier(
        SlideCanvas canvas,
        double centreX,
        double centreY,
        double radius,
        string background,
        string color,
        string name)
    {
        canvas.AddEllipse(
            centreX - radius,
            centreY - radius,
            radius * 2,
            radius * 2,
            background,
            color,
            .90,
            $"{name} magnifier lens");
        canvas.AddLine(
            centreX + (radius * .66),
            centreY + (radius * .66),
            centreX + (radius * 1.48),
            centreY + (radius * 1.48),
            color,
            1.00);
    }

}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;

namespace ProjectManagement.Services.Presentation;

// SECTION: Shared PowerPoint drawing (one implementation for the generated FFC, CoE and ARPP decks)
/// <summary>
/// One run of a <see cref="PptxTextParagraph"/>: its own size, colour and weight. A tab ("\t") in the text
/// moves to the paragraph's right tab stop; <see cref="LineBreak"/> starts a new line in the same paragraph.
/// </summary>
public sealed record PptxRun(string Text, double FontSize, string Color, bool Bold = false)
{
    public bool IsLineBreak { get; init; }

    public static PptxRun LineBreak(double fontSize) => new(string.Empty, fontSize, "000000") { IsLineBreak = true };
}

/// <summary>
/// A paragraph of <see cref="PptxSlideCanvas.AddTextBox"/>: a list item that edits as text in PowerPoint
/// (Enter adds the next one, with the same bullet and tab stop).
/// </summary>
public sealed record PptxTextParagraph(IReadOnlyList<PptxRun> Runs)
{
    /// <summary>"l", "ctr" or "r".</summary>
    public string Align { get; init; } = "l";

    /// <summary>A round bullet in this colour, hanging by <see cref="BulletIndent"/>; null for none.</summary>
    public string? BulletColor { get; init; }

    public double BulletIndent { get; init; } = .2;

    /// <summary>A right-aligned tab stop this far (inches) from the box's left inset: where a "\t" sends the rest of the line.</summary>
    public double? RightTabAt { get; init; }

    public double SpaceBeforePoints { get; init; }

    public double SpaceAfterPoints { get; init; }
}

/// <summary>A text box's inner margins, in inches.</summary>
public sealed record PptxInsets(double Left, double Top, double Right, double Bottom)
{
    public static readonly PptxInsets None = new(0, 0, 0, 0);
}

/// <summary>A native PowerPoint table cell for <see cref="PptxSlideCanvas.AddNativeTable"/>.</summary>
public sealed record PptxTableCell(
    string Value,
    double FontSize,
    string Color,
    bool Bold,
    string Align,
    string Fill);

/// <summary>Template handling shared by the generated decks: the blank layout, and clearing the template's own slides.</summary>
public static class PptxTemplate
{
    public static SlideLayoutPart? FindBlankLayout(PresentationPart presentationPart)
        => presentationPart.SlideMasterParts
               .SelectMany(master => master.SlideLayoutParts)
               .FirstOrDefault(layout =>
                   string.Equals(layout.SlideLayout?.CommonSlideData?.Name?.Value, "Blank", StringComparison.OrdinalIgnoreCase))
           ?? presentationPart.SlideMasterParts.SelectMany(master => master.SlideLayoutParts).FirstOrDefault();

    public static void RemoveTemplateSlides(PresentationPart presentationPart)
    {
        var slideIdList = presentationPart.Presentation.SlideIdList;
        if (slideIdList is null)
        {
            return;
        }

        foreach (var slideId in slideIdList.Elements<SlideId>().ToList())
        {
            var relationshipId = slideId.RelationshipId?.Value;
            if (!string.IsNullOrWhiteSpace(relationshipId) && presentationPart.GetPartById(relationshipId) is SlidePart slidePart)
            {
                presentationPart.DeletePart(slidePart);
            }
            slideId.Remove();
        }
    }
}

/// <summary>
/// Draws one slide as DrawingML: rectangles, lines, text boxes, native tables and pictures,
/// positioned in inches on a 13.333 x 7.5 in (16:9) slide. Call <see cref="Commit"/> once at the end.
/// </summary>
public sealed class PptxSlideCanvas
{
    private const string Text = "111827";
    private const string Border = "D8E0EA";

    private readonly SlidePart _slidePart;
    private readonly List<string> _elements = new();
    private int _nextShapeId = 2;

    public PptxSlideCanvas(SlidePart slidePart)
    {
        _slidePart = slidePart;
    }

    public void AddRect(
        double x,
        double y,
        double width,
        double height,
        string fill,
        string? line = null,
        double lineWidth = .75)
        => AddShape(x, y, width, height, fill, line, lineWidth, "rect", null, 0, Text, false, "l");

    public void AddRoundedRect(
        double x,
        double y,
        double width,
        double height,
        string fill,
        string? line,
        double radius)
        => AddShape(x, y, width, height, fill, line, .75, "roundRect", null, 0, Text, false, "l");

    /// <summary>A filled circle or ellipse (list bullets, legend dots).</summary>
    public void AddEllipse(double x, double y, double width, double height, string fill)
        => AddShape(x, y, width, height, fill, null, 0, "ellipse", null, 0, Text, false, "l");

    public void AddLine(
        double x1,
        double y1,
        double x2,
        double y2,
        string color,
        double width)
    {
        var id = _nextShapeId++;
        _elements.Add($"""
<p:cxnSp>
  <p:nvCxnSpPr><p:cNvPr id="{id}" name="Line {id}"/><p:cNvCxnSpPr/><p:nvPr/></p:nvCxnSpPr>
  <p:spPr>
<a:xfrm><a:off x="{Emu(x1)}" y="{Emu(y1)}"/><a:ext cx="{Emu(x2 - x1)}" cy="{Emu(y2 - y1)}"/></a:xfrm>
<a:prstGeom prst="line"><a:avLst/></a:prstGeom>
<a:ln w="{LineWidth(width)}"><a:solidFill><a:srgbClr val="{CleanColor(color)}"/></a:solidFill></a:ln>
  </p:spPr>
</p:cxnSp>
""");
    }

    public void AddText(
        double x,
        double y,
        double width,
        double height,
        string text,
        double fontSize,
        string color,
        bool bold,
        string align)
        => AddShape(x, y, width, height, null, null, 0, "rect", text, fontSize, color, bold, align);

    public void AddNativeTable(
        double x,
        double y,
        IReadOnlyList<double> widths,
        IReadOnlyList<double> heights,
        IReadOnlyList<IReadOnlyList<PptxTableCell>> rows,
        string name)
    {
        if (widths.Count == 0 || rows.Count == 0)
        {
            return;
        }

        if (heights.Count != rows.Count)
        {
            throw new ArgumentException("A native PowerPoint table requires one row height per row.", nameof(heights));
        }

        if (rows.Any(row => row.Count != widths.Count))
        {
            throw new ArgumentException("Every native PowerPoint table row must contain one cell per column.", nameof(rows));
        }

        var id = _nextShapeId++;
        var tableWidth = widths.Sum();
        var tableHeight = heights.Sum();
        var columnXml = string.Join(string.Empty, widths.Select(width => $"<a:gridCol w=\"{Emu(width)}\"/>"));
        var rowXml = new StringBuilder();

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            rowXml.Append($"<a:tr h=\"{Emu(heights[rowIndex])}\">");
            foreach (var cell in rows[rowIndex])
            {
                rowXml.Append($"""
<a:tc>
  {BuildTableTextBody(cell)}
  <a:tcPr marL="45720" marR="45720" marT="22860" marB="22860" anchor="ctr">
<a:solidFill><a:srgbClr val="{CleanColor(cell.Fill)}"/></a:solidFill>
{TableBorders()}
  </a:tcPr>
</a:tc>
""");
            }
            rowXml.Append("</a:tr>");
        }

        _elements.Add($"""
<p:graphicFrame>
  <p:nvGraphicFramePr><p:cNvPr id="{id}" name="{Escape(name)}"/><p:cNvGraphicFramePr><a:graphicFrameLocks noGrp="1"/></p:cNvGraphicFramePr><p:nvPr/></p:nvGraphicFramePr>
  <p:xfrm><a:off x="{Emu(x)}" y="{Emu(y)}"/><a:ext cx="{Emu(tableWidth)}" cy="{Emu(tableHeight)}"/></p:xfrm>
  <a:graphic>
<a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/table">
  <a:tbl>
    <a:tblPr firstRow="1" bandRow="1"/>
    <a:tblGrid>{columnXml}</a:tblGrid>
    {rowXml}
  </a:tbl>
</a:graphicData>
  </a:graphic>
</p:graphicFrame>
""");
    }

    public void AddImage(
        byte[] content,
        double x,
        double y,
        double width,
        double height,
        string name)
    {
        var imagePart = _slidePart.AddImagePart(ImagePartType.Png);
        using (var imageStream = new MemoryStream(content, writable: false))
        {
            imagePart.FeedData(imageStream);
        }
        var relationshipId = _slidePart.GetIdOfPart(imagePart);
        var id = _nextShapeId++;
        _elements.Add($"""
<p:pic>
  <p:nvPicPr><p:cNvPr id="{id}" name="{Escape(name)}"/><p:cNvPicPr><a:picLocks noChangeAspect="1"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>
  <p:blipFill><a:blip r:embed="{relationshipId}"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>
  <p:spPr><a:xfrm><a:off x="{Emu(x)}" y="{Emu(y)}"/><a:ext cx="{Emu(width)}" cy="{Emu(height)}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:ln><a:noFill/></a:ln></p:spPr>
</p:pic>
""");
    }

    /// <summary>
    /// A PNG scaled to fit inside the box without stretching, centred in it (an insignia keeps its
    /// shape whatever its pixel size). Nothing is drawn for an empty image.
    /// </summary>
    public void AddImageContained(
        byte[]? content,
        double x,
        double y,
        double maximumWidth,
        double maximumHeight,
        string name)
    {
        if (content is null || content.Length == 0)
        {
            return;
        }

        var (pixelWidth, pixelHeight) = PngDimensions(content);
        var aspectRatio = pixelWidth / (double)pixelHeight;
        var width = maximumWidth;
        var height = width / aspectRatio;
        if (height > maximumHeight)
        {
            height = maximumHeight;
            width = height * aspectRatio;
        }

        AddImage(content, x + ((maximumWidth - width) / 2d), y + ((maximumHeight - height) / 2d), width, height, name);
    }

    private static (int Width, int Height) PngDimensions(byte[] content)
    {
        if (content.Length >= 24 && content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47)
        {
            var width = (content[16] << 24) | (content[17] << 16) | (content[18] << 8) | content[19];
            var height = (content[20] << 24) | (content[21] << 16) | (content[22] << 8) | content[23];
            return (Math.Max(1, width), Math.Max(1, height));
        }

        return (1, 1);
    }

    public void Commit()
    {
        var xml = $"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:sld xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main">
  <p:cSld>
<p:spTree>
  <p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>
  <p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr>
  {string.Join(Environment.NewLine, _elements)}
</p:spTree>
  </p:cSld>
  <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
</p:sld>
""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        _slidePart.FeedData(stream);
    }

    private void AddShape(
        double x,
        double y,
        double width,
        double height,
        string? fill,
        string? line,
        double lineWidth,
        string geometry,
        string? text,
        double fontSize,
        string color,
        bool bold,
        string align)
    {
        var textXml = text is null
            ? string.Empty
            : BuildTextBody(text, fontSize, color, bold, align);
        AddShapeXml(x, y, width, height, fill, line, lineWidth, geometry, textXml);
    }

    /// <summary>
    /// One named shape holding whole paragraphs (a card, a column heading, a column of list items), so the
    /// slide edits as text in PowerPoint instead of as a shape per line. <paramref name="shrinkOnOverflow"/>
    /// lets PowerPoint shrink the text when an edit makes it overflow; <paramref name="columns"/> flows the
    /// paragraphs through that many text columns.
    /// </summary>
    public void AddTextBox(
        double x,
        double y,
        double width,
        double height,
        IReadOnlyList<PptxTextParagraph> paragraphs,
        string name,
        string? fill = null,
        bool rounded = false,
        string anchor = "t",
        PptxInsets? insets = null,
        bool shrinkOnOverflow = false,
        int columns = 1,
        double columnGap = 0)
    {
        var inset = insets ?? PptxInsets.None;
        var textColumns = columns > 1 ? $" numCol=\"{columns}\" spcCol=\"{Emu(columnGap)}\"" : string.Empty;
        var anchorValue = anchor is "ctr" or "b" ? anchor : "t";
        var body = string.Concat(paragraphs.Select(BuildParagraph));
        var textXml = $"<p:txBody><a:bodyPr wrap=\"square\" lIns=\"{Emu(inset.Left)}\" tIns=\"{Emu(inset.Top)}\" rIns=\"{Emu(inset.Right)}\" bIns=\"{Emu(inset.Bottom)}\" anchor=\"{anchorValue}\"{textColumns}>{(shrinkOnOverflow ? "<a:normAutofit/>" : "<a:noAutofit/>")}</a:bodyPr><a:lstStyle/>{body}</p:txBody>";
        AddShapeXml(x, y, width, height, fill, null, 0, rounded ? "roundRect" : "rect", textXml, name);
    }

    private static string BuildParagraph(PptxTextParagraph paragraph)
    {
        var alignment = paragraph.Align switch
        {
            "ctr" => "ctr",
            "r" => "r",
            _ => "l"
        };
        var attributes = new StringBuilder($" algn=\"{alignment}\"");
        var properties = new StringBuilder();
        if (paragraph.SpaceBeforePoints > 0)
        {
            properties.Append($"<a:spcBef><a:spcPts val=\"{FontSize(paragraph.SpaceBeforePoints)}\"/></a:spcBef>");
        }
        if (paragraph.SpaceAfterPoints > 0)
        {
            properties.Append($"<a:spcAft><a:spcPts val=\"{FontSize(paragraph.SpaceAfterPoints)}\"/></a:spcAft>");
        }
        if (paragraph.BulletColor is { } bullet)
        {
            attributes.Append($" marL=\"{Emu(paragraph.BulletIndent)}\" indent=\"{-Emu(paragraph.BulletIndent)}\"");
            properties.Append($"<a:buClr><a:srgbClr val=\"{CleanColor(bullet)}\"/></a:buClr><a:buSzPct val=\"100000\"/><a:buFont typeface=\"Arial\"/><a:buChar char=\"●\"/>");
        }
        else
        {
            properties.Append("<a:buNone/>");
        }
        if (paragraph.RightTabAt is { } tab)
        {
            properties.Append($"<a:tabLst><a:tab pos=\"{Emu(tab)}\" algn=\"r\"/></a:tabLst>");
        }

        var runs = string.Concat(paragraph.Runs.Select(run => run.IsLineBreak
            ? $"<a:br><a:rPr lang=\"en-US\" sz=\"{FontSize(run.FontSize)}\"/></a:br>"
            : $"<a:r><a:rPr lang=\"en-US\" sz=\"{FontSize(run.FontSize)}\" b=\"{(run.Bold ? 1 : 0)}\"><a:solidFill><a:srgbClr val=\"{CleanColor(run.Color)}\"/></a:solidFill><a:latin typeface=\"Aptos\"/></a:rPr><a:t xml:space=\"preserve\">{Escape(run.Text)}</a:t></a:r>"));
        var endSize = paragraph.Runs.Count > 0 ? paragraph.Runs[^1].FontSize : 10;
        return $"<a:p><a:pPr{attributes}>{properties}</a:pPr>{runs}<a:endParaRPr lang=\"en-US\" sz=\"{FontSize(endSize)}\"/></a:p>";
    }

    private void AddShapeXml(
        double x,
        double y,
        double width,
        double height,
        string? fill,
        string? line,
        double lineWidth,
        string geometry,
        string textXml,
        string? name = null)
    {
        var id = _nextShapeId++;
        var fillXml = string.IsNullOrWhiteSpace(fill)
            ? "<a:noFill/>"
            : $"<a:solidFill><a:srgbClr val=\"{CleanColor(fill)}\"/></a:solidFill>";
        var lineXml = string.IsNullOrWhiteSpace(line)
            ? "<a:ln><a:noFill/></a:ln>"
            : $"<a:ln w=\"{LineWidth(lineWidth)}\"><a:solidFill><a:srgbClr val=\"{CleanColor(line)}\"/></a:solidFill></a:ln>";

        _elements.Add($"""
<p:sp>
  <p:nvSpPr><p:cNvPr id="{id}" name="{Escape(name ?? $"Shape {id}")}"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr>
  <p:spPr>
<a:xfrm><a:off x="{Emu(x)}" y="{Emu(y)}"/><a:ext cx="{Emu(width)}" cy="{Emu(height)}"/></a:xfrm>
<a:prstGeom prst="{geometry}"><a:avLst/></a:prstGeom>
{fillXml}{lineXml}
  </p:spPr>
  {textXml}
</p:sp>
""");
    }

    private static string BuildTableTextBody(PptxTableCell cell)
    {
        var alignment = cell.Align switch
        {
            "ctr" => "ctr",
            "r" => "r",
            _ => "l"
        };

        var paragraphs = cell.Value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n')
            .Select(line => $"""
<a:p><a:pPr algn="{alignment}"/><a:r><a:rPr lang="en-US" sz="{FontSize(cell.FontSize)}" b="{(cell.Bold ? 1 : 0)}"><a:solidFill><a:srgbClr val="{CleanColor(cell.Color)}"/></a:solidFill><a:latin typeface="Aptos"/></a:rPr><a:t xml:space="preserve">{Escape(line)}</a:t></a:r><a:endParaRPr lang="en-US" sz="{FontSize(cell.FontSize)}"/></a:p>
""");

        return $"""
<a:txBody><a:bodyPr wrap="square" lIns="0" rIns="0" tIns="0" bIns="0" anchor="ctr"/><a:lstStyle/>{string.Join(string.Empty, paragraphs)}</a:txBody>
""";
    }

    private static string TableBorders()
    {
        var line = $"<a:solidFill><a:srgbClr val=\"{Border}\"/></a:solidFill><a:prstDash val=\"solid\"/>";
        return $"""
<a:lnL w="6350">{line}</a:lnL>
<a:lnR w="6350">{line}</a:lnR>
<a:lnT w="6350">{line}</a:lnT>
<a:lnB w="6350">{line}</a:lnB>
""";
    }

    private static string BuildTextBody(
        string text,
        double fontSize,
        string color,
        bool bold,
        string align)
    {
        var alignment = align switch
        {
            "ctr" => "ctr",
            "r" => "r",
            _ => "l"
        };
        var paragraphs = text
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n')
            .Select(line => $"""
<a:p><a:pPr algn="{alignment}"/><a:r><a:rPr lang="en-US" sz="{FontSize(fontSize)}" b="{(bold ? 1 : 0)}"><a:solidFill><a:srgbClr val="{CleanColor(color)}"/></a:solidFill><a:latin typeface="Aptos"/></a:rPr><a:t xml:space="preserve">{Escape(line)}</a:t></a:r><a:endParaRPr lang="en-US" sz="{FontSize(fontSize)}"/></a:p>
""");
        return $"""
<p:txBody><a:bodyPr wrap="square" lIns="45720" rIns="45720" tIns="22860" bIns="22860" anchor="ctr"/><a:lstStyle/>{string.Join(string.Empty, paragraphs)}</p:txBody>
""";
    }

    private static long Emu(double inches) => (long)Math.Round(inches * 914400d);
    private static long LineWidth(double points) => (long)Math.Round(points * 12700d);
    private static int FontSize(double points) => (int)Math.Round(points * 100d);
    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;
    private static string CleanColor(string value) => value.Trim().TrimStart('#').ToUpperInvariant();
}
// END SECTION

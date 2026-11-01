using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace ProjectManagement.Services.ProjectBriefings.Presentation;

// Reads a PowerPoint produced by ProjectBriefingSlideComposer back into positioned HTML so the
// builder can show the exact slides the download will contain. It understands only the drawing
// vocabulary the composer writes (shapes, text, lines, pictures, tables and groups); anything
// else is skipped rather than guessed. Every text value is HTML-encoded and every colour and
// number is re-formatted, so the result is safe to insert into the page.
internal static partial class ProjectBriefingSlidePreviewRenderer
{
    private const double EmuPerPixel = 9525d; // 96 dpi
    private const double PointToPixel = 96d / 72d;
    private const int MaximumImageWidth = 960;
    private const string FontStack = "Aptos, 'Segoe UI', Arial, sans-serif";

    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static ProjectBriefingSlidePreview Render(byte[] presentation, DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        using var stream = new MemoryStream(presentation, writable: false);
        using var document = PresentationDocument.Open(stream, false);
        var presentationPart = document.PresentationPart
            ?? throw new InvalidOperationException("The composed presentation has no presentation part.");
        var slideSize = presentationPart.Presentation.SlideSize;
        var widthPx = Px(slideSize?.Cx?.Value ?? 12192000);
        var heightPx = Px(slideSize?.Cy?.Value ?? 6858000);

        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        var slides = new List<ProjectBriefingSlidePreviewSlide>();
        var slideIds = presentationPart.Presentation.SlideIdList?.Elements<DocumentFormat.OpenXml.Presentation.SlideId>()
            ?? Enumerable.Empty<DocumentFormat.OpenXml.Presentation.SlideId>();
        foreach (var slideId in slideIds)
        {
            if (slideId.RelationshipId?.Value is not { } relationshipId
                || presentationPart.GetPartById(relationshipId) is not SlidePart slidePart)
            {
                continue;
            }

            XDocument xml;
            using (var partStream = slidePart.GetStream(FileMode.Open, FileAccess.Read))
            {
                xml = XDocument.Load(partStream);
            }

            var tree = xml.Root?.Element(P + "cSld")?.Element(P + "spTree");
            var html = new StringBuilder();
            html.Append(CultureInfo.InvariantCulture,
                $"<div class=\"pbd-slide-stage\" style=\"width:{N(widthPx)}px;height:{N(heightPx)}px;font-family:{FontStack}\">");
            if (tree is not null)
            {
                var context = new RenderContext(slidePart, images, html);
                RenderChildren(tree, Transform.Identity, context);
            }

            html.Append("</div>");
            var number = slides.Count + 1;
            slides.Add(new ProjectBriefingSlidePreviewSlide(number, ResolveTitle(tree, number, slideIds.Count()), html.ToString()));
        }

        return new ProjectBriefingSlidePreview(slides.Count, generatedAtUtc, slides, images);
    }

    private static void RenderChildren(XElement container, Transform transform, RenderContext context)
    {
        foreach (var element in container.Elements())
        {
            if (element.Name == P + "sp") RenderShape(element, transform, context);
            else if (element.Name == P + "cxnSp") RenderLine(element, transform, context);
            else if (element.Name == P + "pic") RenderPicture(element, transform, context);
            else if (element.Name == P + "graphicFrame") RenderTable(element, transform, context);
            else if (element.Name == P + "grpSp") RenderGroup(element, transform, context);
        }
    }

    private static void RenderGroup(XElement group, Transform transform, RenderContext context)
    {
        var xfrm = group.Element(P + "grpSpPr")?.Element(A + "xfrm");
        var off = ReadPoint(xfrm?.Element(A + "off"), "x", "y");
        var ext = ReadPoint(xfrm?.Element(A + "ext"), "cx", "cy");
        var chOff = ReadPoint(xfrm?.Element(A + "chOff"), "x", "y");
        var chExt = ReadPoint(xfrm?.Element(A + "chExt"), "cx", "cy");
        var scaleX = chExt.X > 0 ? ext.X / chExt.X : 1d;
        var scaleY = chExt.Y > 0 ? ext.Y / chExt.Y : 1d;
        var inner = new Transform(off.X - (chOff.X * scaleX), off.Y - (chOff.Y * scaleY), scaleX, scaleY);
        RenderChildren(group, transform.Then(inner), context);
    }

    private static void RenderShape(XElement shape, Transform transform, RenderContext context)
    {
        var properties = shape.Element(P + "spPr");
        var frame = ReadFrame(properties?.Element(A + "xfrm"), transform);
        if (frame is null) return;

        var geometry = properties?.Element(A + "prstGeom");
        var preset = (string?)geometry?.Attribute("prst") ?? "rect";
        var style = new StringBuilder(FrameStyle(frame.Value));
        AppendFill(style, properties);
        AppendOutline(style, properties?.Element(A + "ln"), transform);
        if (preset == "ellipse")
        {
            style.Append("border-radius:50%;");
        }
        else if (preset == "roundRect")
        {
            var adjustment = geometry?.Descendants(A + "gd")
                .Select(gd => (string?)gd.Attribute("fmla"))
                .Select(fmla => fmla is not null && fmla.StartsWith("val ", StringComparison.Ordinal)
                    && double.TryParse(fmla[4..], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                        ? value
                        : (double?)null)
                .FirstOrDefault(value => value.HasValue) ?? 16667d;
            var radius = Math.Min(frame.Value.Width, frame.Value.Height) * Math.Clamp(adjustment, 0, 50000) / 100000d;
            style.Append(CultureInfo.InvariantCulture, $"border-radius:{N(radius)}px;");
        }

        var body = shape.Element(P + "txBody");
        context.Html.Append($"<div class=\"pbd-slide-shape\" style=\"{style}\">");
        if (body is not null)
        {
            AppendTextBody(context.Html, body, transform, defaultInsets: (91440, 45720, 91440, 45720));
        }

        context.Html.Append("</div>");
    }

    private static void RenderLine(XElement connector, Transform transform, RenderContext context)
    {
        var properties = connector.Element(P + "spPr");
        var xfrm = properties?.Element(A + "xfrm");
        var frame = ReadFrame(xfrm, transform, allowEmpty: true);
        if (frame is null) return;

        var outline = properties?.Element(A + "ln");
        var color = SolidColor(outline) ?? "000000";
        var strokeWidth = Math.Max(.75, Px(ReadLong(outline, "w") ?? 9525) * transform.ScaleY);
        var flipH = (string?)xfrm?.Attribute("flipH") == "1";
        var flipV = (string?)xfrm?.Attribute("flipV") == "1";
        var width = Math.Max(frame.Value.Width, .01);
        var height = Math.Max(frame.Value.Height, .01);
        var x1 = flipH ? width : 0;
        var x2 = flipH ? 0 : width;
        var y1 = flipV ? height : 0;
        var y2 = flipV ? 0 : height;
        context.Html.Append(CultureInfo.InvariantCulture,
            $"<svg class=\"pbd-slide-line\" style=\"left:{N(frame.Value.X)}px;top:{N(frame.Value.Y)}px\" width=\"{N(width)}\" height=\"{N(height)}\" aria-hidden=\"true\"><line x1=\"{N(x1)}\" y1=\"{N(y1)}\" x2=\"{N(x2)}\" y2=\"{N(y2)}\" stroke=\"#{color}\" stroke-width=\"{N(strokeWidth)}\"/></svg>");
    }

    private static void RenderPicture(XElement picture, Transform transform, RenderContext context)
    {
        var frame = ReadFrame(picture.Element(P + "spPr")?.Element(A + "xfrm"), transform);
        var relationshipId = (string?)picture.Element(P + "blipFill")?.Element(A + "blip")?.Attribute(R + "embed");
        if (frame is null || relationshipId is null) return;

        var key = context.ImageKey(relationshipId);
        if (key is null) return;
        var description = (string?)picture.Element(P + "nvPicPr")?.Element(P + "cNvPr")?.Attribute("descr") ?? string.Empty;
        context.Html.Append(
            $"<img class=\"pbd-slide-picture\" style=\"{FrameStyle(frame.Value)}\" alt=\"{Encode(description)}\" data-pbd-preview-image=\"{key}\" />");
    }

    private static void RenderTable(XElement graphicFrame, Transform transform, RenderContext context)
    {
        var table = graphicFrame.Descendants(A + "tbl").FirstOrDefault();
        var frame = ReadFrame(graphicFrame.Element(P + "xfrm"), transform);
        if (table is null || frame is null) return;

        var columns = table.Element(A + "tblGrid")?.Elements(A + "gridCol")
            .Select(column => Px(ReadLong(column, "w") ?? 0) * transform.ScaleX)
            .ToArray() ?? Array.Empty<double>();
        var html = context.Html;
        html.Append(CultureInfo.InvariantCulture,
            $"<table class=\"pbd-slide-table\" style=\"left:{N(frame.Value.X)}px;top:{N(frame.Value.Y)}px;width:{N(columns.Sum())}px\"><colgroup>");
        foreach (var column in columns)
        {
            html.Append(CultureInfo.InvariantCulture, $"<col style=\"width:{N(column)}px\" />");
        }

        html.Append("</colgroup>");
        foreach (var row in table.Elements(A + "tr"))
        {
            var rowHeight = Px(ReadLong(row, "h") ?? 0) * transform.ScaleY;
            html.Append(CultureInfo.InvariantCulture, $"<tr style=\"height:{N(rowHeight)}px\">");
            foreach (var cell in row.Elements(A + "tc"))
            {
                if ((string?)cell.Attribute("hMerge") == "1" || (string?)cell.Attribute("vMerge") == "1") continue;

                var span = (int?)cell.Attribute("gridSpan") ?? 1;
                var cellProperties = cell.Element(A + "tcPr");
                var style = new StringBuilder();
                var fill = SolidColor(cellProperties);
                if (fill is not null) style.Append(CultureInfo.InvariantCulture, $"background:#{fill};");
                style.Append(CultureInfo.InvariantCulture,
                    $"padding:{N(Px(ReadLong(cellProperties, "marT") ?? 45720))}px {N(Px(ReadLong(cellProperties, "marR") ?? 91440))}px {N(Px(ReadLong(cellProperties, "marB") ?? 45720))}px {N(Px(ReadLong(cellProperties, "marL") ?? 91440))}px;");
                style.Append("vertical-align:").Append(((string?)cellProperties?.Attribute("anchor")) switch
                {
                    "ctr" => "middle",
                    "b" => "bottom",
                    _ => "top"
                }).Append(';');
                foreach (var (side, css) in new[] { ("lnL", "left"), ("lnR", "right"), ("lnT", "top"), ("lnB", "bottom") })
                {
                    var border = cellProperties?.Element(A + side);
                    var color = SolidColor(border);
                    if (color is null) continue;
                    var width = Math.Max(.75, Px(ReadLong(border, "w") ?? 9525));
                    style.Append(CultureInfo.InvariantCulture, $"border-{css}:{N(width)}px solid #{color};");
                }

                var spanAttribute = span > 1 ? $" colspan=\"{span}\"" : string.Empty;
                html.Append($"<td{spanAttribute} style=\"{style}\">");
                var body = cell.Element(A + "txBody");
                if (body is not null)
                {
                    AppendParagraphs(html, body, fontScale: 1d, lineScale: 1d, transform);
                }

                html.Append("</td>");
            }

            html.Append("</tr>");
        }

        html.Append("</table>");
    }

    private static void AppendTextBody(
        StringBuilder html,
        XElement body,
        Transform transform,
        (long Left, long Top, long Right, long Bottom) defaultInsets)
    {
        var bodyProperties = body.Element(A + "bodyPr");
        var autoFit = bodyProperties?.Element(A + "normAutofit");
        var fontScale = ((double?)ReadLong(autoFit, "fontScale") ?? 100000d) / 100000d;
        var lineScale = 1d - (((double?)ReadLong(autoFit, "lnSpcReduction") ?? 0d) / 100000d);
        var justify = ((string?)bodyProperties?.Attribute("anchor")) switch
        {
            "ctr" => "center",
            "b" => "flex-end",
            _ => "flex-start"
        };
        html.Append(CultureInfo.InvariantCulture,
            $"<div class=\"pbd-slide-text\" style=\"justify-content:{justify};padding:{N(Px(ReadLong(bodyProperties, "tIns") ?? defaultInsets.Top))}px {N(Px(ReadLong(bodyProperties, "rIns") ?? defaultInsets.Right))}px {N(Px(ReadLong(bodyProperties, "bIns") ?? defaultInsets.Bottom))}px {N(Px(ReadLong(bodyProperties, "lIns") ?? defaultInsets.Left))}px\">");
        AppendParagraphs(html, body, fontScale, lineScale, transform);
        html.Append("</div>");
    }

    private static void AppendParagraphs(
        StringBuilder html,
        XElement body,
        double fontScale,
        double lineScale,
        Transform transform)
    {
        foreach (var paragraph in body.Elements(A + "p"))
        {
            var properties = paragraph.Element(A + "pPr");
            var runs = paragraph.Elements().Where(element => element.Name == A + "r" || element.Name == A + "br").ToArray();
            var firstSize = runs
                .Select(run => ReadLong(run.Element(A + "rPr"), "sz"))
                .FirstOrDefault(size => size.HasValue)
                ?? ReadLong(paragraph.Element(A + "endParaRPr"), "sz")
                ?? 1800;
            var fontPx = firstSize / 100d * PointToPixel * fontScale * transform.ScaleY;
            var style = new StringBuilder();
            style.Append("text-align:").Append(((string?)properties?.Attribute("algn")) switch
            {
                "ctr" => "center",
                "r" => "right",
                "just" => "justify",
                _ => "left"
            }).Append(';');
            var marginLeft = Px(ReadLong(properties, "marL") ?? 0);
            var indent = Px(ReadLong(properties, "indent") ?? 0);
            if (marginLeft != 0) style.Append(CultureInfo.InvariantCulture, $"padding-left:{N(marginLeft)}px;");
            if (indent != 0) style.Append(CultureInfo.InvariantCulture, $"text-indent:{N(indent)}px;");

            var lineSpacingPoints = ReadLong(properties?.Element(A + "lnSpc")?.Element(A + "spcPts"), "val");
            var lineHeight = lineSpacingPoints.HasValue
                ? lineSpacingPoints.Value / 100d * PointToPixel * lineScale
                : fontPx * 1.2 * lineScale;
            style.Append(CultureInfo.InvariantCulture, $"line-height:{N(lineHeight)}px;font-size:{N(fontPx)}px;");
            var spaceAfter = ReadLong(properties?.Element(A + "spcAft")?.Element(A + "spcPts"), "val");
            if (spaceAfter is > 0) style.Append(CultureInfo.InvariantCulture, $"margin-bottom:{N(spaceAfter.Value / 100d * PointToPixel)}px;");
            var tab = ReadLong(properties?.Element(A + "tabLst")?.Element(A + "tab"), "pos");
            style.Append(CultureInfo.InvariantCulture, $"tab-size:{N(Math.Max(1, Px(tab ?? 914400) - marginLeft))}px;");

            html.Append($"<p style=\"{style}\">");
            var isFirstRun = true;
            foreach (var run in runs)
            {
                var isLeadRun = isFirstRun;
                isFirstRun = false;
                if (run.Name == A + "br")
                {
                    html.Append("<br />");
                    continue;
                }

                var runProperties = run.Element(A + "rPr");
                var size = (ReadLong(runProperties, "sz") ?? firstSize) / 100d * PointToPixel * fontScale * transform.ScaleY;
                var runStyle = new StringBuilder();
                runStyle.Append(CultureInfo.InvariantCulture, $"font-size:{N(size)}px;");
                if ((string?)runProperties?.Attribute("b") == "1") runStyle.Append("font-weight:700;");
                if ((string?)runProperties?.Attribute("i") == "1") runStyle.Append("font-style:italic;");
                var color = SolidColor(runProperties);
                if (color is not null) runStyle.Append(CultureInfo.InvariantCulture, $"color:#{color};");
                var text = string.Concat(run.Elements(A + "t").Select(element => element.Value));
                if (isLeadRun && indent < 0 && text.EndsWith('\t'))
                {
                    // A hanging bullet: PowerPoint tabs from the bullet to the paragraph margin, so
                    // the bullet takes the hanging width and the text starts on the margin.
                    runStyle.Append(CultureInfo.InvariantCulture, $"display:inline-block;width:{N(-indent)}px;text-indent:0;");
                    text = text.TrimEnd('\t');
                }

                html.Append($"<span style=\"{runStyle}\">{Encode(text)}</span>");
            }

            if (runs.Length == 0)
            {
                html.Append("&#8203;");
            }

            html.Append("</p>");
        }
    }

    private static string ResolveTitle(XElement? tree, int number, int total)
    {
        if (tree is null) return $"Slide {number}";

        string? TextOf(string shapeName) => tree.Descendants(P + "sp")
            .Where(shape => (string?)shape.Element(P + "nvSpPr")?.Element(P + "cNvPr")?.Attribute("name") == shapeName)
            .Select(shape => string.Concat(shape.Descendants(A + "t").Select(text => text.Value)).Trim())
            .FirstOrDefault(text => text.Length > 0);

        var title = TextOf("Slide title")
            ?? TextOf("Project sheet title")
            ?? TextOf("Closing message");
        if (title is null && number == 1) title = "Cover";
        title ??= $"Slide {number}";
        title = WhitespaceRun().Replace(title, " ");
        return title.Length > 90 ? title[..87] + "…" : title;
    }

    private static void AppendFill(StringBuilder style, XElement? properties)
    {
        if (properties?.Element(A + "noFill") is not null) return;
        var color = properties?.Element(A + "solidFill")?.Element(A + "srgbClr")?.Attribute("val")?.Value;
        if (CleanColor(color) is { } clean) style.Append(CultureInfo.InvariantCulture, $"background:#{clean};");
    }

    private static void AppendOutline(StringBuilder style, XElement? outline, Transform transform)
    {
        var color = SolidColor(outline);
        if (color is null) return;
        var width = Math.Max(.5, Px(ReadLong(outline, "w") ?? 9525) * transform.ScaleY);
        style.Append(CultureInfo.InvariantCulture, $"box-shadow:inset 0 0 0 {N(width)}px #{color};");
    }

    private static string? SolidColor(XElement? container)
        => CleanColor(container?.Element(A + "solidFill")?.Element(A + "srgbClr")?.Attribute("val")?.Value);

    private static string? CleanColor(string? value)
        => value is { Length: 6 } && HexColor().IsMatch(value) ? value.ToUpperInvariant() : null;

    private static Frame? ReadFrame(XElement? xfrm, Transform transform, bool allowEmpty = false)
    {
        if (xfrm is null) return null;
        var off = ReadPoint(xfrm.Element(A + "off"), "x", "y");
        var ext = ReadPoint(xfrm.Element(A + "ext"), "cx", "cy");
        if (!allowEmpty && (ext.X <= 0 || ext.Y <= 0)) return null;
        var (x, y) = transform.Apply(off.X, off.Y);
        return new Frame(Px(x), Px(y), Px(ext.X * transform.ScaleX), Px(ext.Y * transform.ScaleY));
    }

    private static (double X, double Y) ReadPoint(XElement? element, string xName, string yName)
        => (ReadLong(element, xName) ?? 0, ReadLong(element, yName) ?? 0);

    private static long? ReadLong(XElement? element, string attribute)
        => long.TryParse((string?)element?.Attribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static string FrameStyle(Frame frame)
        => string.Create(CultureInfo.InvariantCulture,
            $"left:{N(frame.X)}px;top:{N(frame.Y)}px;width:{N(frame.Width)}px;height:{N(frame.Height)}px;");

    private static double Px(double emu) => emu / EmuPerPixel;

    private static string N(double value)
        => Math.Round(double.IsFinite(value) ? value : 0, 2).ToString("0.##", CultureInfo.InvariantCulture);

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private readonly record struct Frame(double X, double Y, double Width, double Height);

    private readonly record struct Transform(double OffsetX, double OffsetY, double ScaleX, double ScaleY)
    {
        public static Transform Identity => new(0, 0, 1, 1);

        public (double X, double Y) Apply(double x, double y) => (OffsetX + (x * ScaleX), OffsetY + (y * ScaleY));

        // Child coordinates pass through the inner (group) transform first, then this one.
        public Transform Then(Transform inner)
            => new(OffsetX + (inner.OffsetX * ScaleX), OffsetY + (inner.OffsetY * ScaleY), ScaleX * inner.ScaleX, ScaleY * inner.ScaleY);
    }

    private sealed class RenderContext
    {
        private readonly SlidePart _slidePart;
        private readonly Dictionary<string, string> _images;
        private readonly Dictionary<string, string?> _keysByRelationship = new(StringComparer.Ordinal);

        public RenderContext(SlidePart slidePart, Dictionary<string, string> images, StringBuilder html)
        {
            _slidePart = slidePart;
            _images = images;
            Html = html;
        }

        public StringBuilder Html { get; }

        // Pictures repeat across slides (insignia on every header), so each distinct picture is
        // shrunk once and shipped once, keyed by a hash of its original bytes.
        public string? ImageKey(string relationshipId)
        {
            if (_keysByRelationship.TryGetValue(relationshipId, out var cached)) return cached;

            string? key = null;
            if (_slidePart.GetPartById(relationshipId) is ImagePart imagePart)
            {
                byte[] content;
                using (var source = imagePart.GetStream(FileMode.Open, FileAccess.Read))
                using (var buffer = new MemoryStream())
                {
                    source.CopyTo(buffer);
                    content = buffer.ToArray();
                }

                key = Convert.ToHexString(SHA256.HashData(content))[..16].ToLowerInvariant();
                if (!_images.ContainsKey(key))
                {
                    var dataUri = ToPreviewDataUri(content);
                    if (dataUri is null)
                    {
                        key = null;
                    }
                    else
                    {
                        _images[key] = dataUri;
                    }
                }
            }

            _keysByRelationship[relationshipId] = key;
            return key;
        }

        private static string? ToPreviewDataUri(byte[] content)
        {
            try
            {
                using var image = Image.Load(content);
                var isPng = image.Metadata.DecodedImageFormat?.Name == "PNG";
                if (image.Width > MaximumImageWidth)
                {
                    image.Mutate(context => context.Resize(MaximumImageWidth, 0));
                }

                using var output = new MemoryStream();
                if (isPng)
                {
                    image.Save(output, new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression });
                    return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
                }

                image.Save(output, new JpegEncoder { Quality = 74 });
                return "data:image/jpeg;base64," + Convert.ToBase64String(output.ToArray());
            }
            catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException)
            {
                return null;
            }
        }
    }

    [GeneratedRegex("^[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}

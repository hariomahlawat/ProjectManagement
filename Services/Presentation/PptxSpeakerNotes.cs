using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;

namespace ProjectManagement.Services.Presentation;

// SECTION: Speaker notes (one implementation for the generated decks: Briefing Studio, the CoE slides and the ARPP progress slides)
/// <summary>
/// Writes speaker notes onto a generated slide. The bundled templates have no notes master, so one
/// is added (themed like the slides) the first time a deck carries notes. Each line of the notes is
/// its own paragraph; a blank line stays blank.
/// </summary>
public static class PptxSpeakerNotes
{
    public static void Add(PresentationPart presentationPart, SlidePart slidePart, string notes)
    {
        ArgumentNullException.ThrowIfNull(presentationPart);
        ArgumentNullException.ThrowIfNull(slidePart);
        if (string.IsNullOrWhiteSpace(notes))
        {
            return;
        }

        var notesMaster = EnsureNotesMaster(presentationPart);
        var notesPart = slidePart.NotesSlidePart ?? slidePart.AddNewPart<NotesSlidePart>();
        notesPart.AddPart(notesMaster);
        notesPart.AddPart(slidePart);
        FeedXml(notesPart, BuildNotesSlideXml(notes));
    }

    private static NotesMasterPart EnsureNotesMaster(PresentationPart presentationPart)
    {
        if (presentationPart.NotesMasterPart is { } existing)
        {
            return existing;
        }

        var notesMaster = presentationPart.AddNewPart<NotesMasterPart>();
        var sourceTheme = presentationPart.SlideMasterParts.Select(master => master.ThemePart).FirstOrDefault(part => part is not null)
            ?? presentationPart.ThemePart;
        if (sourceTheme is not null)
        {
            var themePart = notesMaster.AddNewPart<ThemePart>();
            using var themeStream = sourceTheme.GetStream(FileMode.Open, FileAccess.Read);
            themePart.FeedData(themeStream);
        }

        var notesSize = presentationPart.Presentation.NotesSize;
        FeedXml(notesMaster, BuildNotesMasterXml(
            notesSize?.Cx?.Value ?? 6_858_000L,
            notesSize?.Cy?.Value ?? 9_144_000L));

        var presentation = presentationPart.Presentation;
        var notesMasterIds = presentation.NotesMasterIdList;
        if (notesMasterIds is null)
        {
            notesMasterIds = new NotesMasterIdList();
            if (presentation.SlideMasterIdList is { } slideMasterIds)
            {
                presentation.InsertAfter(notesMasterIds, slideMasterIds);
            }
            else
            {
                presentation.PrependChild(notesMasterIds);
            }
        }

        notesMasterIds.RemoveAllChildren<NotesMasterId>();
        notesMasterIds.Append(new NotesMasterId { Id = presentationPart.GetIdOfPart(notesMaster) });
        return notesMaster;
    }

    private static void FeedXml(OpenXmlPart part, string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        part.FeedData(stream);
    }

    private const string PresentationNamespaces =
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
        + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" "
        + "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"";

    private static string BuildNotesMasterXml(long width, long height)
    {
        // Slide image above, notes below, the usual notes-page arrangement.
        var imageWidth = (long)(width * 0.88);
        var imageHeight = imageWidth * 9 / 16;
        var imageX = (width - imageWidth) / 2;
        var imageY = (long)(height * 0.075);
        var bodyX = (long)(width * 0.1);
        var bodyY = imageY + imageHeight + (long)(height * 0.05);
        var bodyWidth = width - (2 * bodyX);
        var bodyHeight = (long)(height * 0.92) - bodyY;
        return $"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:notesMaster {PresentationNamespaces}>
  <p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr>
    <p:sp><p:nvSpPr><p:cNvPr id="2" name="Slide Image Placeholder 1"/><p:cNvSpPr><a:spLocks noGrp="1" noRot="1" noChangeAspect="1"/></p:cNvSpPr><p:nvPr><p:ph type="sldImg" idx="2"/></p:nvPr></p:nvSpPr><p:spPr><a:xfrm><a:off x="{imageX}" y="{imageY}"/><a:ext cx="{imageWidth}" cy="{imageHeight}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/><a:ln w="12700"><a:solidFill><a:prstClr val="black"/></a:solidFill></a:ln></p:spPr></p:sp>
    <p:sp><p:nvSpPr><p:cNvPr id="3" name="Notes Placeholder 2"/><p:cNvSpPr><a:spLocks noGrp="1"/></p:cNvSpPr><p:nvPr><p:ph type="body" sz="quarter" idx="3"/></p:nvPr></p:nvSpPr><p:spPr><a:xfrm><a:off x="{bodyX}" y="{bodyY}"/><a:ext cx="{bodyWidth}" cy="{bodyHeight}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></p:spPr><p:txBody><a:bodyPr vert="horz" lIns="91440" tIns="45720" rIns="91440" bIns="45720" rtlCol="0"/><a:lstStyle/><a:p><a:pPr lvl="0"/><a:r><a:rPr lang="en-IN"/><a:t>Speaker notes</a:t></a:r></a:p></p:txBody></p:sp>
  </p:spTree></p:cSld>
  <p:clrMap bg1="lt1" tx1="dk1" bg2="lt2" tx2="dk2" accent1="accent1" accent2="accent2" accent3="accent3" accent4="accent4" accent5="accent5" accent6="accent6" hlink="hlink" folHlink="folHlink"/>
  <p:notesStyle><a:lvl1pPr marL="0" algn="l" defTabSz="914400" rtl="0" eaLnBrk="1" latinLnBrk="0" hangingPunct="1"><a:defRPr sz="1200" kern="1200"><a:solidFill><a:schemeClr val="tx1"/></a:solidFill><a:latin typeface="+mn-lt"/><a:ea typeface="+mn-ea"/><a:cs typeface="+mn-cs"/></a:defRPr></a:lvl1pPr></p:notesStyle>
</p:notesMaster>
""";
    }

    private static string BuildNotesSlideXml(string notes)
    {
        var paragraphs = notes
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => string.IsNullOrWhiteSpace(line)
                ? "<a:p><a:endParaRPr lang=\"en-IN\" dirty=\"0\"/></a:p>"
                : $"<a:p><a:r><a:rPr lang=\"en-IN\" dirty=\"0\"/><a:t>{SecurityElement.Escape(SanitizeText(line.TrimEnd()))}</a:t></a:r></a:p>");
        return $"""
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<p:notes {PresentationNamespaces}>
  <p:cSld><p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr>
    <p:sp><p:nvSpPr><p:cNvPr id="2" name="Slide Image Placeholder 1"/><p:cNvSpPr><a:spLocks noGrp="1" noRot="1" noChangeAspect="1"/></p:cNvSpPr><p:nvPr><p:ph type="sldImg"/></p:nvPr></p:nvSpPr><p:spPr/></p:sp>
    <p:sp><p:nvSpPr><p:cNvPr id="3" name="Notes Placeholder 2"/><p:cNvSpPr><a:spLocks noGrp="1"/></p:cNvSpPr><p:nvPr><p:ph type="body" idx="1"/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/>{string.Concat(paragraphs)}</p:txBody></p:sp>
  </p:spTree></p:cSld>
  <p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>
</p:notes>
""";
    }

    /// <summary>Replaces characters XML cannot carry (control characters, lone surrogates) with a space.</summary>
    public static string SanitizeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            var scalar = rune.Value;
            if (scalar is 0x9 or 0xA or 0xD
                || scalar is >= 0x20 and <= 0xD7FF
                || scalar is >= 0xE000 and <= 0xFFFD
                || scalar is >= 0x10000 and <= 0x10FFFF)
            {
                builder.Append(rune.ToString());
            }
            else
            {
                builder.Append(' ');
            }
        }

        return builder.ToString();
    }
}
// END SECTION

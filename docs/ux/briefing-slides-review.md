# Briefing decks: slide review (17 Nov 2026)

The owner asked for every generated slide to be reviewed for four things:

1. Space used well, so text is larger and easy to read. Few slides; a slide split in two should be
   very rare.
2. Tables: use the space, larger text, 10–12 rows on one slide.
3. A professional, polished look.
4. Easy to edit in PowerPoint.

And: **photos are never cropped by PRISM.**

## How it was reviewed

The demo deck (49 projects) was generated from the local throwaway copy of the dev database in
every project-slide design, with every slide switched on, and each slide was exported through
PowerPoint itself (1600×900):

| Deck | Design | Slides |
| --- | --- | --- |
| As stored | Key points (no status table, no category slides) | 58 |
| All slides on | Brief | 68 |
| All slides on | Capability | 68 |
| All slides on | Key points only | 68 |
| All slides on | Fact sheet | 54 |

The slide XML was inspected for how it edits in PowerPoint. Theme: Graphite Dark (Editorial Light
uses the same layouts). Many demo projects have no photo and no capability text; findings below are
about layout, not about that demo data.

## Findings

### Photos (owner rule: never crop)

- **P1. Every project photo is cut to 16:9 from the centre** before it reaches a slide
  (`ProjectBriefingPhotoLoader.cs:133`, `PrismImageCodec.ResizeToCover(image, 1600, 900)`). A
  portrait or 4:3 photo loses its top and bottom, or its sides.
- **P2. The fact sheet crops again** to its own frame (`ProjectBriefingSlideComposer.UpdateSheet.cs:148-179`),
  so its photos come out as a wide strip.
- The slides then draw the 16:9 picture stretched to its box (`AddImage`, `a:stretch`), which is
  only safe because P1 already cut it to 16:9.

**Fix:** load the whole photo (resized to fit, never cropped) and place it contained in its frame,
centred, on the frame's own colour; the frame's shape follows the photo where the layout allows.
The person can still crop in PowerPoint (Picture Format › Crop) if they want to.

### Space and text size

| Slide | What we see | Text now | Fix |
| --- | --- | --- | --- |
| Cover | Left half empty apart from the name; figures panel with large gaps. | name 28pt, rest 9–11pt | Larger deck name and subtitle, figures panel tighter, generated date readable. |
| Role & Charter | Nearly half of each panel empty. | 11.5pt | Text grows to fill the panels (to about 16pt). |
| SDD profile | Cards half empty, list text tiny; one header red on dark red, hard to read. | 9–10.8pt | Larger figures and lists; fix the header contrast. |
| Stage-wise distribution (chart) | Good use of space. | axis 9–11pt | Larger axis and band labels. |
| Stage-wise list | An empty "Completed" column takes a quarter of the width while "Under development" (21) spills onto a **second slide**. | 10–12pt | Columns sized by how many projects each band has; empty bands narrow; long bands flow into two sub-columns; one slide. |
| Category distributions (2) | Thin bars and small labels; most of the slide empty. | 9–11pt | Thicker bars, larger labels, the note larger. |
| Status table | **6 rows per slide**: 49 projects need **9 slides**. Rows twice the height their text needs; bottom quarter empty; Proliferation cost column all "—". | 10–10.7pt | **12 rows per slide at 12pt** (49 projects: 5 slides); status up to 3 lines; a column with no values is left out. |
| Project · Capability | Photo and text side by side; capability text panel often half empty. | 16pt | Text sized to its panel. |
| Project · Key points | Key points in a band at the foot in small text, while the panels above have room. | points 9–11pt | Points at 13–14pt; the band takes the height the panels do not use. |
| Project · Key points only | Reads well; photo floats with empty bands above and below. | 11pt | Photo fills the column height; points a little larger. |
| Project · Brief | Brief text panel half empty. | 15pt | Text sized to its panel. |
| Project · Fact sheet | Facts table small; brief panel two-thirds empty; photo cropped to a strip (P2). | 12–14pt | Facts and brief larger; photo whole; panel heights follow content. |
| Capability **continued** | Slide 40 shows two bullets with the panel mostly empty, then slide 41 carries four bullets alone on an otherwise empty slide. Its header differs from slide 40's. | 16pt | **Paginator bug**: fit all six on one slide (step text down to a floor first). |
| FFC footprint | Good; country list small. | 9–10pt | Larger list text. |
| Closing | Fine. | — | — |

**Continuation slides today** (each creates a second slide for one item): capability, brief,
fact-sheet brief, Role & Charter, FFC breakdown and the stage-wise list. The rule should be:
**fit first** (use the free space, then step the text down to a readable floor: 14pt body, 12pt
tables and lists), and **split only when it still does not fit**. When a split happens, the
continuation carries the same header as the first slide.

### Professional look

- A consistent type scale: titles 24pt, body 14–18pt, tables 12–13pt, captions 10pt minimum (no
  9pt text anywhere a reader must read).
- Equal margins and gutters on every slide (today some panels sit at different insets).
- Headers that read: the SDD profile's "Assistance to field formations" header (red on dark red)
  fails contrast.
- Placeholder messages ("Capability overview not recorded", "Photograph not available") are fine
  as gaps, but should be quieter than real content, not the same size as a heading.

### Easy to edit in PowerPoint

- **E1. No placeholders.** Every slide uses the blank layout and its title is a plain text box, so
  PowerPoint's outline view, navigation pane and screen readers see no slide titles.
- **E2. The slide furniture is drawn on every slide** (background, accent bar, insignia plates and
  images, rule lines, footer). Changing a logo or the footer means editing every slide, and the
  pieces can be knocked out of place. They belong on the slide master and layouts.
- **E3. Page numbers are typed** ("15/68"), not slide-number fields: they go wrong when a slide is
  removed or moved.
- **E4. Bullets are typed characters** ("•", "✓") at the start of the text, not paragraph bullets:
  a new line gets no bullet or hanging indent.
- **E5. Charts are drawn shapes** (the stage chart is 65 shapes, the SDD profile 50): nobody can
  correct a number in PowerPoint. The stage chart and the two category distributions should be
  native PowerPoint charts (their data editable in PowerPoint).
- Good already: tables are native PowerPoint tables; shapes have meaningful names; text boxes
  use shrink-on-overflow.

## Plan

Each step is one PR, checked by exporting the demo decks through PowerPoint as above.

1. **Photos never cropped** (P1, P2). Fit, not cover; contained in the frame; tests that a
   portrait photo keeps its full height.
2. **Fit before split** (the paginator bug and the continuation rule). One fitting routine for
   text panels: shrink to the floor, split only beyond it, same header on a continuation.
3. **Status table: 12 rows at 12pt** (and empty columns left out); stage-wise list on one slide.
   Built: rows as tall as their text (cost and basis on one line, name up to two lines, status
   up to three, cut after a whole word), up to 12 a slide, pages of even height. The demo deck's
   49 projects take 5 slides of 9–11 rows. The stage-wise list gives an empty band a narrow column
   and lets a long band take two or more columns side by side, so it stays on one slide; only a
   list that cannot fit even then continues as before.
4. **Type scale and space**: cover, Role & Charter, SDD profile, category distributions, FFC list,
   key points band, Key points only photo, Brief and Fact sheet panels.
   Part 1 built: on the project slides the key points band is 14 pt (labels 12.5 pt, was 11 and
   10), the stage and cost line 12.5 pt (was 10.5), and a brief takes the largest size up to 20 pt
   that fits its panel (was 15 pt at most).
   Category distributions built (owner, 17 Nov: "too simplistic ... no oomph"): each is a native
   PowerPoint doughnut (embedded workbook, so Edit Data works) with the total in its hole, a
   headline naming the largest category, and a legend of every category with count and share.
   Both chosen share one slide, "Project and technical categories", side by side; one alone takes
   the slide with a larger ring and its concentration note. Beyond nine slices the smallest are
   one "Other categories" slice; "Not categorised" is always grey and last. The builder's preview
   draws the rings as SVG. Owner, 17 Nov: the project-category ring counts parent categories only
   (CoE Mid Term, CoE Short Term and CoE are one CoE), in the stage-wise chart's colours.
   Role and Charter: the charter takes the largest size that fits its panels, up to 20 pt (was
   13.2 pt at most, leaving half of each panel empty); the CHARTER heading rule runs full width.
   FFC Global Footprint (owner, 17 Nov: "the map needs to be the main thing", every country on
   one slide, countries across the top and installed, delivered, planned and total down the side):
   one design. The figures in a line under the title with the status bar; the map as a band across
   the full width, drawn in the deck's theme with each country named and its quantity; below it a
   native table with a column per country (largest first, up to 16, then "Others (n)") and rows
   Installed, Delivered, Planned and Total. Official names show their short form ("Tanzania").
   The editor's layout and row-count choices are retired (kept as saved, hidden); the separate
   country-wise breakdown slide still works for decks that use it.
   Cover (owner, 17 Nov, second pass): no figures. The division's own logo (sdd.png) large and
   centred, "SIMULATOR DEVELOPMENT DIVISION" as the largest text (44 pt), an accent rule, the
   deck's name as typed (28 pt down to 18 pt, two lines), the deck's description as an optional
   occasion or presenter line, and the date written in full; a handling marking at top and foot. SDD profile (owner, 17 Nov: the timeline "more better
   and wow"): the history as a journey rail, each year a solid pill on a heavy rail with its
   milestone beneath, the latest year in gold; the cards' text larger (34 pt headlines, 13 pt
   rows), their titles white on the dark theme, and the cards running down to the footer.
5. **Editable structure**: slide master and layouts carry the furniture; title placeholders; real
   slide-number fields; real bullets.
   Part 1 built: each slide's title is PowerPoint's title placeholder (outline view, accessibility
   checks and Reuse Slides see it); the slide number is PowerPoint's slide-number field (it follows
   when slides are added or moved; the "/62" count is the deck's size when made); list bullets in
   narrative text and the profile's partnerships are real bullets. Numbered and lettered items keep
   typed markers, so a list continued on the next slide keeps its numbers. The builder's preview
   draws fields and bullets.
   Part 2 built: each deck gets its own layouts, made as its slides need them ("PRISM · Title and
   content", "· FFC footprint", "· Project update sheet", each with or without insignia, and
   "· Plain" for the cover and closing). A layout carries the background, top rule, insignia,
   header rule, footer rule and the division's name, plus title and content placeholders, so the
   furniture stays put while a slide is edited and a slide added with New Slide comes branded. The
   template's generic 4:3 layouts are removed. Slides keep their own title, content, slide number,
   marking and data-as-on note. The builder's preview draws each slide's layout under it.
6. **Native charts** for the stage chart (the two category distributions are done, above).
   Built: the stage-wise chart is a native stacked column chart, a series per parent category in
   the stage chart's colours, the counts inside, each stage's total above from a line series, and
   the figures in an embedded workbook whose Total column sums each row (Edit Data moves a bar and
   its total together). The bands stay shapes behind the chart, its inner plot set on their slots;
   stages now take equal slots. The builder's preview draws stacked columns.
7. **Re-review**: export every design again and compare.
   Done 17 Nov: all five project designs (Capability, Key points, Brief, Key points only, Fact
   sheet) and every deck slide exported in both themes from the demo deck. No overflow, no cropped
   photo, no split that fitting could avoid. Four fixes:
   - Role and Charter on the dark theme: the lead phrases were in the header's maroon, hard to
     read on dark; they now use a lighter rose there (the light theme is unchanged).
   - Stage-wise list: the text was about 10 pt with the lower third of the slide empty; it now
     takes the largest size up to 1.5 times that at which it still fits on one slide.
   - Fact sheet: a sheet of five or six rows sets them 30% larger (up to nine rows, 15%), and the
     brief tries 22, 20, 18 and 16 pt before its usual size, so a short brief fills its panel.
   - Key points only: the cards take the largest text from 17 pt down to 11 pt that fits, with a
     12 pt heading from 14 pt text upwards.
   Not design faults: some demo key points are placeholder text, and some projects have no brief.
   Follow-up (owner, 17 Nov: "especially key points only"): its cards still showed text smaller
   than set. Every card carried a fixed 94% shrink, and the fit was measured at .50 em without word
   breaks, so one long point set the whole slide at 13–15 pt with half of each card empty. Now the
   cards are measured at .45 em (Aptos averages .42 em) wrapped at word breaks, carry no preset
   shrink, and PowerPoint shrinks a card only if it overflows: 24 of the demo's 37 slides with key
   points at 17 pt, none below 14 pt.

Steps 1–3 change what a deck says least and help most; 5 and 6 are the largest.

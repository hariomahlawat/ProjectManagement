using System;
using System.IO;
using Xunit;

namespace ProjectManagement.Tests.ProjectBriefings;

public sealed class ProjectBriefingContractTests
{
    [Fact]
    public void Builder_UsesApprovedTerminologySelectionMethodsAndExternalStatusPolicy()
    {
        var page = Read("Index.cshtml");

        Assert.Contains("\"Briefing decks | Comdt / HoD Workspace\"", page, StringComparison.Ordinal);
        Assert.Contains("Briefing decks", Read("_CommandWorkspaceRail.cshtml"), StringComparison.Ordinal);
        Assert.Contains("All ongoing projects", page, StringComparison.Ordinal);
        Assert.Contains("Recently completed", page, StringComparison.Ordinal);
        Assert.Contains("Project category", page, StringComparison.Ordinal);
        Assert.Contains("Technical category", page, StringComparison.Ordinal);
        Assert.Contains("Available for proliferation", page, StringComparison.Ordinal);
        Assert.Contains("Manage individually", page, StringComparison.Ordinal);
        Assert.Contains("Cost (R&amp;D)", page, StringComparison.Ordinal);
        Assert.Contains("Generate PowerPoint", page, StringComparison.Ordinal);
        Assert.Contains("<legend class=\"form-label\">Template</legend>", page, StringComparison.Ordinal);
        Assert.Contains("Shared decks", page, StringComparison.Ordinal);
        Assert.Contains("Add projects", page, StringComparison.Ordinal);
        Assert.Contains("recommended chart", page, StringComparison.Ordinal);
        Assert.Contains("optional supporting slide", page, StringComparison.Ordinal);
    }


    [Fact]
    public void Builder_ReplacesTheLongPageWithTheStudio()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");

        // The checks section, projects table and Add projects panel are retired; the Studio and
        // the Add projects dialog take their place.
        Assert.DoesNotContain("Content used by this presentation", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-pbd-selected-section", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-pbd-selector-details", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Manage this deck", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-studio", page, StringComparison.Ordinal);
        Assert.Contains("id=\"pbd-add-projects-modal\"", page, StringComparison.Ordinal);
        Assert.Contains("Gaps show as placeholders on the slides.", page, StringComparison.Ordinal);
        // The landing page is the deck library; the Studio switches decks from its own bar.
        Assert.Contains("<partial name=\"_DeckLibrary\"", page, StringComparison.Ordinal);
        Assert.Contains("Switch deck", page, StringComparison.Ordinal);
        // The way back must not inherit the open deck's id from the route (it would reload the deck).
        Assert.Contains("class=\"pbd-studio-nav__back\" href=\"@Url.Content(\"~/Workspace/BriefingDecks\")\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-pbd-decks-toggle", page, StringComparison.Ordinal);
        Assert.DoesNotContain("is-decks-collapsed", script, StringComparison.Ordinal);

        // Additional slides live in the inspector's Deck tab, before the Checks tab.
        var deckPanel = page.IndexOf("data-pbd-inspector-panel=\"deck\"", StringComparison.Ordinal);
        var additional = page.IndexOf("id=\"pbd-additional-slides\"", StringComparison.Ordinal);
        var checksPanel = page.IndexOf("data-pbd-inspector-panel=\"checks\"", StringComparison.Ordinal);
        Assert.True(deckPanel >= 0 && deckPanel < additional && additional < checksPanel,
            "The additional-slide cards belong to the Deck tab.");

        // The deleted/archived notice moved into the Studio stage.
        Assert.Contains("pbd-stage__notice", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-settings-form", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-preset=", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-standard-section", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-settings-collapsible", page, StringComparison.Ordinal);
    }


    [Fact]
    public void FirstRun_ChoosesATemplateThenAddsProjects()
    {
        var page = Read("Index.cshtml");
        var model = Read("Index.cshtml.cs");
        var script = Read("project-briefing-decks.js");
        var studio = Read("project-briefing-studio.js");

        // New deck: name and template, then the Studio opens on Add projects.
        Assert.Contains("<input type=\"radio\" name=\"Template\" value=\"@preset.Preset\"", page, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingDeckPresets.Templates.Select((preset, index) => (preset, index))", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Set up by hand", page, StringComparison.Ordinal);
        Assert.Contains("Create and add projects", page, StringComparison.Ordinal);
        Assert.Contains("return RedirectToPage(new { deckId, start = \"projects\" });", model, StringComparison.Ordinal);
        Assert.Contains("data-pbd-start=\"@(Model.StartWithAddProjects ? \"projects\" : null)\"", page, StringComparison.Ordinal);
        Assert.Contains("url.searchParams.delete('start');", script, StringComparison.Ordinal);

        // One empty state with one action, and no slide count while there are no slides.
        Assert.Contains("'Add the projects to brief'", studio, StringComparison.Ordinal);
        Assert.Contains("add.dataset.pbdOpenSelector = '';", studio, StringComparison.Ordinal);
        Assert.Contains("event.target.closest('[data-pbd-open-selector]')", script, StringComparison.Ordinal);
        Assert.Contains("@(presentedProjects!.Length == 0 ? null : deck.SlideEstimate.TotalSlides)", page, StringComparison.Ordinal);

        // The placeholder note shows only when something is missing.
        Assert.Contains("<small data-pbd-placeholder-note hidden>", page, StringComparison.Ordinal);
        Assert.Contains("placeholderNote.hidden = openChecks === 0;", studio, StringComparison.Ordinal);

        // A template asks first on a customised deck and can be undone straight after.
        Assert.Contains("Keep my settings", script, StringComparison.Ordinal);
        Assert.Contains("asp-page-handler=\"UndoPreset\"", page, StringComparison.Ordinal);
        Assert.Contains("OnPostUndoPresetAsync", model, StringComparison.Ordinal);
        Assert.Contains("root.querySelector('[data-pbd-preset-undo]')?.remove();", script, StringComparison.Ordinal);

        // Success messages clear themselves.
        Assert.Contains("data-pbd-auto-dismiss", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Studio_GuidesTheWorkAndSaysHowTheDeckBehaves()
    {
        var page = Read("Index.cshtml");
        var library = Read("_DeckLibrary.cshtml");
        var script = Read("project-briefing-decks.js");
        var studio = Read("project-briefing-studio.js");

        // The progress line: projects, checks, settings, generate, each opening where it is done.
        Assert.Contains("<nav class=\"pbd-progress\" aria-label=\"Deck progress\" data-pbd-progress>", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-progress-tab=\"checks\"", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-progress-tab=\"deck\"", page, StringComparison.Ordinal);
        // Generate is the button beside the progress line, not a step of it (review PR E).
        Assert.DoesNotContain("data-pbd-progress-generate", page, StringComparison.Ordinal);
        Assert.Contains("const renderProgress = (presented, openChecks) =>", studio, StringComparison.Ordinal);
        Assert.Contains("progressTemplate.textContent", script, StringComparison.Ordinal);

        // How the deck behaves, said once, and the save state by the title.
        Assert.Contains("data-pbd-model-note hidden", page, StringComparison.Ordinal);
        Assert.Contains("'prism.briefingDecks.modelNoteSeen'", script, StringComparison.Ordinal);
        Assert.Contains("<span data-pbd-saved-chip-text>Saved · shared</span>", page, StringComparison.Ordinal);
        Assert.Contains("showSaveState('saving');", script, StringComparison.Ordinal);
        // A refused settings save fails inside the queue, before the chip says "Saved".
        var settingsSave = script[script.IndexOf("const saveSettingsOnce", StringComparison.Ordinal)..];
        Assert.True(
            settingsSave.IndexOf("if (!reply.ok) throw", StringComparison.Ordinal)
                < settingsSave.IndexOf("return { response: reply, payload: replyPayload };", StringComparison.Ordinal),
            "The settings save must throw on a refused reply inside runDeckMutation.");

        // Gaps live in This slide only, each fix saying where it lands.
        Assert.DoesNotContain("data-pbd-stage-checks", page, StringComparison.Ordinal);
        Assert.Contains("const FIX_IN_PROJECT = 'Fix in project';", studio, StringComparison.Ordinal);
        Assert.Contains("const THIS_DECK_ONLY = 'This deck only';", studio, StringComparison.Ordinal);

        // Add projects: a quick add says it is done; Escape closes even when focus left the dialog.
        Assert.Contains("document.createTextNode(' Added')", script, StringComparison.Ordinal);
        Assert.Contains("addProjectsModal?.hide();", script, StringComparison.Ordinal);

        // The library explains the empty slide count.
        // The PowerPoint column says it in words instead of a dash with a tooltip (library PR 2).
        Assert.Contains("\"No projects yet\" : \"Not generated\"", library, StringComparison.Ordinal);
    }

    [Fact]
    public void Present_FillsTheScreenWithNotesOnRequest()
    {
        var page = Read("Index.cshtml");
        var studio = Read("project-briefing-studio.js");

        // Full screen where allowed; leaving it (Esc) ends the presentation and returns to the Studio.
        Assert.Contains("screenRoot.requestFullscreen({ navigationUI: 'hide' })", studio, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('fullscreenchange'", studio, StringComparison.Ordinal);
        Assert.Contains("document.exitFullscreen?.()", studio, StringComparison.Ordinal);

        // The strip and the speaker notes wait until asked for; From the start sits in the toolbar.
        Assert.Contains("<ol class=\"pbd-viewer__strip\" data-pbd-viewer-strip aria-label=\"All slides\" hidden></ol>", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-viewer-notes hidden", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-viewer-first", page, StringComparison.Ordinal);
        Assert.Contains("aria-keyshortcuts=\"N\" data-pbd-viewer-notes-toggle", page, StringComparison.Ordinal);
        Assert.Contains("const presentPrefsKey = 'prism.briefingDecks.present';", studio, StringComparison.Ordinal);
    }

    [Fact]
    public void Phone_PutsTheSlideFirstWithTheOutlineAsASheet()
    {
        var page = Read("Index.cshtml");
        var studio = Read("project-briefing-studio.js");
        var css = Read("project-briefing-decks.css").Replace("\r\n", "\n");

        // The outline folds into a sheet under the slide, opened with Show slides.
        Assert.Contains("aria-controls=\"pbd-outline-sheet\" data-pbd-outline-toggle", page, StringComparison.Ordinal);
        Assert.Contains("id=\"pbd-outline-sheet\"", page, StringComparison.Ordinal);
        Assert.Contains(".pbd-studio__outline:not(.is-open) > :not(.pbd-outline__header) {", css, StringComparison.Ordinal);

        // The phone rules come after the sticky side columns, so they win.
        var phone = css.LastIndexOf("@container pbd-studio (max-width: 699.98px) {", StringComparison.Ordinal);
        Assert.True(phone > css.IndexOf("max-height: calc(100vh - var(--topbar-height, 56px) - 1.5rem);", StringComparison.Ordinal));
        Assert.Contains("order: -1;", css[phone..], StringComparison.Ordinal);

        // Picking a slide folds the sheet; All slides is not restored on narrow screens.
        Assert.Contains("if (outlineIsSheet()) {", studio, StringComparison.Ordinal);
        Assert.Contains("savedPlace.view === 'grid' && !narrowScreen()", studio, StringComparison.Ordinal);
    }

    [Fact]
    public void Walkthrough_StudioHeaderIsOneBlockAndFixesFlowBack()
    {
        var page = Read("Index.cshtml");
        var studio = Read("project-briefing-studio.js");
        var css = Read("project-briefing-decks.css");

        // No row of its own above the deck header: the way back is in the header, Switch deck
        // beside Generate, New deck in the deck menu.
        Assert.DoesNotContain("<nav class=\"pbd-studio-nav\"", page, StringComparison.Ordinal);
        var header = page[page.IndexOf("<header class=\"pbd-deck-header\">", StringComparison.Ordinal)..page.IndexOf("</header>", page.IndexOf("<header class=\"pbd-deck-header\">", StringComparison.Ordinal), StringComparison.Ordinal)];
        Assert.Contains("class=\"pbd-studio-nav__back\"", header, StringComparison.Ordinal);
        Assert.Contains("Switch deck", header, StringComparison.Ordinal);
        Assert.Contains("data-bs-target=\"#pbd-new-deck-modal\"><i class=\"bi bi-plus-lg\" aria-hidden=\"true\"></i> New deck</button>", header, StringComparison.Ordinal);

        // A fix made in the project's tab shows when the person comes back.
        Assert.Contains("document.addEventListener('visibilitychange', rebuildOnReturn);", studio, StringComparison.Ordinal);
        Assert.Contains("href: (p) => `${p.openUrl}#remarks`", studio, StringComparison.Ordinal);

        // Added stays readable when focused; the header icon shows in dark theme.
        Assert.Contains(".pbd-quick-card .btn.is-added:focus,", css, StringComparison.Ordinal);
        Assert.Contains("color: var(--pbd-surface);\n    font-size: 1.13rem;", css.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.DoesNotContain(":where([data-bs-theme=\"dark\"]) .pbd-deck-header__mark {", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Walkthrough_LibraryRowsStayOneLineAndAddProjectsHasDone()
    {
        var page = Read("Index.cshtml");
        var library = Read("_DeckLibrary.cshtml");
        var script = Read("project-briefing-decks.js");
        var css = Read("project-briefing-decks.css").Replace("\r\n", "\n");

        // Library: dates without this year's year, owner truncated with the full name on hover,
        // and no "Not generated yet" repeated in Data since.
        Assert.Contains("return ist.Year == todayIst.Year ? ist.ToString(\"dd MMM\") : ist.ToString(\"dd MMM yy\");", library, StringComparison.Ordinal);
        Assert.Contains("<td class=\"is-edited\" data-label=\"Edited\"", library, StringComparison.Ordinal);
        Assert.DoesNotContain("Not generated yet", library, StringComparison.Ordinal);
        Assert.Contains(".pbd-library__table .is-date {\n    white-space: nowrap;", css, StringComparison.Ordinal);

        // The outline's "projects added" confirmation clears itself; errors stay.
        Assert.Contains("if (state === 'saved' && element.matches('[data-pbd-membership-status]'))", script, StringComparison.Ordinal);

        // Add projects can be finished with Done.
        Assert.Contains("data-bs-dismiss=\"modal\" data-pbd-add-projects-done>Done</button>", page, StringComparison.Ordinal);
    }

    [Fact]
    public void AddProjects_KeepsItsTabsUndoesBulkAddsAndTellsPendingFromSaved()
    {
        var script = Read("project-briefing-decks.js");
        var css = Read("project-briefing-decks.css").Replace("\r\n", "\n");

        // The tab strip cannot shrink, and only the dialog scrolls.
        Assert.Contains(".pbd-add-projects .modal-body {\n    grid-auto-rows: max-content;", css, StringComparison.Ordinal);
        Assert.Contains(".pbd-add-projects .pbd-search-results {\n    max-height: none;", css, StringComparison.Ordinal);

        // A quick or category add can be undone: exactly the projects it brought in.
        Assert.Contains("if (added.length) offerAddUndo(added, button);", script, StringComparison.Ordinal);
        Assert.Contains("await updateMembership([], added, addProjectsStatus);", script, StringComparison.Ordinal);

        // Saved and pending told apart; the deck's own projects listed before any search.
        Assert.Contains("? (desired ? ['In deck', 'is-in-deck'] : ['Will be removed', 'is-pending-remove'])", script, StringComparison.Ordinal);
        Assert.Contains(": (desired ? ['Will be added', 'is-pending-add'] : ['', '']);", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'NOT IN DECK'", script, StringComparison.Ordinal);
        Assert.Contains("renderSearchResults(deckRowsFrom(latestProjects));", script, StringComparison.Ordinal);
        // Codex review: the Undo baseline is taken inside the queued save; an empty deck's list fills in after an add.
        var addHandler = script[script.IndexOf("let membersBefore = new Set();", StringComparison.Ordinal)..];
        Assert.True(addHandler.IndexOf("membersBefore = new Set(latestProjects", StringComparison.Ordinal)
            > addHandler.IndexOf("await runDeckMutation(async () => {", StringComparison.Ordinal));
        Assert.Contains("if (blankQuery && !showingDeckRows) {", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_StudioHeaderCompactOutlineReadableAndKeysWork()
    {
        var page = Read("Index.cshtml");
        var studio = Read("project-briefing-studio.js");
        var css = Read("project-briefing-decks.css").Replace("\r\n", "\n");

        // The progress line sits in the deck header with the last change, not in a row of its own.
        var header = page[page.IndexOf("<header class=\"pbd-deck-header\">", StringComparison.Ordinal)..page.IndexOf("</header>", page.IndexOf("<header class=\"pbd-deck-header\">", StringComparison.Ordinal), StringComparison.Ordinal)];
        Assert.Contains("<div class=\"pbd-deck-header__status\">", header, StringComparison.Ordinal);
        Assert.Contains("data-pbd-progress", header, StringComparison.Ordinal);

        // Outline titles on two lines.
        Assert.Contains("-webkit-line-clamp: 2;", css, StringComparison.Ordinal);

        // Arrows from anywhere outside fields; the outline and the Checks filter survive a reload.
        Assert.Contains("document.addEventListener('keydown', (event) => {\n    if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;", studio.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("root.addEventListener('keydown'", studio, StringComparison.Ordinal);
        Assert.Contains("filter: filterGap || null", studio, StringComparison.Ordinal);
        Assert.Contains("centreSelectedRow();", studio, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_GapsHaveOneNameAndAreSaidBeforePresentingOrGenerating()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");
        var studio = Read("project-briefing-studio.js");

        // One word: gaps.
        Assert.Contains("data-pbd-inspector-tab=\"checks\" tabindex=\"-1\">Gaps <span", page, StringComparison.Ordinal);
        Assert.Contains("<span class=\"pbd-progress__label\">Gaps</span>", page, StringComparison.Ordinal);
        Assert.Contains("`Gaps · ${missing.length}`", studio, StringComparison.Ordinal);
        Assert.DoesNotContain("items missing", studio, StringComparison.Ordinal);

        // Each field once in This slide.
        Assert.Contains("if (!missing.includes('costRd')) facts.append(", studio, StringComparison.Ordinal);

        // Present and Generate say so first (once per deck per session).
        Assert.Contains("if (await confirmGaps('Present', presentButton)) {", script, StringComparison.Ordinal);
        Assert.Contains("if (!(await confirmGaps('Generate', generateButton))) return;", script, StringComparison.Ordinal);
        Assert.Contains("cancelText: 'Review gaps',", script, StringComparison.Ordinal);
        // Generate asks only after pending saves settle, so the count matches the deck downloaded.
        var generate = script[script.IndexOf("generateForm?.addEventListener('submit'", StringComparison.Ordinal)..];
        Assert.True(generate.IndexOf("await deckMutationTail;", StringComparison.Ordinal)
            < generate.IndexOf("confirmGaps('Generate'", StringComparison.Ordinal));
    }

    [Fact]
    public void Review_PlainWordsHeadingsAndKeysForEveryone()
    {
        var page = Read("Index.cshtml");
        var library = Read("_DeckLibrary.cshtml");
        var libraryScript = Read("project-briefing-library.js");
        var decks = Read("project-briefing-decks.js");
        var extraSlides = Read("project-briefing-additional-slides.js");
        var profile = Read("_InstitutionalProfileEditor.cshtml");
        var charter = Read("_RoleCharterEditor.cshtml");
        var ffc = Read("_FfcGlobalFootprintEditor.cshtml");

        // The deck name is the page's main heading.
        Assert.Contains("<h1 id=\"pbd-deck-title\" data-pbd-deck-name>@deck.Name</h1>", page, StringComparison.Ordinal);

        // Drawers in plain words, pointing at where the slide is switched on.
        foreach (var drawer in new[] { profile, charter, ffc })
        {
            Assert.DoesNotContain("authoritative", drawer, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("72:28", drawer, StringComparison.Ordinal);
            Assert.DoesNotContain("Additional Slides workspace", drawer, StringComparison.Ordinal);
        }
        Assert.Contains("in the Deck tab under Extra slides", profile, StringComparison.Ordinal);

        // Escape closes each drawer even when focus is outside it.
        Assert.Contains("if (!profileDrawer?.classList.contains('is-open') || profileDrawer.contains(event.target)) return;", decks, StringComparison.Ordinal);
        Assert.Contains("if (!drawer?.classList.contains('is-open') || drawer.contains(event.target)) return;", extraSlides, StringComparison.Ordinal);
        Assert.Contains("if (!ffcDrawer?.classList.contains('is-open') || ffcDrawer.contains(event.target)) return;", extraSlides, StringComparison.Ordinal);

        // Library: which project matched; a clear column name. New deck: the rule up front.
        Assert.Contains("data-pbd-library-match hidden", library, StringComparison.Ordinal);
        Assert.Contains("match.textContent = projects.length", libraryScript, StringComparison.Ordinal);
        Assert.Contains("<th scope=\"col\">PowerPoint</th>", library, StringComparison.Ordinal);
        Assert.Contains("required, at least 3 characters", page, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@UpdateSheetRowLabel(row)\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Review_PhoneHeaderAndLibraryCards()
    {
        var library = Read("_DeckLibrary.cshtml");
        var css = Read("project-briefing-decks.css").Replace("\r\n", "\n");
        var phone = css[css.IndexOf("/* Phone (review PR D).", StringComparison.Ordinal)..];

        // Studio header: Generate on its own row; the icon, "Updated" and the note give way.
        Assert.Contains(".pbd-deck-header__actions .pbd-generate-action {\n        flex: 1 1 100%;", phone, StringComparison.Ordinal);
        Assert.Contains(".pbd-deck-header__meta,", phone, StringComparison.Ordinal);

        // Library: cards with labelled values.
        Assert.Contains(".pbd-library__table td[data-label]::before {", phone, StringComparison.Ordinal);
        // Cards never wider than the phone (Codex, #2946): the column and name cell shrink, descriptions wrap.
        Assert.Contains("grid-template-columns: minmax(0, 1fr);", phone, StringComparison.Ordinal);
        Assert.Contains(".pbd-library__table .pbd-library__description {\n        max-width: none;\n        white-space: normal;", phone, StringComparison.Ordinal);
        foreach (var label in new[] { "Template", "Projects", "Edited", "PowerPoint" })
        {
            Assert.Contains($"data-label=\"{label}\"", library, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Library_ContinueIsHonestAndGenerateRunsTheStudiosChecks()
    {
        var library = Read("_DeckLibrary.cshtml");
        var script = Read("project-briefing-decks.js");

        // "Continue" only for the person's own last deck.
        Assert.Contains("@(continueDeck.LastEditedByMe ? \"Continue\" : \"Recently edited\")", library, StringComparison.Ordinal);

        // Generate from the library opens the deck and runs the Studio's Generate; no bare post.
        Assert.DoesNotContain("asp-page-handler=\"Generate\"", library, StringComparison.Ordinal);
        Assert.Contains("asp-route-generate=\"1\"", library, StringComparison.Ordinal);
        Assert.Contains("if (startUrl.searchParams.get('generate') === '1') {", script, StringComparison.Ordinal);
        Assert.Contains("form.requestSubmit();", script, StringComparison.Ordinal);
        // It waits for the Studio to set the Gaps count, however the modules load (Codex, #2950).
        Assert.Contains("document.addEventListener('pbd:studio-ready', generateFromLibrary, { once: true });", script, StringComparison.Ordinal);
        Assert.DoesNotContain("    }, 0);", script, StringComparison.Ordinal);
        Assert.Contains("document.dispatchEvent(new CustomEvent('pbd:studio-ready'));", Read("project-briefing-studio.js"), StringComparison.Ordinal);

        // A deck without projects offers Add projects instead of a greyed-out Generate.
        Assert.Contains("asp-route-start=\"projects\">Add projects</a>", library, StringComparison.Ordinal);
    }

    [Fact]
    public void Library_PolishOneMainLandmarkOwnScriptsAndAFriendlyNewDeckForm()
    {
        var page = Read("Index.cshtml");
        var decks = Read("project-briefing-decks.js");
        var css = Read("project-briefing-decks.css");

        // The layout owns the page's <main>; the workspace content is a div.
        Assert.DoesNotContain("<main", page, StringComparison.Ordinal);
        Assert.Contains("<div class=\"cw-workspace-content pbd-shell\" data-pbd-root", page, StringComparison.Ordinal);

        // The library loads only its own script; the Studio's and Sortable come with a deck.
        var scripts = page[page.IndexOf("@section Scripts", StringComparison.Ordinal)..];
        var studioAt = scripts.IndexOf("project-briefing-studio.js", StringComparison.Ordinal);
        var libraryAt = scripts.IndexOf("project-briefing-library.js", StringComparison.Ordinal);
        Assert.True(studioAt > 0 && libraryAt > studioAt);
        Assert.Contains("else", scripts[studioAt..libraryAt], StringComparison.Ordinal);
        Assert.True(scripts.IndexOf("@if (deck is not null)", StringComparison.Ordinal) < scripts.IndexOf("Sortable.min.js", StringComparison.Ordinal));

        // New deck: the name rule is said under the field, and closing starts the next one fresh.
        Assert.Contains("asp-page-handler=\"Create\" novalidate data-pbd-new-form", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-new-name-error hidden", page, StringComparison.Ordinal);
        Assert.Contains("newDeckModal?.addEventListener('hidden.bs.modal', () => {\n    newDeckForm?.reset();", decks.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("'The name needs at least 3 characters.'", decks, StringComparison.Ordinal);
        Assert.Contains(".pbd-modal .form-label small {\n    color: var(--pbd-muted);\n    font-size: .75rem;", css.Replace("\r\n", "\n"), StringComparison.Ordinal);

        // Deck links take the shared focus ring.
        Assert.Contains(".pbd-library__name:focus-visible,\n.pbd-library__moved:focus-visible,", css.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.Contains("box-shadow: var(--pm-focus-ring);", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Library_TableShowsOnePowerPointStatusAndExplainsEmptyResults()
    {
        var library = Read("_DeckLibrary.cshtml");
        var script = Read("project-briefing-library.js");

        // Five columns: Deck, Template, Projects, Edited, PowerPoint.
        foreach (var gone in new[] { ">Slides</th>", ">Owner</th>", ">Last edited</th>", ">Last generated</th>", "Changed since</th>" })
        {
            Assert.DoesNotContain(gone, library, StringComparison.Ordinal);
        }
        Assert.Contains("<th scope=\"col\">Edited</th>", library, StringComparison.Ordinal);
        Assert.Contains("@deck.DataMovement.Total @(deck.DataMovement.Total == 1 ? \"change\" : \"changes\") since</a>", library, StringComparison.Ordinal);
        Assert.Contains("<small class=\"pbd-library__current\">up to date</small>", library, StringComparison.Ordinal);

        // The heading follows Mine/All, an empty list says why and offers the way out, and the
        // view is kept for the tab.
        Assert.Contains("title.textContent = scope === 'mine' ? 'My decks' : 'All decks';", script, StringComparison.Ordinal);
        Assert.Contains("You have not created or edited a deck yet.", script, StringComparison.Ordinal);
        Assert.Contains("data-pbd-library-reset>Show all decks</button>", library, StringComparison.Ordinal);
        Assert.Contains("const viewKey = 'prism.briefingDecks.libraryView';", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Studio_GivesTheSlideTheRoomOnLaptops()
    {
        var page = Read("Index.cshtml");
        var css = Read("project-briefing-decks.css").Replace("\r\n", "\n");
        var rail = Read("command-workspace.js");

        // The Studio opens with the workspace navigation folded and remembers its own choice.
        Assert.Contains("@(deck is null ? \"is-nav-expanded\" : null)", page, StringComparison.Ordinal);
        Assert.Contains("data-workspace-nav-scope=\"@(deck is null ? null : \"studio\")\"", page, StringComparison.Ordinal);
        Assert.Contains("prism.workspace.navigationExpanded.${navigationScope}", rail, StringComparison.Ordinal);
        Assert.Contains("const expandedByDefault = !navigationScope;", rail, StringComparison.Ordinal);

        // The panes follow the Studio's own width, not the window's.
        Assert.Contains("<div class=\"pbd-studio-frame\">", page, StringComparison.Ordinal);
        Assert.Contains("container: pbd-studio / inline-size;", css, StringComparison.Ordinal);
        Assert.Contains("@container pbd-studio (max-width: 1399.98px)", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 13.5rem minmax(0, 1fr) 20rem;", css, StringComparison.Ordinal);
        Assert.DoesNotContain("@media (max-width: 1399.98px) {\n    .pbd-studio {", css, StringComparison.Ordinal);

        // A long segmented label takes the room it needs instead of an ellipsis.
        Assert.Contains("grid-auto-columns: minmax(min-content, 1fr);", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_KeepsSettingsInTheDeckTabSavedInPlace()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");
        var css = Read("project-briefing-decks.css");

        // The drawer is gone: every setting sits in the Deck tab's numbered steps and saves itself.
        Assert.DoesNotContain("data-pbd-settings-drawer", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Save settings", page, StringComparison.Ordinal);
        var deckPanel = page.IndexOf("data-pbd-inspector-panel=\"deck\"", StringComparison.Ordinal);
        var form = page.IndexOf("data-pbd-settings-form", StringComparison.Ordinal);
        var checksPanel = page.IndexOf("data-pbd-inspector-panel=\"checks\"", StringComparison.Ordinal);
        Assert.True(deckPanel >= 0 && deckPanel < form && form < checksPanel, "The settings form belongs to the Deck tab.");
        Assert.DoesNotContain("data-pbd-deck-advanced", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-steps", page, StringComparison.Ordinal);
        foreach (var step in new[] { "slides", "project", "sheet", "cost", "extra", "look", "handling" })
        {
            Assert.Contains($"data-pbd-step=\"{step}\"", page, StringComparison.Ordinal);
        }

        // Template picker (Plan: Deck settings): the presets under one compact row, "customised" after changes.
        Assert.Contains("data-pbd-template-toggle", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-template-customised", page, StringComparison.Ordinal);
        Assert.Contains("Started from this template, then customised.", script, StringComparison.Ordinal);
        Assert.DoesNotContain(">Purpose</h4>", page, StringComparison.Ordinal);
        Assert.Contains("serializeSettings", script, StringComparison.Ordinal);
        Assert.Contains("beforeunload", script, StringComparison.Ordinal);
        Assert.Contains("saveSettingsNow", script, StringComparison.Ordinal);
        Assert.Contains("restoreSettingsSectionState", script, StringComparison.Ordinal);
        Assert.Contains("contain: inline-size", css, StringComparison.Ordinal);
        Assert.Contains("overflow-x: clip", css, StringComparison.Ordinal);
        Assert.Contains("max-width: 1499px", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_UsesCompactTemplateAwareSettingsLayout()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");
        var css = Read("project-briefing-decks.css");

        Assert.Contains("data-pbd-settings-appearance-title", page, StringComparison.Ordinal);
        Assert.Contains("Classification marking <small>optional</small>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Handling / classification marking", page, StringComparison.Ordinal);
        Assert.Contains("Presentation theme", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-theme-preview", page, StringComparison.Ordinal);
        Assert.Contains("preview.classList.toggle('is-update-sheet'", script, StringComparison.Ordinal);
        Assert.Contains(".pbd-theme-preview--light.is-update-sheet", css, StringComparison.Ordinal);
        Assert.Contains(".pbd-theme-preview--light .pbd-theme-preview__header { background: #7a263a; }", css, StringComparison.Ordinal);
        Assert.Contains(".pbd-theme-preview--dark .pbd-theme-preview__header { background: #8a3042; }", css, StringComparison.Ordinal);
        Assert.Contains("background: #242a34;", css, StringComparison.Ordinal);
        Assert.Contains("border: 1px solid #4a5260;", css, StringComparison.Ordinal);
        Assert.DoesNotContain("appearanceTitle.textContent", script, StringComparison.Ordinal);
        // In the narrow Deck tab every choice group stacks in one column.
        Assert.Contains(".pbd-deck-settings .pbd-choice-cards,\n.pbd-deck-settings .pbd-theme-cards,\n.pbd-deck-settings .pbd-form-grid {\n    grid-template-columns: minmax(0, 1fr);", css.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }

    [Fact]
    public void Client_UsesApplicationAntiforgeryHeaderAndDownloadsPptx()
    {
        var script = Read("project-briefing-decks.js");

        Assert.Contains("X-CSRF-TOKEN", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'RequestVerificationToken'", script, StringComparison.Ordinal);
        Assert.Contains("application/vnd.openxmlformats-officedocument.presentationml.presentation", script, StringComparison.Ordinal);
        Assert.Contains("X-Project-Briefing-Slides", script, StringComparison.Ordinal);
    }

    [Fact]
    public void CostResolver_UsesTheSharedProjectCostRuleAndKeepsProliferationSeparate()
    {
        // Cost (R&D) is the project's cost under the one shared rule; the order itself is
        // pinned by ProjectCostResolverTests.
        var source = Read("ProjectBriefingCostResolver.cs");

        Assert.Contains("new ProjectCostResolver(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectCommercialFacts", source, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingCostBasis.PNC", source, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingCostBasis.Recorded", source, StringComparison.Ordinal);
        Assert.Contains("ResolveCostsAsync", source, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingResolvedCosts", source, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingCostBasis.IPA", source, StringComparison.Ordinal);
        Assert.Contains("ProjectProductionCostFacts", source, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingCostBasis.Proliferation", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalStatusResolver_UsesExternalGeneralRemarksOnly()
    {
        // The authoritative query now lives in the shared formal-output reader.
        // The briefing service is intentionally only an adapter over that reader.
        var source = Read("ProjectLatestExternalRemarkService.cs");
        var adapter = Read("ProjectBriefingExternalStatusService.cs");

        Assert.Contains("remark.Type == RemarkType.External", source, StringComparison.Ordinal);
        Assert.Contains("remark.Scope == RemarkScope.General", source, StringComparison.Ordinal);
        Assert.Contains("!remark.IsDeleted", source, StringComparison.Ordinal);
        Assert.Contains("row.LastEditedAtUtc ?? row.CreatedAtUtc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RemarkType.Internal", source, StringComparison.Ordinal);
        Assert.Contains("IProjectLatestExternalRemarkService", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectUpdateSheet_UsesApprovedTemplateAndAuthoritativeFieldSources()
    {
        var page = Read("Index.cshtml");
        var resolver = Read("ProjectBriefingUpdateSheetFactsResolver.cs");
        var composer = Read("ProjectBriefingSlideComposer.UpdateSheet.cs");
        var planner = Read("ProjectBriefingUpdateSheetPlanner.cs");

        Assert.Contains("<span>Update sheets</span>", page, StringComparison.Ordinal);
        Assert.Contains("value=\"ProjectUpdateSheet\"", page, StringComparison.Ordinal);
        Assert.Contains("<strong>Rows on each sheet</strong>", page, StringComparison.Ordinal);
        Assert.Contains("The project name stays the title", page, StringComparison.Ordinal);
        Assert.Contains("Hide rows with nothing recorded", page, StringComparison.Ordinal);
        Assert.Contains("name=\"IncludeCoverSlide\"", page, StringComparison.Ordinal);
        Assert.Contains("name=\"IncludePortfolioSummarySlide\"", page, StringComparison.Ordinal);

        var formalFacts = Read("ProjectFormalUpdateFactsResolver.cs");

        Assert.Contains("IProjectFormalUpdateFactsResolver", resolver, StringComparison.Ordinal);
        Assert.Contains("StageCodes.AON", formalFacts, StringComparison.Ordinal);
        Assert.Contains("StageCodes.DEVP", formalFacts, StringComparison.Ordinal);
        Assert.Contains("project.LeadPoUser.Rank", resolver, StringComparison.Ordinal);
        Assert.Contains("project.LeadPoUser.FullName", resolver, StringComparison.Ordinal);
        Assert.Contains("project.SponsoringLineDirectorate.Name", resolver, StringComparison.Ordinal);
        Assert.Contains("link.IndustryPartner.Name", resolver, StringComparison.Ordinal);
        Assert.Contains("ProjectSupplyOrderFacts", formalFacts, StringComparison.Ordinal);
        Assert.Contains("ArppPublishedEntries", resolver, StringComparison.Ordinal);

        Assert.Contains("RenderProjectUpdateSheet", composer, StringComparison.Ordinal);
        Assert.Contains("source.CostRd.DisplayValue", planner, StringComparison.Ordinal);
        Assert.Contains("NormalizeStatus(source.ExternalStatus)", planner, StringComparison.Ordinal);
        Assert.Contains("BRIEF OF THE PROJECT", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("PROJECT UPDATE SHEET", composer, StringComparison.Ordinal);
        Assert.Contains("AddProjectSlideHeader", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectSlideHeaderVariant.ProjectUpdateSheet", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateSheetAccent", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectUpdateLabelFill", composer, StringComparison.Ordinal);
        Assert.Contains("SO Date:", planner, StringComparison.Ordinal);
        Assert.Contains("Firm:", planner, StringComparison.Ordinal);
        Assert.Contains("string.Join(\"\\n\"", planner, StringComparison.Ordinal);
        Assert.DoesNotContain("B5122B", composer, StringComparison.OrdinalIgnoreCase);
    }



    [Fact]
    public void ProjectUpdateSheet_PortfolioSummaryShowsSeparateRdAndAuthoritativeIpaTotals()
    {
        var dataSource = Read("ProjectBriefingDataService.cs");
        var composer = Read("ProjectBriefingSlideComposer.ExecutiveSummary.cs");

        Assert.Contains("TotalIpaCostInRupees", dataSource, StringComparison.Ordinal);
        Assert.Contains("IpaCostRecordedCount", dataSource, StringComparison.Ordinal);
        Assert.Contains("RECORDED R&D COST", composer, StringComparison.Ordinal);
        Assert.Contains("RECORDED IPA COST", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingLayout.ProjectUpdateSheet", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectUpdateSheet_SupportsThemeAndAdaptiveSelectableRowsWithoutMigration()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");
        var css = Read("project-briefing-decks.css");
        var dataSource = Read("ProjectBriefingDataService.cs");
        var service = Read("ProjectBriefingDeckService.cs");
        var composer = Read("ProjectBriefingSlideComposer.UpdateSheet.cs");
        var planner = Read("ProjectBriefingUpdateSheetPlanner.cs");

        Assert.Contains("name=\"PresentationTheme\"", page, StringComparison.Ordinal);
        Assert.Contains("name=\"UpdateSheetRows\"", page, StringComparison.Ordinal);
        Assert.Contains("UpdateSheetRowOrder", page, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingUpdateSheetOptions.AllRows", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-update-row-list", page, StringComparison.Ordinal);
        Assert.Contains("selectedUpdateSheetRowKeys", script, StringComparison.Ordinal);
        Assert.Contains("restoreUpdateRowOrder", script, StringComparison.Ordinal);
        Assert.Contains("recommendedKeys.has", script, StringComparison.Ordinal);
        Assert.Contains("validateUpdateSheetRows", script, StringComparison.Ordinal);
        Assert.Contains(".pbd-update-row-list", css, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingDeckConfigurationCodec.WithPresentationOptions", service, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingDeckConfigurationCodec.WithSelectionRules", service, StringComparison.Ordinal);
        Assert.Contains("SelectionRulesJson", dataSource, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingUpdateSheetPlanner.ResolveRows", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingUpdateSheetLayoutVariant.Compact", planner, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingUpdateSheetLayoutVariant.Standard", planner, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingUpdateSheetLayoutVariant.Detailed", planner, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp", planner, StringComparison.Ordinal);
        Assert.Contains("Completion Status", planner, StringComparison.Ordinal);
        Assert.Contains("Project completed", planner, StringComparison.Ordinal);
        Assert.Contains("StageCodes.DEVP", planner, StringComparison.Ordinal);
        Assert.Contains("BriefTypographyProfiles", planner, StringComparison.Ordinal);
        Assert.DoesNotContain("Name of Project", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void PresentationVisualSystem_UsesSharedSemanticHeaderAndNarrativeRoles()
    {
        var theme = Read("ProjectBriefingThemeDefinition.cs");
        var composer = Read("ProjectBriefingSlideComposer.cs");
        var updateComposer = Read("ProjectBriefingSlideComposer.UpdateSheet.cs");
        var typography = Read("ProjectBriefingNarrativeTypography.cs");

        Assert.Contains("HeaderAccent", theme, StringComparison.Ordinal);
        Assert.Contains("ProjectUpdateAccent", theme, StringComparison.Ordinal);
        Assert.Contains("ProjectUpdateLabelFill", theme, StringComparison.Ordinal);
        Assert.Contains("OperationalAccent", theme, StringComparison.Ordinal);
        Assert.Contains("NarrativeAccent", theme, StringComparison.Ordinal);
        Assert.Contains("AddProjectSlideHeader", composer, StringComparison.Ordinal);
        Assert.Contains("Project sheet title", composer, StringComparison.Ordinal);
        Assert.Contains("canvas.Theme.TextPrimary", composer, StringComparison.Ordinal);
        Assert.Contains("canvas.Theme.ProjectUpdateAccent", composer, StringComparison.Ordinal);
        Assert.Contains("CalculateDetailedLayout", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingNarrativeDensity.Sparse", typography, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingNarrativeDensity.Dense", typography, StringComparison.Ordinal);
        Assert.Contains("ResolveProjectBrief", typography, StringComparison.Ordinal);
        Assert.Contains("ResolveUpdateSheetBrief", typography, StringComparison.Ordinal);
        Assert.DoesNotContain("private const string UpdateSheetAccent", updateComposer, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedDecks_AreCommandWorkspaceWideAndTrackLastModifier()
    {
        var source = Read("ProjectBriefingDeckService.cs");

        Assert.DoesNotContain("deck.OwnerUserId ==", source, StringComparison.Ordinal);
        Assert.Contains("LastModifiedByUserId", source, StringComparison.Ordinal);
        Assert.Contains("A shared command deck with this name already exists", source, StringComparison.Ordinal);
        Assert.Contains("OrderByDescending(deck => deck.UpdatedAtUtc)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StageSummary_UsesCanonicalMaturityOrderAcrossBuilderAndPresentation()
    {
        var dataSource = Read("ProjectBriefingDataService.cs");
        var composer = Read("ProjectBriefingSlideComposer.cs");
        var stageComposer = Read("ProjectBriefingSlideComposer.StageSummary.cs");

        Assert.Contains("ProjectBriefingStageOrder.BuildSummary", dataSource, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingProjectOrdering.OrderProjects", dataSource, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingStageOrder.Resolve", dataSource, StringComparison.Ordinal);
        Assert.Contains("OrderProjects(data.Projects)", composer, StringComparison.Ordinal);
        Assert.Contains("AddStageSummarySlides", composer, StringComparison.Ordinal);
        Assert.Contains("RenderStageWiseExecutiveSummary", composer, StringComparison.Ordinal);
        Assert.Contains("RenderStageWiseExecutiveSummary", stageComposer, StringComparison.Ordinal);
        Assert.Contains("OngoingStageSummary", dataSource, StringComparison.Ordinal);
        Assert.Contains("ProjectLifecycleStatus.Active", dataSource, StringComparison.Ordinal);
        Assert.Contains("CancelledCount", dataSource, StringComparison.Ordinal);
        Assert.Contains("ONGOING PROJECTS BY PRESENT STAGE", stageComposer, StringComparison.Ordinal);
        Assert.Contains("KEY TAKEAWAY", stageComposer, StringComparison.Ordinal);
        Assert.Contains("AddEllipse", composer, StringComparison.Ordinal);
        Assert.Contains("RenderStageSummaryTable", composer, StringComparison.Ordinal);
        Assert.Contains("StandardSlideOptions.IncludeStageDistributionTable", composer, StringComparison.Ordinal);
        Assert.Contains("IncludeStageDistributionTable", dataSource, StringComparison.Ordinal);
        Assert.Contains("Stage-wise project distribution", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("data.Summary.StageSummary.Chunk", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("reverse workflow order", composer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bars are native editable PowerPoint shapes", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("STATUS: LATEST EXTERNAL REMARK ONLY", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingTablePagination.Paginate", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingTablePagination.Paginate", dataSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DetailedSlide_PrioritisesCapabilityOverviewAndCombinesStageWithStatus()
    {
        var composer = Read("ProjectBriefingSlideComposer.cs");

        Assert.Contains("PRESENT STATUS", composer, StringComparison.Ordinal);
        var detailStart = composer.IndexOf("private static void RenderProjectDetail(", StringComparison.Ordinal);
        var detailEnd = composer.IndexOf("private static void RenderCapabilityContinuation(", detailStart, StringComparison.Ordinal);
        Assert.True(detailStart >= 0 && detailEnd > detailStart);
        Assert.DoesNotContain("PROJECT POSITION", composer[detailStart..detailEnd], StringComparison.Ordinal);
        Assert.Contains("CAPABILITY OVERVIEW", composer, StringComparison.Ordinal);
        Assert.Contains("const double rightWidth = 7.48", composer, StringComparison.Ordinal);
        Assert.Contains("CalculateDetailedLayout", composer, StringComparison.Ordinal);
        Assert.Contains("vertOverflow=\\\"clip\\\"", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingCapabilityPaginator.Paginate", composer, StringComparison.Ordinal);
        Assert.Contains("RenderCapabilityContinuation", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("FitOverview", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_OffersIndependentCapabilityAndProjectBriefNarratives()
    {
        var page = Read("Index.cshtml");
        var dataSource = Read("ProjectBriefingDataService.cs");
        var composer = Read("ProjectBriefingSlideComposer.cs");

        Assert.Contains("<legend>Each project gets</legend>", page, StringComparison.Ordinal);
        Assert.Contains("value=\"CapabilityOverview\"", page, StringComparison.Ordinal);
        Assert.Contains("value=\"ProjectBrief\"", page, StringComparison.Ordinal);
        Assert.Contains("value=\"Both\"", page, StringComparison.Ordinal);
        Assert.Contains("ProjectCapabilityStatements", dataSource, StringComparison.Ordinal);
        Assert.Contains("item.Project.ProjectBrief", dataSource, StringComparison.Ordinal);
        Assert.Contains("RenderProjectBrief", composer, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingNarrativeMode.Both", composer, StringComparison.Ordinal);
    }


    [Fact]
    public void StandardBriefing_OffersTwoProjectBriefDesignsAndIndependentContextControls()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");
        var composer = Read("ProjectBriefingSlideComposer.cs");

        Assert.Contains("Project Brief layout", page, StringComparison.Ordinal);
        Assert.Contains("name=\"ProjectBriefLayout\" value=\"Automatic\"", page, StringComparison.Ordinal);
        Assert.Contains("name=\"ProjectBriefLayout\" value=\"Standard\"", page, StringComparison.Ordinal);
        Assert.Contains("name=\"ProjectBriefLayout\" value=\"PhotoEmphasis\"", page, StringComparison.Ordinal);
        Assert.Contains("name=\"ShowPresentStage\"", page, StringComparison.Ordinal);
        Assert.Contains("name=\"ShowPresentStatus\"", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-step=\"cost\"", page, StringComparison.Ordinal);
        // Compact controls (Plan: Deck settings, PR 3): segmented choices explain only the selected option.
        foreach (var name in new[] { "Layout", "PresentationMode", "NarrativeMode", "ProjectBriefLayout", "CostMode", "BrandingScope", "ClosingSlideType" })
        {
            Assert.Contains($"data-pbd-seg-help=\"{name}\"", page, StringComparison.Ordinal);
        }
        Assert.Contains("class=\"pbd-chip\"", page, StringComparison.Ordinal);
        Assert.Contains("role=\"switch\" name=\"ShowKeyPoints\"", page, StringComparison.Ordinal);
        Assert.Contains("value=\"CostRdOnly\"", page, StringComparison.Ordinal);
        Assert.Contains("value=\"ProliferationOnly\"", page, StringComparison.Ordinal);
        Assert.Contains("value=\"Both\"", page, StringComparison.Ordinal);
        Assert.Contains("value=\"None\"", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-project-brief-layout-settings", page, StringComparison.Ordinal);
        Assert.Contains("ResolveProjectBriefLayout", composer, StringComparison.Ordinal);
        Assert.Contains("RenderStandardProjectBrief", composer, StringComparison.Ordinal);
        Assert.Contains("RenderPhotoEmphasisProjectBrief", composer, StringComparison.Ordinal);
        Assert.Contains("CostCards(canvas, data.CostMode, project)", composer, StringComparison.Ordinal);
        Assert.Contains("AddProjectBriefInformationStrip", composer, StringComparison.Ordinal);
        Assert.Contains("ResolvePresentStatusValue", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("normalized = \"No external status recorded\"", composer, StringComparison.Ordinal);
    }


    [Fact]
    public void Builder_AppendsConfigurableProfessionalClosingSlideToEveryDeck()
    {
        var page = Read("Index.cshtml");
        var composer = Read("ProjectBriefingSlideComposer.cs");
        var codec = Read("ProjectBriefingDeckConfigurationCodec.cs");
        var dataSource = Read("ProjectBriefingDataService.cs");

        Assert.Contains("name=\"ClosingSlideType\" value=\"JaiHind\"", page, StringComparison.Ordinal);
        Assert.Contains("name=\"ClosingSlideType\" value=\"ThankYou\"", page, StringComparison.Ordinal);
        Assert.Contains("<legend>Closing slide</legend>", page, StringComparison.Ordinal);
        Assert.Contains("SlidePlanKind.Closing", composer, StringComparison.Ordinal);
        Assert.Contains("RenderClosingSlide", composer, StringComparison.Ordinal);
        Assert.Contains("HeaderVariant.Closing", composer, StringComparison.Ordinal);
        Assert.Contains("AddSubtleRoundedRect", composer, StringComparison.Ordinal);
        Assert.Contains("Closing ceremonial panel", composer, StringComparison.Ordinal);
        Assert.Contains("Closing organisation", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("Closing deck descriptor", composer, StringComparison.Ordinal);
        Assert.Contains("Closing saffron accent", composer, StringComparison.Ordinal);
        Assert.Contains("Closing white accent", composer, StringComparison.Ordinal);
        Assert.Contains("Closing green accent", composer, StringComparison.Ordinal);
        Assert.Contains("kind is SlidePlanKind.Cover or SlidePlanKind.Closing", composer, StringComparison.Ordinal);
        Assert.Contains("[\"closingSlide\"]", codec, StringComparison.Ordinal);
        Assert.Contains("ClosingSlides = 1", dataSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AudienceDeck_RemovesBuilderReadinessAndUsesFullCapabilityPagination()
    {
        var composer = Read("ProjectBriefingSlideComposer.cs");
        var executive = Read("ProjectBriefingSlideComposer.ExecutiveSummary.cs");
        var dataSource = Read("ProjectBriefingDataService.cs");

        Assert.DoesNotContain("STATUS MISSING", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("DATA READINESS", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("PowerPoint-ready photo", composer, StringComparison.Ordinal);
        Assert.Contains("DATA COVERAGE", executive, StringComparison.Ordinal);
        Assert.Contains("metric.Recorded.ToString(CultureInfo.InvariantCulture)", executive, StringComparison.Ordinal);
        Assert.Contains("totalProjects.ToString(CultureInfo.InvariantCulture)", executive, StringComparison.Ordinal);
        Assert.Contains("CapabilityContinuationSlides", dataSource, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingCapabilityPaginator", dataSource, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingTextNormalizer.NormalizeFull", dataSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectBriefingTextNormalizer.Normalize(", dataSource, StringComparison.Ordinal);
        Assert.Contains("ExecutiveStatus", composer, StringComparison.Ordinal);
        Assert.Contains("includeBasis: false", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_ManagesMembershipWithoutFullPageReload()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");
        var handler = Read("Index.cshtml.cs");
        var service = Read("ProjectBriefingDeckService.cs");

        Assert.Contains("data-membership-url", page, StringComparison.Ordinal);
        Assert.Contains("In this deck", page, StringComparison.Ordinal);
        Assert.Contains("Not in this deck", page, StringComparison.Ordinal);
        Assert.Contains("Apply changes", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-add-selection-form", page, StringComparison.Ordinal);
        Assert.Contains("updateMembership", script, StringComparison.Ordinal);
        Assert.Contains("sessionStorage", script, StringComparison.Ordinal);
        // An in-page Add projects request is answered with the refreshed deck, not a redirect.
        Assert.Contains("return new JsonResult(new { saved = true, message, deck });", handler, StringComparison.Ordinal);
        Assert.Contains("UpdateMembershipAsync", service, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefing.MembershipUpdated", service, StringComparison.Ordinal);
    }


    [Fact]
    public void SavedDeckItems_RemainVisibleAndRemovableAfterProjectLifecycleChanges()
    {
        var dataSource = Read("ProjectBriefingDataService.cs");

        Assert.Contains(".Where(item => item.DeckId == deckId)", dataSource, StringComparison.Ordinal);
        Assert.DoesNotContain("&& !item.Project.IsDeleted", dataSource, StringComparison.Ordinal);
        Assert.DoesNotContain("&& !item.Project.IsArchived", dataSource, StringComparison.Ordinal);
        Assert.Contains("Deleted record", dataSource, StringComparison.Ordinal);
        Assert.Contains("Archived", dataSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_PersistsProfessionalThemesBrandingAndRevealsFilteredMatches()
    {
        var page = Read("Index.cshtml");
        var script = Read("project-briefing-decks.js");
        var composer = Read("ProjectBriefingSlideComposer.cs");

        Assert.Contains("Editorial Light", page, StringComparison.Ordinal);
        Assert.Contains("Graphite Dark", page, StringComparison.Ordinal);
        Assert.Contains("<legend>Insignia</legend>", page, StringComparison.Ordinal);
        Assert.Contains("ProjectBriefingThemeCatalog.Resolve", composer, StringComparison.Ordinal);
        Assert.Contains("AddBrandingImages", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("SIMULATOR DEVELOPMENT DIVISION · PRISM ERP", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_KeepsCanonicalStageGroupsAndReordersOnlyWithinAStage()
    {
        var studio = Read("project-briefing-studio.js");
        var script = Read("project-briefing-decks.js");
        var composer = Read("ProjectBriefingSlideComposer.cs");

        // The outline groups projects by present stage and only reorders inside a group.
        Assert.Contains("Projects are grouped by present stage, in the deck's (maturity) order.", studio, StringComparison.Ordinal);
        Assert.Contains("requestReorder(stageIdsFromList(list))", studio, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('pbd:reorder-projects'", script, StringComparison.Ordinal);
        Assert.Contains("double LeftMargin = .05", composer, StringComparison.Ordinal);
        Assert.Contains("double TopMargin = .025", composer, StringComparison.Ordinal);
        Assert.Contains("Emu(cell.LeftMargin)", composer, StringComparison.Ordinal);
        Assert.Contains("Emu(cell.TopMargin)", composer, StringComparison.Ordinal);
    }

    [Fact]
    public void PhotoLoader_ValidatesActualFilesAndProducesPowerPointReadyJpeg()
    {
        var source = Read("ProjectBriefingPhotoLoader.cs");

        Assert.Contains("Image.Identify", source, StringComparison.Ordinal);
        Assert.Contains("ResizeMode.Crop", source, StringComparison.Ordinal);
        Assert.Contains("new JpegEncoder", source, StringComparison.Ordinal);
        Assert.Contains("master/", source, StringComparison.Ordinal);
        Assert.Contains("No PowerPoint-ready photograph was found", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InstitutionalProfileSlide_IsOptionalModularAndUsesAuthoritativePrismBreakdowns()
    {
        var page = Read("Index.cshtml");
        var profileEditor = Read("_InstitutionalProfileEditor.cshtml");
        var additionalSlideCatalog = Read("ProjectBriefingAdditionalSlideCatalog.cs");
        var pageModel = Read("Index.cshtml.cs");
        var deckService = Read("ProjectBriefingDeckService.cs");
        var script = Read("project-briefing-decks.js");
        var service = Read("ProjectBriefingInstitutionalProfileService.cs");
        var composer = Read("ProjectBriefingSlideComposer.InstitutionalProfile.cs");
        var mainComposer = Read("ProjectBriefingSlideComposer.cs");
        var dataSource = Read("ProjectBriefingDataService.cs");

        Assert.Contains("<h5 id=\"pbd-additional-slides-title\">Institution slides</h5>", page, StringComparison.Ordinal);
        Assert.Contains("SDD Institutional Profile", additionalSlideCatalog, StringComparison.Ordinal);
        Assert.Contains("@AdditionalSlideDisplayName(slideType)", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-profile-drawer", profileEditor, StringComparison.Ordinal);
        Assert.Contains("InstitutionalModules", profileEditor, StringComparison.Ordinal);
        Assert.Contains("data-pbd-institutional-history-editor", profileEditor, StringComparison.Ordinal);
        Assert.Contains("data-pbd-institutional-partnership-editor", profileEditor, StringComparison.Ordinal);
        Assert.Contains("InstitutionalProjectScope", profileEditor, StringComparison.Ordinal);
        Assert.Contains("Original completed projects — recommended", profileEditor, StringComparison.Ordinal);
        Assert.Contains("come from PRISM and cannot be edited here", profileEditor, StringComparison.Ordinal);
        Assert.Contains("Profile footer strip", profileEditor, StringComparison.Ordinal);
        Assert.Contains("InstitutionalFooterStripText", profileEditor, StringComparison.Ordinal);
        Assert.Contains("Shown exactly as entered", profileEditor, StringComparison.Ordinal);
        Assert.Contains("validateInstitutionalProfile", script, StringComparison.Ordinal);
        Assert.Contains("syncInstitutionalModuleOrder", script, StringComparison.Ordinal);
        Assert.Contains("syncInstitutionalHistoryEditor", script, StringComparison.Ordinal);
        Assert.Contains("syncInstitutionalPartnershipEditor", script, StringComparison.Ordinal);
        Assert.Contains("syncInstitutionalLayoutSummary", script, StringComparison.Ordinal);
        Assert.Contains("data-pbd-additional-slides", page, StringComparison.Ordinal);
        // One place for additional slides: the settings no longer carry a "Manage slides" summary.
        Assert.DoesNotContain("data-pbd-manage-additional-slides", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-profile-open", page, StringComparison.Ordinal);
        Assert.Contains("SaveInstitutionalProfile", profileEditor, StringComparison.Ordinal);
        Assert.Contains("openProfileDrawer", script, StringComparison.Ordinal);
        Assert.Contains("profileInitialState", script, StringComparison.Ordinal);
        Assert.Contains("OnPostSaveInstitutionalProfileAsync", pageModel, StringComparison.Ordinal);
        Assert.Contains("OnPostToggleInstitutionalProfileAsync", pageModel, StringComparison.Ordinal);
        Assert.Contains("InstitutionalProfileOptions = additionalSlides.InstitutionalProfileOptions", pageModel, StringComparison.Ordinal);
        Assert.Contains("AdditionalSlideOrder = additionalSlides.AdditionalSlideOrder", pageModel, StringComparison.Ordinal);
        Assert.Contains("UpdateInstitutionalProfileAsync", deckService, StringComparison.Ordinal);
        var profileUpdateStart = deckService.IndexOf("public async Task UpdateInstitutionalProfileAsync(", StringComparison.Ordinal);
        var profileUpdateEnd = deckService.IndexOf("public async Task UpdateRoleCharterAsync(", profileUpdateStart, StringComparison.Ordinal);
        Assert.True(profileUpdateStart >= 0 && profileUpdateEnd > profileUpdateStart);
        var profileUpdate = deckService[profileUpdateStart..profileUpdateEnd];
        Assert.Contains("ProjectBriefingDeckConfigurationCodec.WithAdditionalSlides", profileUpdate, StringComparison.Ordinal);
        Assert.Contains("normalized,", profileUpdate, StringComparison.Ordinal);
        Assert.Contains("configuration.RoleCharterOptions", profileUpdate, StringComparison.Ordinal);
        Assert.Contains("configuration.FfcGlobalFootprintOptions", profileUpdate, StringComparison.Ordinal);
        Assert.True(
            page.IndexOf("Content used by this presentation", StringComparison.Ordinal)
            < page.IndexOf("data-pbd-additional-slides", StringComparison.Ordinal)
            && page.IndexOf("data-pbd-additional-slides", StringComparison.Ordinal)
            < page.IndexOf("Projects in this deck", StringComparison.Ordinal),
            "Additional slides must remain visible between preflight and project management.");

        Assert.Contains("ProjectLifecycleStatus.Completed", service, StringComparison.Ordinal);
        Assert.Contains("!project.IsBuild", service, StringComparison.Ordinal);
        Assert.Contains("GetApprovedAggregatesAsync", service, StringComparison.Ordinal);
        Assert.Contains("TechnicalCategoryName", service, StringComparison.Ordinal);
        Assert.Contains("GetKpisAsync", service, StringComparison.Ordinal);
        Assert.Contains("FY {year.TrainingYearLabel}", service, StringComparison.Ordinal);
        Assert.Contains("Units /", service, StringComparison.Ordinal);
        Assert.Contains("IprRecords", service, StringComparison.Ordinal);
        Assert.DoesNotContain("515 ABW", service, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("RenderInstitutionalProfile", composer, StringComparison.Ordinal);
        Assert.Contains("RenderInstitutionalHistory", composer, StringComparison.Ordinal);
        Assert.Contains("RenderInstitutionalModules", composer, StringComparison.Ordinal);
        Assert.Contains("AddGroup", composer, StringComparison.Ordinal);
        Assert.Contains("SDD institutional history timeline", composer, StringComparison.Ordinal);
        Assert.Contains("institutional module", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("AddNativeTable", composer, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeTableBorders", composer, StringComparison.Ordinal);
        Assert.Contains("InstitutionalModuleDisplayTitle", composer, StringComparison.Ordinal);
        Assert.Contains("module labels", composer, StringComparison.Ordinal);
        Assert.Contains("module values", composer, StringComparison.Ordinal);
        Assert.Contains("RenderInstitutionalFooterStrip", composer, StringComparison.Ordinal);
        Assert.Contains("Data as on", mainComposer, StringComparison.Ordinal);
        Assert.Contains("Source: PRISM ERP", mainComposer, StringComparison.Ordinal);
        Assert.Contains("InstitutionalProfileSlides", dataSource, StringComparison.Ordinal);
    }


    [Fact]
    public void FfcGlobalFootprintSlide_IsRegisteredErpBackedAndFixedBeforeClosing()
    {
        var page = Read("Index.cshtml");
        var editor = Read("_FfcGlobalFootprintEditor.cshtml");
        var pageModel = Read("Index.cshtml.cs");
        var catalog = Read("ProjectBriefingAdditionalSlideCatalog.cs");
        var dataService = Read("ProjectBriefingDataService.cs");
        var composer = Read("ProjectBriefingSlideComposer.FfcGlobalFootprint.cs");
        var mainComposer = Read("ProjectBriefingSlideComposer.cs");
        var script = Read("project-briefing-additional-slides.js");

        Assert.Contains("FfcGlobalFootprint", catalog, StringComparison.Ordinal);
        Assert.Contains("BeforeClosing", catalog, StringComparison.Ordinal);
        Assert.Contains("CanReorder: false", catalog, StringComparison.Ordinal);
        Assert.Contains("Opening slides", page, StringComparison.Ordinal);
        Assert.Contains("Before closing", page, StringComparison.Ordinal);
        // A fixed slide shows a pin, not a drag handle.
        Assert.Contains("title=\"Fixed immediately before the closing slide\"", page, StringComparison.Ordinal);
        Assert.Contains("Each approved slide type can be added once", page, StringComparison.Ordinal);
        Assert.Contains("data-pbd-ffc-footprint-open", page, StringComparison.Ordinal);
        Assert.Contains("SaveFfcGlobalFootprint", editor, StringComparison.Ordinal);
        Assert.Contains("Live, read-only PRISM data", editor, StringComparison.Ordinal);
        Assert.Contains("FfcFootprintPreviewSummary", editor, StringComparison.Ordinal);
        Assert.Contains("FfcFootprintPreviewSummary", pageModel, StringComparison.Ordinal);
        Assert.Contains("OnPostSaveFfcGlobalFootprintAsync", pageModel, StringComparison.Ordinal);
        Assert.Contains("IFfcFootprintService", dataService, StringComparison.Ordinal);
        Assert.Contains("IFfcPresentationMapRenderer", dataService, StringComparison.Ordinal);
        Assert.Contains("RenderFocused", dataService, StringComparison.Ordinal);
        Assert.Contains("RenderFfcGlobalFootprint", composer, StringComparison.Ordinal);
        Assert.Contains("RenderFfcMapDominant", composer, StringComparison.Ordinal);
        Assert.Contains("RenderFfcCountryWiseBreakdownSlide", composer, StringComparison.Ordinal);
        Assert.Contains("IncludeCountryWiseBreakdown", editor, StringComparison.Ordinal);
        Assert.Contains("FfcGlobalFootprintLayout", editor, StringComparison.Ordinal);
        Assert.Contains("CountriesPerBreakdownSlide", mainComposer, StringComparison.Ordinal);
        Assert.Contains("Delivered, awaiting installation", composer, StringComparison.Ordinal);
        Assert.Contains("COUNTRY-WISE BREAKDOWN", composer, StringComparison.Ordinal);
        Assert.Contains("FfcMapWidth = 8.36", composer, StringComparison.Ordinal);
        Assert.Contains("FfcBreakdownWidth = 3.30", composer, StringComparison.Ordinal);
        Assert.Contains("2100, height: 1065", dataService, StringComparison.Ordinal);
        Assert.Contains("countries — recommended", editor, StringComparison.Ordinal);
        Assert.Contains("AddConcludingPlans", mainComposer, StringComparison.Ordinal);
        Assert.Contains("SlidePlanKind.FfcGlobalFootprint", mainComposer, StringComparison.Ordinal);
        Assert.Contains("confirmFfcDiscard", script, StringComparison.Ordinal);
        Assert.DoesNotContain("window.confirm", script, StringComparison.Ordinal);
    }

    private static string Read(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "ProjectBriefing", fileName);
        Assert.True(File.Exists(path), $"Project briefing contract file was not copied to test output: {path}");
        return File.ReadAllText(path);
    }
}

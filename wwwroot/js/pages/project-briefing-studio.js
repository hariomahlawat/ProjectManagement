// Briefing Studio: the slide outline, the selected slide (or every slide as a grid) and an
// inspector for the selected slide, the deck and its content checks. The server composes the
// same PowerPoint that Generate downloads and returns each slide as encoded, positioned HTML plus
// what each slide is (its role and project); this module lays that out and rebuilds it after
// in-place edits. Saving stays with project-briefing-decks.js: Studio buttons reuse its hooks.
const root = document.querySelector('[data-pbd-studio]');

if (root) {
  const page = root.closest('[data-pbd-root]') || document;
  const outlineNav = root.querySelector('[data-pbd-outline]');
  const searchInput = root.querySelector('[data-pbd-outline-search]');
  const filterBox = root.querySelector('[data-pbd-outline-filter]');
  const filterText = root.querySelector('[data-pbd-outline-filter-text]');
  const filterClear = root.querySelector('[data-pbd-outline-filter-clear]');
  const countLabel = root.querySelector('[data-pbd-studio-count]');
  const stageMeta = root.querySelector('[data-pbd-stage-meta]');
  const stageTitle = root.querySelector('[data-pbd-stage-title]');
  const slideBox = root.querySelector('[data-pbd-stage-slide]');
  const gridBox = root.querySelector('[data-pbd-stage-grid]');
  const viewButtons = [...root.querySelectorAll('[data-pbd-stage-view]')];
  const prevButton = root.querySelector('[data-pbd-stage-prev]');
  const nextButton = root.querySelector('[data-pbd-stage-next]');
  const refreshButton = root.querySelector('[data-pbd-preview-refresh]');
  const presentButton = root.querySelector('[data-pbd-preview-open]');
  const status = root.querySelector('[data-pbd-preview-status]');
  const tabs = [...root.querySelectorAll('[data-pbd-inspector-tab]')];
  const panels = Object.fromEntries([...root.querySelectorAll('[data-pbd-inspector-panel]')]
    .map((panel) => [panel.dataset.pbdInspectorPanel, panel]));
  const checksBadge = root.querySelector('[data-pbd-checks-badge]');
  const placeholderNote = document.querySelector('[data-pbd-placeholder-note]');
  const slideTotal = document.querySelector('[data-pbd-slide-total]');
  const slideBreakdown = document.querySelector('[data-pbd-slide-breakdown]');
  const viewer = document.querySelector('[data-pbd-viewer]');
  const viewerStage = viewer?.querySelector('[data-pbd-viewer-stage]');
  const viewerStrip = viewer?.querySelector('[data-pbd-viewer-strip]');
  const viewerTitle = viewer?.querySelector('[data-pbd-viewer-title]');
  const viewerCounter = viewer?.querySelector('[data-pbd-viewer-counter]');
  const usesCapability = root.dataset.usesCapability === 'true';

  const ROLES = {
    Cover: { section: 'opening', label: 'Cover', about: 'The deck name and description, the project counts and the handling marking.' },
    InstitutionalProfile: { section: 'opening', label: 'SDD profile', editor: '[data-pbd-profile-open]', about: 'History, institutional outputs and partnerships, from PRISM and the profile settings.' },
    RoleCharter: { section: 'opening', label: 'Role & Charter', fold: true, editor: '[data-pbd-role-charter-open]', about: 'The role statements and charter items kept for this deck. A long charter continues on a second slide rather than shrinking below 10 pt.' },
    PortfolioSummary: { section: 'portfolio', label: 'Portfolio summary', about: 'Selected, completed and ongoing projects, with the recorded costs.' },
    StageSummary: { section: 'portfolio', label: 'Stage summary', about: 'Ongoing projects by present stage, with the leading stage called out.' },
    StageDistributionTable: { section: 'portfolio', label: 'Stage table', about: 'The stage-wise counts as an editable table.' },
    CategorySummary: { section: 'portfolio', label: 'Category summary', fold: true, about: 'Projects by category. Left out when the deck holds a single category.' },
    StatusTable: { section: 'portfolio', label: 'Status table', fold: true, about: 'Every project with its cost, present stage and latest status remark, in deck order.' },
    ProjectCapability: { section: 'project', label: 'Capability overview' },
    ProjectCapabilityContinuation: { section: 'project', label: 'Capability overview, continued' },
    ProjectBrief: { section: 'project', label: 'Project brief' },
    ProjectBriefContinuation: { section: 'project', label: 'Project brief, continued' },
    ProjectUpdateSheet: { section: 'project', label: 'Update sheet' },
    ProjectUpdateSheetContinuation: { section: 'project', label: 'Update sheet, continued' },
    FfcGlobalFootprint: { section: 'closing', label: 'FFC footprint', editor: '[data-pbd-ffc-footprint-open]', about: 'Countries supplied with PRISM projects, from live FFC records.' },
    FfcCountryBreakdown: { section: 'closing', label: 'FFC country breakdown', fold: true, editor: '[data-pbd-ffc-footprint-open]', about: 'Country-wise detail behind the FFC footprint.' },
    Closing: { section: 'closing', label: 'Closing', about: 'The closing slide. Choose Jai Hind or Thank You in the deck settings.' }
  };
  // After a settings change the preview shows a slide it affects: the first of these roles the
  // rebuilt deck has (on the project already in view when it has one). Settings that change
  // every slide (theme, marking) keep the slide in view.
  const projectRoles = () => (root.querySelector('input[name="KeyPointSlide"]:checked')?.value === 'CapabilitySlide'
    ? ['ProjectCapability', 'ProjectBrief', 'ProjectUpdateSheet']
    : ['ProjectBrief', 'ProjectCapability', 'ProjectUpdateSheet']);
  const SETTING_SLIDES = {
    Layout: () => ['ProjectUpdateSheet', 'ProjectCapability', 'ProjectBrief', 'StatusTable'],
    PresentationMode: () => ['ProjectCapability', 'ProjectBrief', 'StatusTable'],
    NarrativeMode: () => ['ProjectCapability', 'ProjectBrief'],
    ShowPresentStage: () => ['ProjectCapability', 'ProjectBrief'],
    ShowPresentStatus: () => ['ProjectCapability', 'ProjectBrief'],
    ShowKeyPoints: projectRoles,
    KeyPointSlide: projectRoles,
    ProjectBriefLayout: () => ['ProjectBrief'],
    CostMode: () => ['ProjectCapability', 'ProjectBrief', 'StatusTable', 'PortfolioSummary'],
    UpdateSheetRows: () => ['ProjectUpdateSheet'],
    UpdateSheetRowOrder: () => ['ProjectUpdateSheet'],
    HideEmptyUpdateSheetValues: () => ['ProjectUpdateSheet'],
    IncludeStageSummary: () => ['StageSummary'],
    IncludeStageDistributionTable: () => ['StageDistributionTable', 'StageSummary'],
    // Both category charts share a role; their titles tell them apart.
    IncludeProjectCategorySummary: () => [{ role: 'CategorySummary', title: /project-category/i }],
    IncludeTechnicalCategorySummary: () => [{ role: 'CategorySummary', title: /technical-category/i }],
    IncludeCoverSlide: () => ['Cover'],
    IncludePortfolioSummarySlide: () => ['PortfolioSummary'],
    BrandingScope: () => ['Cover'],
    ClosingSlideType: () => ['Closing']
  };
  // The roles to look for after the next rebuild, from the latest change that names any.
  let followRoles = null;
  let outlineItems = [];
  const followChange = () => {
    const roles = followRoles;
    followRoles = null;
    if (!roles?.length || !outlineItems.length) return false;
    const inView = entryForIndex(current)?.projectId;
    for (const target of roles) {
      // A target is a role, or a role with the slide title that picks one kind of it.
      const role = typeof target === 'string' ? target : target.role;
      const title = typeof target === 'string' ? null : target.title;
      const matches = outlineItems
        .map((item, index) => ({ item, index }))
        .filter(({ item, index }) => item.role === role && (!title || title.test(slides[index]?.title || '')));
      if (!matches.length) continue;
      const chosen = matches.find(({ item }) => inView && item.projectId === inView) || matches[0];
      selectIndex(chosen.index, { key: entryForIndex(chosen.index)?.key || '' });
      return true;
    }
    return false;
  };

  const SECTIONS = [
    { key: 'opening', label: 'Opening' },
    { key: 'portfolio', label: 'Portfolio' },
    { key: 'project', label: 'Projects' },
    { key: 'closing', label: 'Closing' },
    { key: 'leftout', label: 'Not in the PowerPoint' }
  ];
  // Each fix says where the change lands: "Fix in project" changes the project record (every deck
  // and page shows it); "This deck only" writes text kept with this deck.
  const GAPS = {
    status: { label: 'Status remark', missing: 'No external status remark yet', href: (p) => `${p.openUrl}#remarks` },
    costRd: { label: 'Cost (R&D)', missing: 'Not recorded', href: (p) => p.openUrl },
    proliferationCost: { label: 'Proliferation cost', missing: 'Not recorded', href: (p) => p.openUrl },
    photo: { label: 'Photograph', missing: 'No usable cover photo', href: (p) => `/Projects/${p.projectId}/Photos` },
    capabilityOverview: { label: 'Capability overview', missing: 'Not recorded in the project', describe: true },
    projectBrief: { label: 'Project brief', missing: 'Not recorded', href: (p) => `${p.openUrl}?content=brief#content-brief` },
    keyPoints: { label: 'Key points', missing: 'None recorded', href: (p) => `${p.openUrl}?content=keypoints#content-keypoints` },
    updateSheetFacts: { label: 'Update-sheet facts', missing: 'Some selected rows are empty', href: (p) => p.openUrl }
  };
  const FIX_IN_PROJECT = 'Fix in project';
  const THIS_DECK_ONLY = 'This deck only';

  let slides = [];
  let images = {};
  let entries = [];
  let projects = new Map();
  let gaps = {};
  let current = 0;
  let view = 'slide';
  let filterGap = '';
  let query = '';
  const collapsed = new Set();
  let controller = null;
  let refreshTimer = 0;
  let lastBuiltAt = 0;
  let hasProjects = root.dataset.hasProjects === 'true';
  let viewerIndex = 0;
  let selectedKey = '';
  let outlineSortables = [];
  // The deck's template and format decide what "own slides" and "table row only" mean.
  const deckShape = { layout: root.dataset.layout || '', mode: root.dataset.presentationMode || '' };
  const deckKey = root.closest('[data-deck-id]')?.dataset.deckId || '';
  const placeStorageKey = `projectBriefingStudio:${deckKey}:place`;
  const readPlace = () => {
    try {
      return JSON.parse(window.sessionStorage.getItem(placeStorageKey) || 'null') || {};
    } catch {
      return {};
    }
  };
  const savedPlace = readPlace();
  const narrowScreen = () => Boolean(window.matchMedia?.('(max-width: 899.98px)').matches);

  // Phones: the outline is a sheet under the slide, opened with "Show slides". The CSS decides
  // when (the Studio's width); the toggle is only displayed then.
  const outlineAside = root.querySelector('.pbd-studio__outline');
  const outlineToggle = root.querySelector('[data-pbd-outline-toggle]');
  const outlineToggleText = outlineToggle?.querySelector('[data-pbd-outline-toggle-text]');
  const outlineIsSheet = () => Boolean(outlineToggle && window.getComputedStyle(outlineToggle).display !== 'none');
  const setOutlineOpen = (open) => {
    outlineAside?.classList.toggle('is-open', open);
    outlineToggle?.setAttribute('aria-expanded', String(open));
    if (outlineToggleText) outlineToggleText.textContent = open ? 'Hide slides' : 'Show slides';
  };
  outlineToggle?.addEventListener('click', () => setOutlineOpen(!outlineAside?.classList.contains('is-open')));

  const el = (tag, className, text) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined && text !== null) node.textContent = text;
    return node;
  };
  const icon = (name) => {
    const node = el('i', `bi ${name}`);
    node.setAttribute('aria-hidden', 'true');
    return node;
  };
  const plural = (count, word) => `${count} ${word}${count === 1 ? '' : 's'}`;

  const readStudioData = (data) => {
    if (!data) return;
    projects = new Map((data.projects || []).map((project) => [project.projectId, project]));
    gaps = data.contentGaps || {};
  };
  try {
    readStudioData(JSON.parse(root.querySelector('[data-pbd-studio-data]')?.textContent || 'null'));
  } catch {
    readStudioData(null);
  }

  const gapsFor = (projectId) => gaps[projectId] || gaps[String(projectId)] || [];

  // Re-rendering a panel replaces its nodes. Put keyboard focus (and a text field's caret) back
  // on the equivalent control, so a keyboard user is not dropped to the top of the page.
  const focusKeyOf = (node) => node?.dataset?.pbdFocusKey || (node?.id ? `#${node.id}` : '');
  const keepFocus = (container, render) => {
    const active = document.activeElement;
    const key = container && active && active !== container && container.contains(active) ? focusKeyOf(active) : '';
    const caret = key && typeof active.selectionStart === 'number' ? [active.selectionStart, active.selectionEnd] : null;
    render();
    if (!key) return;
    const target = key.startsWith('#')
      ? container.querySelector(`#${CSS.escape(key.slice(1))}`)
      : container.querySelector(`[data-pbd-focus-key="${CSS.escape(key)}"]`);
    if (!target || target.disabled) return;
    target.focus({ preventScroll: true });
    if (caret && typeof target.setSelectionRange === 'function') {
      try { target.setSelectionRange(caret[0], caret[1]); } catch { /* not a text field */ }
    }
  };

  // Each slide is a fixed 1280 × 720 canvas; its frame scales it to the frame's width.
  const scaleObserver = new ResizeObserver((records) => records.forEach((record) => scaleFrame(record.target)));
  const scaleFrame = (frame) => {
    const stage = frame.querySelector('.pbd-slide-stage');
    if (!stage) return;
    const designWidth = parseFloat(stage.style.width) || 1280;
    if (frame.clientWidth > 0) stage.style.transform = `scale(${frame.clientWidth / designWidth})`;
  };
  const releaseFrames = (container) =>
    container?.querySelectorAll('.pbd-slide-frame').forEach((frame) => scaleObserver.unobserve(frame));
  const buildFrame = (slide) => {
    const frame = el('div', 'pbd-slide-frame');
    // Slide HTML is produced server-side from the composed deck with all text encoded.
    frame.innerHTML = slide.html;
    frame.querySelectorAll('img[data-pbd-preview-image]').forEach((image) => {
      const source = images[image.dataset.pbdPreviewImage];
      if (source) image.src = source;
    });
    scaleObserver.observe(frame);
    return frame;
  };

  // Outline entries: one per slide, except that a project's slides, and runs of folding slides
  // (status-table pages, category pages, FFC breakdown pages), share one entry.
  const buildEntries = (outline) => {
    const result = [];
    const byProject = new Map();
    outline.forEach((item, index) => {
      const role = ROLES[item.role] || { section: 'portfolio', label: item.role };
      if (role.section === 'project' && item.projectId) {
        const existing = byProject.get(item.projectId);
        if (existing) {
          existing.indices.push(index);
          return;
        }
        const entry = { key: `project-${item.projectId}`, section: 'project', projectId: item.projectId, role: item.role, indices: [index] };
        byProject.set(item.projectId, entry);
        result.push(entry);
        return;
      }
      const previous = result.at(-1);
      if (role.fold && previous && previous.role === item.role) {
        previous.indices.push(index);
        return;
      }
      result.push({ key: `${item.role}-${index}`, section: role.section, role: item.role, indices: [index] });
    });
    // A project without slides of its own (an executive-table deck, or a table row only project)
    // is listed against the table page that shows it, so the Checks filter and inspector work too.
    outline.forEach((item, index) => {
      (item.listedProjectIds || []).forEach((projectId) => {
        if (byProject.has(projectId)) return;
        const entry = { key: `project-${projectId}`, section: 'project', projectId, role: item.role, indices: [index] };
        byProject.set(projectId, entry);
        result.push(entry);
      });
    });
    // Projects kept in the deck but not in the PowerPoint (left out, deleted or archived) have
    // no slide; they are listed last so they can be brought back or removed.
    [...projects.values()]
      .filter((project) => !project.isPresented && !byProject.has(project.projectId))
      .sort((first, second) => (first.sortOrder ?? 0) - (second.sortOrder ?? 0))
      .forEach((project) => {
        result.push({ key: `project-${project.projectId}`, section: 'leftout', projectId: project.projectId, role: '', indices: [] });
      });
    return result;
  };

  // How the project appears, for the outline row and the inspector.
  const presentationTag = (project) => {
    if (!project) return '';
    if (project.isDeletedOrArchived) return project.lifecycleDisplay || 'Archived';
    if (project.presentation === 'LeftOut') return 'Left out';
    if (project.presentation === 'TableRowOnly' && deckShape.mode !== 'ExecutiveTable') return 'Table row';
    return '';
  };

  const PRESENTATIONS = ['Slides', 'TableRowOnly', 'LeftOut'];
  const presentationLabel = (value) => {
    if (value === 'Slides') return deckShape.layout === 'ProjectUpdateSheet' ? 'Update sheet' : 'Own slides';
    if (value === 'TableRowOnly') return 'Table row only';
    return 'Leave out';
  };
  const presentationMeaning = (value) => {
    const sheets = deckShape.layout === 'ProjectUpdateSheet';
    if (value === 'LeftOut') return 'Kept in this deck but not in the PowerPoint. Choose another option to bring it back.';
    if (value === 'TableRowOnly') {
      if (!sheets && deckShape.mode === 'ExecutiveTable') return 'A row in the status table. In this format every project is a table row.';
      if (!sheets && deckShape.mode === 'Combined') return 'A row in the status table, with no slides of its own.';
      return `Listed in the “Other projects in this briefing” table after the ${sheets ? 'update sheets' : 'project slides'}.`;
    }
    if (sheets) return 'Its own update sheet.';
    if (deckShape.mode === 'ExecutiveTable') return 'A row in the status table.';
    if (deckShape.mode === 'Combined') return 'Its own slides and a row in the status table.';
    return 'Its own slides.';
  };

  // Asks the builder (which owns every deck save) to change how a project appears.
  const presentationControl = (project) => {
    const wrapper = el('div', 'pbd-inspector__presentation');
    const heading = el('h5', 'pbd-inspector__heading', 'In this deck');
    heading.id = `pbd-presentation-${project.projectId}`;
    const group = el('div', 'pbd-segmented');
    group.setAttribute('role', 'group');
    group.setAttribute('aria-labelledby', heading.id);
    PRESENTATIONS.forEach((value) => {
      const button = el('button', 'pbd-segmented__option', presentationLabel(value));
      button.type = 'button';
      button.dataset.pbdPresentation = value;
      button.setAttribute('aria-pressed', String((project.presentation || 'Slides') === value));
      button.addEventListener('click', () => {
        const previous = project.presentation || 'Slides';
        if (previous === value) return;
        project.presentation = value;
        project.isPresented = value !== 'LeftOut' && !project.isDeletedOrArchived;
        renderOutline();
        renderStage();
        root.querySelector(`[data-pbd-presentation="${value}"]`)?.focus();
        document.dispatchEvent(new CustomEvent('pbd:set-project-presentation', { detail: { projectId: project.projectId, presentation: value, previous } }));
      });
      group.append(button);
    });
    wrapper.append(heading, group, el('p', 'pbd-inspector__note', presentationMeaning(project.presentation || 'Slides')));
    return wrapper;
  };

  // Photograph: the deck can show any of the project's photos instead of its cover.
  let photoPicker = { projectId: null, photos: null, loading: false, saving: false, error: '' };
  const usesPhoto = (project) => !project.isDeletedOrArchived
    && (project.presentation || 'Slides') === 'Slides'
    && (deckShape.layout === 'ProjectUpdateSheet' || deckShape.mode !== 'ExecutiveTable');
  const refreshInspector = () => renderSlideInspector(entryForIndex(current));
  const closePhotoPicker = () => {
    photoPicker = { projectId: null, photos: null, loading: false, saving: false, error: '' };
  };

  const loadPhotoOptions = async (project) => {
    photoPicker = { projectId: project.projectId, photos: null, loading: true, saving: false, error: '' };
    refreshInspector();
    try {
      const url = new URL(page.dataset.photoOptionsUrl, window.location.origin);
      url.searchParams.set('deckId', deckKey);
      url.searchParams.set('projectId', String(project.projectId));
      const response = await fetch(url, {
        credentials: 'same-origin',
        headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
      });
      const payload = await response.json().catch(() => null);
      if (!response.ok) throw new Error(payload?.message || `The photographs could not be loaded (${response.status}).`);
      if (photoPicker.projectId !== project.projectId) return;
      photoPicker = { ...photoPicker, photos: Array.isArray(payload?.photos) ? payload.photos : [], loading: false };
    } catch (error) {
      if (photoPicker.projectId !== project.projectId) return;
      photoPicker = { ...photoPicker, loading: false, error: error.message || 'The photographs could not be loaded.' };
    }
    refreshInspector();
  };

  const choosePhoto = (project, photo) => {
    // The cover is stored as "no choice", so the deck follows the project if its cover changes.
    const photoId = photo.isDefault ? null : photo.photoId;
    if ((project.chosenPhotoId ?? null) === photoId || photoPicker.saving) return;
    photoPicker = { ...photoPicker, saving: true, error: '' };
    refreshInspector();
    document.dispatchEvent(new CustomEvent('pbd:set-project-photo', { detail: { projectId: project.projectId, photoId } }));
  };

  const photoSection = (project) => {
    const section = el('div', 'pbd-inspector__photo');
    section.append(el('h5', 'pbd-inspector__heading', 'Photograph'));
    section.append(el('p', 'pbd-inspector__note', project.chosenPhotoId
      ? 'Chosen for this deck. The project’s own cover is unchanged.'
      : (project.hasCoverPhoto ? 'The project’s cover photo.' : (project.coverPhotoReadinessReason || 'No usable photograph yet.'))));
    if (photoPicker.projectId !== project.projectId) {
      section.append(actionButton(project.chosenPhotoId ? 'Change photograph' : 'Choose a photograph', () => loadPhotoOptions(project)));
      return section;
    }
    if (photoPicker.loading) {
      section.append(el('p', 'pbd-inspector__note', 'Loading the photographs…'));
      return section;
    }
    if (photoPicker.error) section.append(el('p', 'pbd-inspector__note is-error', photoPicker.error));
    const photos = photoPicker.photos || [];
    if (photos.length === 0 && !photoPicker.error) {
      section.append(el('p', 'pbd-inspector__note', 'This project has no photographs yet.'));
      section.append(actionLink('Add in Photos', `/Projects/${project.projectId}/Photos`, project.projectId));
      return section;
    }
    const list = el('ul', 'pbd-photo-picker');
    list.setAttribute('aria-label', `Photographs of ${project.projectName}`);
    photos.forEach((photo, index) => {
      const item = el('li');
      const button = el('button', 'pbd-photo-picker__option');
      button.type = 'button';
      button.disabled = photoPicker.saving;
      const selected = project.chosenPhotoId ? photo.photoId === project.chosenPhotoId : photo.isDefault;
      button.setAttribute('aria-pressed', String(Boolean(selected)));
      const image = el('img');
      image.src = photo.thumbnailUrl;
      image.alt = photo.caption || `Photograph ${index + 1}`;
      image.loading = 'lazy';
      button.append(image);
      const tags = [photo.isDefault ? 'Cover' : '', photo.isLowResolution ? 'Low resolution' : ''].filter(Boolean);
      if (tags.length) button.append(el('span', 'pbd-photo-picker__tag', tags.join(' · ')));
      button.title = [photo.caption, ...tags].filter(Boolean).join(' · ') || image.alt;
      button.addEventListener('click', () => choosePhoto(project, photo));
      item.append(button);
      list.append(item);
    });
    section.append(list);
    if (photoPicker.saving) section.append(el('p', 'pbd-inspector__note', 'Saving…'));
    section.append(actionButton('Done', () => {
      closePhotoPicker();
      refreshInspector();
    }));
    return section;
  };

  // Speaker notes: written into the PowerPoint's notes for the project's first slide. Each
  // project keeps its own draft until it is saved, through rebuilds, other saves and moving
  // between projects.
  const NOTES_MAX = 4000;
  const notesDrafts = new Map();
  const hasOwnSlides = (project) => !project.isDeletedOrArchived
    && (project.presentation || 'Slides') === 'Slides'
    && (deckShape.layout === 'ProjectUpdateSheet' || deckShape.mode !== 'ExecutiveTable');
  const notesText = (project) => notesDrafts.get(project.projectId)?.text ?? (project.speakerNotes || '');
  const differsFromSaved = (projectId, text) => text.trim() !== (projects.get(projectId)?.speakerNotes || '').trim();
  // Tells the builder (Generate) whether any project holds notes that are not saved.
  const markUnsavedNotes = () => {
    const unsaved = [...notesDrafts].some(([projectId, draft]) => projects.has(projectId) && differsFromSaved(projectId, draft.text));
    root.dataset.unsavedNotes = String(unsaved);
  };

  // `update` refreshes the notes box in place: rebuilding the whole panel on blur would replace
  // the button the person is clicking (the click is then lost) and drop keyboard focus.
  const saveNotes = (project, update) => {
    const draft = notesDrafts.get(project.projectId);
    const text = notesText(project).trim();
    if (draft?.saving) return;
    if (!differsFromSaved(project.projectId, text)) {
      notesDrafts.delete(project.projectId);
      markUnsavedNotes();
      return;
    }
    if (text.length > NOTES_MAX) return;
    notesDrafts.set(project.projectId, { text, saving: true, error: '' });
    if (update) update();
    else refreshInspector();
    document.dispatchEvent(new CustomEvent('pbd:set-project-notes', { detail: { projectId: project.projectId, notes: text } }));
  };

  const notesSection = (project) => {
    const section = el('div', 'pbd-inspector__notes');
    const label = el('label', 'pbd-inspector__heading', 'Speaker notes');
    const field = el('textarea', 'form-control form-control-sm');
    field.id = `pbd-speaker-notes-${project.projectId}`;
    label.htmlFor = field.id;
    field.rows = 5;
    field.maxLength = NOTES_MAX;
    field.placeholder = 'What to say on this project. Shown to the presenter, not on the slide.';
    field.value = notesText(project);
    field.disabled = Boolean(notesDrafts.get(project.projectId)?.saving);
    const footer = el('div', 'pbd-inspector__notes-footer');
    const state = el('small', 'pbd-inspector__notes-state');
    const counter = el('small', 'pbd-inspector__notes-count');
    const save = el('button', 'btn btn-sm btn-outline-primary', 'Save notes');
    save.type = 'button';
    save.dataset.pbdFocusKey = 'notes-save';
    const refresh = () => {
      const draft = notesDrafts.get(project.projectId);
      field.disabled = Boolean(draft?.saving);
      const dirty = field.value.trim() !== (project.speakerNotes || '').trim();
      counter.textContent = `${field.value.length} / ${NOTES_MAX}`;
      save.disabled = !dirty || field.disabled;
      if (draft?.error) {
        state.textContent = draft.error;
        state.className = 'pbd-inspector__notes-state is-error';
      } else {
        state.textContent = field.disabled ? 'Saving…' : (dirty ? 'Not saved yet' : (project.speakerNotes ? 'Saved. On the project’s first slide.' : ''));
        state.className = 'pbd-inspector__notes-state';
      }
    };
    field.addEventListener('input', () => {
      notesDrafts.set(project.projectId, { text: field.value, saving: false, error: '' });
      markUnsavedNotes();
      refresh();
    });
    field.addEventListener('blur', () => saveNotes(project, refresh));
    save.addEventListener('click', () => saveNotes(project, refresh));
    refresh();
    footer.append(state, counter, save);
    section.append(label, field, footer);
    return section;
  };

  const entryTitle = (entry) => {
    if (entry.projectId) return projects.get(entry.projectId)?.projectName || slides[entry.indices[0]]?.title || 'Project';
    const title = (slides[entry.indices[0]]?.title || ROLES[entry.role]?.label || '').replace(/\s*\(\d+\/\d+\)$/, '');
    return entry.indices.length > 1 ? `${title} · ${plural(entry.indices.length, 'slide')}` : title;
  };

  // Several entries can share a slide (projects on one status-table page); the one the person
  // chose stays selected while they remain on that slide.
  const entryForIndex = (index) => {
    const chosen = selectedKey ? entries.find((entry) => entry.key === selectedKey) : null;
    // A project with no slide stays selected until another entry or slide is chosen.
    if (chosen && (chosen.indices.includes(index) || chosen.indices.length === 0)) return chosen;
    return entries.find((entry) => entry.indices.includes(index) && !entry.projectId)
      || entries.find((entry) => entry.indices.includes(index));
  };

  const matchesFilters = (entry) => {
    if (filterGap && !(entry.projectId && gapsFor(entry.projectId).includes(filterGap))) return false;
    if (!query) return true;
    const project = entry.projectId ? projects.get(entry.projectId) : null;
    const haystack = [entryTitle(entry), project?.presentStage, project?.projectCategory, project?.technicalCategory]
      .filter(Boolean).join(' ').toLowerCase();
    return haystack.includes(query);
  };

  const groupsForOutline = () => {
    const groups = [];
    SECTIONS.forEach((section) => {
      const sectionEntries = entries.filter((entry) => entry.section === section.key && matchesFilters(entry));
      if (section.key !== 'project') {
        if (sectionEntries.length) groups.push({ key: section.key, label: section.label, entries: sectionEntries });
        return;
      }
      // Projects are grouped by present stage, in the deck's (maturity) order.
      const byStage = new Map();
      sectionEntries.forEach((entry) => {
        const stage = projects.get(entry.projectId)?.presentStage || 'Stage not recorded';
        if (!byStage.has(stage)) byStage.set(stage, []);
        byStage.get(stage).push(entry);
      });
      byStage.forEach((stageEntries, stage) => groups.push({ key: `stage-${stage}`, label: stage, entries: stageEntries, isStage: true }));
    });
    return groups;
  };

  // Reordering stays inside a present stage, which is how the generator orders project slides.
  // The full order sent back keeps every other project (left-out ones included) where it was.
  const requestReorder = (stageProjectIds) => {
    const moving = new Set(stageProjectIds);
    const existingOrder = [...projects.values()]
      .sort((first, second) => (first.sortOrder ?? 0) - (second.sortOrder ?? 0) || first.projectId - second.projectId)
      .map((project) => project.projectId);
    const queue = [...stageProjectIds];
    const order = existingOrder.map((projectId) => (moving.has(projectId) ? queue.shift() : projectId));
    order.forEach((projectId, index) => {
      const project = projects.get(projectId);
      if (project) project.sortOrder = (index + 1) * 10;
    });
    document.dispatchEvent(new CustomEvent('pbd:reorder-projects', { detail: { projectIds: order } }));
  };

  const stageIdsFromList = (list) => [...list.querySelectorAll('[data-pbd-outline-project]')]
    .map((row) => Number(row.dataset.pbdOutlineProject))
    .filter(Number.isInteger);

  const canReorder = () => !query && !filterGap;

  // A rebuild (e.g. a preview finishing) must not destroy a drag in progress: wait for the drop.
  let dragging = false;
  let outlinePending = false;
  const renderOutline = () => {
    if (!outlineNav) return;
    if (dragging) {
      outlinePending = true;
      return;
    }
    outlinePending = false;
    keepFocus(outlineNav, drawOutline);
  };

  const drawOutline = () => {
    outlineSortables.forEach((sortable) => sortable.destroy());
    outlineSortables = [];
    const groups = groupsForOutline();
    if (groups.length === 0) {
      outlineNav.replaceChildren(el('p', 'pbd-outline__empty', slides.length ? 'No slides match.' : 'No slides yet.'));
      return;
    }
    let lastSection = '';
    const nodes = [];
    groups.forEach((group) => {
      if (group.isStage && lastSection !== 'project') {
        nodes.push(el('p', 'pbd-outline__section', 'Projects'));
      }
      lastSection = group.isStage ? 'project' : group.key;
      const open = !collapsed.has(group.key) || Boolean(query || filterGap);
      const wrapper = el('div', 'pbd-outline__group');
      const header = el('button', group.isStage ? 'pbd-outline__group-toggle is-stage' : 'pbd-outline__group-toggle');
      header.type = 'button';
      header.dataset.pbdFocusKey = `group:${group.key}`;
      header.setAttribute('aria-expanded', String(open));
      header.append(icon('bi-chevron-right'), el('span', 'pbd-outline__group-label', group.label), el('span', 'pbd-outline__group-count', String(group.entries.length)));
      header.addEventListener('click', () => {
        if (collapsed.has(group.key)) collapsed.delete(group.key);
        else collapsed.add(group.key);
        renderOutline();
      });
      wrapper.append(header);
      if (open) {
        const list = el('ul', 'pbd-outline__rows');
        group.entries.forEach((entry) => {
          const item = el('li');
          const row = el('button', 'pbd-outline__row');
          row.type = 'button';
          row.dataset.pbdOutlineKey = entry.key;
          row.dataset.pbdFocusKey = `outline:${entry.key}`;
          if (group.isStage && entry.projectId) {
            row.dataset.pbdOutlineProject = String(entry.projectId);
            if (canReorder()) {
              const grip = el('span', 'pbd-outline__grip');
              grip.append(icon('bi-grip-vertical'));
              grip.title = 'Drag to reorder within this stage';
              item.append(grip);
              row.setAttribute('aria-keyshortcuts', 'Alt+ArrowUp Alt+ArrowDown');
              row.addEventListener('keydown', (event) => {
                if (!event.altKey || (event.key !== 'ArrowUp' && event.key !== 'ArrowDown')) return;
                event.preventDefault();
                event.stopPropagation();
                const sibling = event.key === 'ArrowUp' ? item.previousElementSibling : item.nextElementSibling;
                if (!sibling) return;
                if (event.key === 'ArrowUp') item.parentElement.insertBefore(item, sibling);
                else item.parentElement.insertBefore(sibling, item);
                row.focus();
                requestReorder(stageIdsFromList(item.parentElement));
              });
            }
          }
          const number = entry.indices.length ? slides[entry.indices[0]]?.number ?? entry.indices[0] + 1 : '–';
          const title = entryTitle(entry);
          row.title = title;
          row.append(el('span', 'pbd-outline__number', String(number)), el('span', 'pbd-outline__name', title));
          const project = entry.projectId ? projects.get(entry.projectId) : null;
          const tag = presentationTag(project);
          if (tag) row.append(el('span', 'pbd-outline__tag', tag));
          const missing = project?.isPresented ? gapsFor(entry.projectId) : [];
          if (missing.length) {
            const names = missing.map((key) => GAPS[key]?.label.toLowerCase() || key).join(', ');
            const badge = el('span', 'pbd-outline__gaps');
            const count = el('span', null, String(missing.length));
            count.setAttribute('aria-hidden', 'true');
            badge.prepend(icon('bi-exclamation-triangle'));
            badge.append(count, el('span', 'visually-hidden', ` (${plural(missing.length, 'gap')}: ${names})`));
            badge.title = `${plural(missing.length, 'gap')}: ${names}. The slides show a placeholder until it is filled; select the slide to fill it.`;
            row.append(badge);
          }
          row.addEventListener('click', () => {
            selectEntry(entry, { openSlideTab: true });
            // On a phone the sheet folds away and the chosen slide comes into view.
            if (outlineIsSheet()) {
              setOutlineOpen(false);
              // After the folded sheet has left the layout.
              window.requestAnimationFrame(() => root.querySelector('.pbd-studio__stage')?.scrollIntoView({ block: 'start' }));
            }
          });
          item.append(row);
          list.append(item);
        });
        wrapper.append(list);
        if (group.isStage && canReorder() && group.entries.length > 1 && typeof window.Sortable?.create === 'function') {
          outlineSortables.push(window.Sortable.create(list, {
            handle: '.pbd-outline__grip',
            animation: 130,
            ghostClass: 'is-dragging',
            onStart: () => { dragging = true; },
            onEnd: (event) => {
              dragging = false;
              if (event.oldIndex !== event.newIndex) requestReorder(stageIdsFromList(list));
              if (outlinePending) renderOutline();
            }
          }));
        }
      }
      nodes.push(wrapper);
    });
    outlineNav.replaceChildren(...nodes);
    markOutlineSelection();
  };

  const rememberPlace = () => {
    try {
      const entry = entryForIndex(current);
      window.sessionStorage.setItem(placeStorageKey, JSON.stringify({
        number: slides[current]?.number || null,
        projectId: entry?.projectId || null,
        view,
        tab: tabs.find((tab) => tab.classList.contains('is-active'))?.dataset.pbdInspectorTab || 'slide',
        filter: filterGap || null
      }));
    } catch {
      // Private windows can refuse storage; the Studio then starts at the first slide.
    }
  };

  const markOutlineSelection = () => {
    rememberPlace();
    const entry = entryForIndex(current);
    outlineNav?.querySelectorAll('[data-pbd-outline-key]').forEach((row) => {
      const selected = entry && row.dataset.pbdOutlineKey === entry.key;
      row.classList.toggle('is-selected', Boolean(selected));
      if (selected) row.setAttribute('aria-current', 'true');
      else row.removeAttribute('aria-current');
    });
  };

  // Deck settings live in the Deck tab, as numbered steps; this opens the first one.
  const showDeckSettings = () => {
    setTab('deck');
    const first = [...root.querySelectorAll('[data-pbd-step]')].find((step) => !step.hidden);
    if (first instanceof HTMLDetailsElement) first.open = true;
    first?.querySelector('summary')?.focus();
  };

  const actionButton = (label, onClick, className = 'btn btn-sm btn-outline-secondary') => {
    const button = el('button', className, label);
    button.type = 'button';
    button.addEventListener('click', onClick);
    return button;
  };
  // Project pages open in the one named tab PRISM keeps per project (ProjectTab.Name).
  const actionLink = (label, href, projectId, className = 'btn btn-sm btn-outline-secondary') => {
    const link = el('a', className, label);
    link.href = href;
    link.target = projectId ? `prism-project-${projectId}` : '_blank';
    link.rel = 'noopener';
    return link;
  };
  // The capability-overview editor belongs to project-briefing-decks.js; this button carries
  // the attributes its delegated handler reads.
  const describeButton = (project, label) => {
    const button = el('button', 'btn btn-sm btn-outline-primary', label);
    button.type = 'button';
    button.dataset.pbdEditDescription = '';
    button.dataset.projectId = String(project.projectId);
    button.dataset.projectName = project.projectName;
    button.dataset.description = project.briefDescriptionOverride || '';
    return button;
  };

  // What the project's slides lack, at the top of This slide: the one place each gap shows, with
  // the fix and where it lands.
  const gapsSection = (project) => {
    const missing = project.isPresented ? gapsFor(project.projectId) : [];
    if (missing.length === 0) {
      const done = el('p', 'pbd-stage__check is-ready pbd-inspector__ready');
      done.append(icon('bi-check-circle'), el('span', null, 'No gaps: everything these slides show is recorded.'));
      return done;
    }
    const section = el('section', 'pbd-inspector__gaps');
    section.setAttribute('aria-label', 'Gaps on these slides');
    section.append(el('h5', 'pbd-inspector__heading', `Gaps · ${missing.length}`));
    const list = el('ul');
    missing.forEach((key) => {
      const gap = GAPS[key] || { label: key, missing: 'Missing', href: (p) => p.openUrl };
      const item = el('li');
      const text = el('span', 'pbd-inspector__gap-text');
      text.append(el('strong', null, gap.label),
        el('small', null, key === 'photo' && project.coverPhotoReadinessReason ? project.coverPhotoReadinessReason : gap.missing));
      item.append(text);
      if (gap.describe && usesCapability) {
        const write = describeButton(project, THIS_DECK_ONLY);
        write.title = 'Write the text for this deck. The project’s own record is unchanged.';
        write.prepend(icon('bi-pencil'), document.createTextNode(' '));
        item.append(write);
      } else if (gap.href) {
        const fix = actionLink(FIX_IN_PROJECT, gap.href(project), project.projectId);
        fix.title = 'Opens the project. The change shows in every deck and page.';
        fix.append(document.createTextNode(' '), icon('bi-box-arrow-up-right'));
        item.append(fix);
      }
      list.append(item);
    });
    section.append(list, el('p', 'pbd-inspector__note', 'Until a gap is filled the slides show a placeholder.'));
    return section;
  };

  const renderSlideInspector = (entry) => {
    if (!panels.slide) return;
    keepFocus(panels.slide, () => drawSlideInspector(entry));
  };

  const drawSlideInspector = (entry) => {
    const panel = panels.slide;
    const slide = slides[current];
    // A project with no slide (left out) is still shown, even when the deck has no slides at all.
    if (!entry || (!slide && entry.indices.length)) {
      panel.replaceChildren(el('p', 'pbd-inspector__empty', hasProjects ? 'Select a slide in the outline.' : 'Add projects to build the deck.'));
      return;
    }
    const role = ROLES[entry.role] || { label: entry.role };
    const project = entry.projectId ? projects.get(entry.projectId) : null;
    const nodes = [];
    if (project) {
      nodes.push(el('span', 'pbd-eyebrow', `Project · ${project.presentStage || 'Stage not recorded'}`));
      nodes.push(el('h4', 'pbd-inspector__title', project.projectName));
      nodes.push(el('p', 'pbd-inspector__meta', [project.lifecycleDisplay, project.projectCategory, project.technicalCategory].filter(Boolean).join(' · ')));
      const missing = project.isPresented ? gapsFor(project.projectId) : [];
      if (project.isPresented) nodes.push(gapsSection(project));
      const facts = el('dl', 'pbd-inspector__facts');
      const fact = (label, value, note) => {
        const row = el('div');
        const dd = el('dd', null, value);
        if (note) dd.append(el('small', null, note));
        row.append(el('dt', null, label), dd);
        return row;
      };
      // Each field once: a cost listed under Gaps is not repeated here.
      if (!missing.includes('costRd')) facts.append(fact('Cost (R&D)', project.costRd?.displayValue || 'Not recorded', project.costRd?.basisDisplay));
      // Whether the proliferation cost shows is the server's resolved rule (an ongoing project's
      // only when recorded); missing from older payloads, it is shown as before.
      const showsProliferation = project.showsProliferationCost !== false;
      if (showsProliferation && !missing.includes('proliferationCost')) facts.append(fact('Proliferation', project.proliferationCost?.displayValue || 'Not recorded'));
      facts.append(entry.indices.length
        ? fact('Slides', plural(entry.indices.length, 'slide'), entry.indices.map((index) => slides[index]?.number).join(', '))
        : fact('Slides', 'None', 'Not in the PowerPoint'));
      nodes.push(facts);
      if (!project.isDeletedOrArchived) nodes.push(presentationControl(project));
      if (usesPhoto(project)) nodes.push(photoSection(project));
      if (hasOwnSlides(project)) nodes.push(notesSection(project));

      if (!missing.includes('status')) {
        nodes.push(el('h5', 'pbd-inspector__heading', 'Present status'));
        nodes.push(project.externalStatus
          ? el('blockquote', 'pbd-inspector__quote', project.externalStatus)
          : el('p', 'pbd-inspector__note', 'No external status remark recorded. The slides show the placeholder.'));
      }

      if (usesCapability && !missing.includes('capabilityOverview')) {
        nodes.push(el('h5', 'pbd-inspector__heading', 'Capability overview'));
        nodes.push(el('p', 'pbd-inspector__note', project.briefDescriptionOverride
          ? 'Written for this deck. The project’s own statements are unchanged.'
          : (project.capabilityStatements?.length ? `From the project: ${plural(project.capabilityStatements.length, 'statement')}.` : 'Not recorded in the project.')));
        const write = describeButton(project, project.briefDescriptionOverride ? 'Edit the text for this deck' : 'Write for this deck only');
        write.title = 'Text kept with this deck. The project’s own record is unchanged.';
        nodes.push(write);
      }

      const actions = el('div', 'pbd-inspector__actions');
      actions.append(actionLink('Open project', project.openUrl, project.projectId));
      const remove = el('button', 'btn btn-sm btn-outline-danger', 'Remove from deck');
      remove.type = 'button';
      remove.dataset.pbdFocusKey = 'remove-project';
      remove.dataset.pbdRemoveProject = '';
      remove.dataset.projectId = String(project.projectId);
      remove.dataset.projectName = project.projectName;
      actions.append(remove);
      nodes.push(actions);
    } else {
      nodes.push(el('span', 'pbd-eyebrow', role.label));
      nodes.push(el('h4', 'pbd-inspector__title', entryTitle(entry)));
      if (role.about) nodes.push(el('p', 'pbd-inspector__note', role.about));
      const actions = el('div', 'pbd-inspector__actions');
      if (role.editor && page.querySelector(role.editor)) {
        actions.append(actionButton('Edit slide content', () => page.querySelector(role.editor)?.click(), 'btn btn-sm btn-outline-primary'));
      }
      actions.append(actionButton('Deck settings', showDeckSettings));
      nodes.push(actions);
    }
    panel.replaceChildren(...nodes);
  };

  // The progress line under the deck header: projects, checks, settings, generate.
  const progress = document.querySelector('[data-pbd-progress]');
  const progressStep = (name) => progress?.querySelector(`[data-pbd-progress-step="${name}"]`);
  const setStepState = (name, state) => {
    const step = progressStep(name);
    if (!step) return;
    step.classList.remove('is-done', 'is-next', 'is-open', 'is-ready');
    if (state) step.classList.add(state);
  };
  const renderProgress = (presented, openChecks) => {
    if (!progress) return;
    const projectsValue = progress.querySelector('[data-pbd-progress-projects]');
    if (projectsValue) projectsValue.textContent = projects.size ? String(projects.size) : 'Add';
    setStepState('projects', projects.size ? 'is-done' : 'is-next');
    const checksValue = progress.querySelector('[data-pbd-progress-checks]');
    if (checksValue) checksValue.textContent = presented === 0 ? '–' : (openChecks ? `${openChecks} to fill` : 'None');
    setStepState('checks', presented === 0 ? '' : (openChecks ? 'is-open' : 'is-done'));
    setStepState('generate', presented ? 'is-ready' : '');
  };

  const renderChecksPanel = () => {
    const panel = panels.checks;
    if (!panel) return;
    const presented = [...projects.values()].filter((project) => project.isPresented);
    const total = presented.length;
    const counts = {};
    let projectsWithGaps = 0;
    presented.forEach((project) => {
      const missing = gapsFor(project.projectId);
      if (missing.length) projectsWithGaps += 1;
      missing.forEach((key) => { counts[key] = (counts[key] || 0) + 1; });
    });
    // The badge counts the checks listed in the tab (what is missing), not projects: a deck where
    // most projects lack one optional item would otherwise sit at "47" for ever.
    const openChecks = Object.keys(GAPS).filter((key) => counts[key]).length;
    if (placeholderNote) placeholderNote.hidden = openChecks === 0;
    if (checksBadge) {
      checksBadge.hidden = openChecks === 0;
      checksBadge.textContent = String(openChecks);
      checksBadge.title = `${openChecks} ${openChecks === 1 ? 'kind' : 'kinds'} of gap to fill`;
      checksBadge.setAttribute('aria-label', `${openChecks} ${openChecks === 1 ? 'kind' : 'kinds'} of gap to fill`);
    }
    renderProgress(total, openChecks);
    if (total === 0) {
      panel.replaceChildren(el('p', 'pbd-inspector__empty', 'Add projects to see the gaps.'));
      return;
    }
    const keys = Object.keys(GAPS).filter((key) => counts[key]);
    if (keys.length === 0) {
      const done = el('p', 'pbd-stage__check is-ready');
      done.append(icon('bi-check-circle'), el('span', null, 'No gaps: everything this deck shows is recorded for every project.'));
      panel.replaceChildren(done);
      return;
    }
    const list = el('ul', 'pbd-inspector__checks');
    keys.forEach((key) => {
      const item = el('li');
      const text = el('span');
      text.append(el('strong', null, GAPS[key].label), el('small', null, `${counts[key]} of ${plural(total, 'project')}`));
      const show = actionButton(filterGap === key ? 'Showing' : 'Show', () => setFilter(filterGap === key ? '' : key));
      show.setAttribute('aria-pressed', String(filterGap === key));
      show.setAttribute('aria-label', `Show the projects with a gap in ${GAPS[key].label.toLowerCase()}`);
      item.append(text, show);
      list.append(item);
    });
    panel.replaceChildren(
      el('p', 'pbd-inspector__note', `${plural(projectsWithGaps, 'project')} of ${total} ${projectsWithGaps === 1 ? 'has' : 'have'} gaps. The slides show placeholders until they are filled.`),
      list,
      el('p', 'pbd-inspector__note', 'Show narrows the outline to those projects; fill each one from This slide.'));
  };

  const setFilter = (key) => {
    filterGap = key;
    rememberPlace();
    // Show narrows the outline; on a phone, open the sheet so the narrowed list is seen.
    if (key && outlineIsSheet()) setOutlineOpen(true);
    filterGap = key;
    if (filterBox) filterBox.hidden = !key;
    if (filterText) filterText.textContent = key ? `Projects with a gap in ${GAPS[key]?.label.toLowerCase() || key}` : '';
    renderOutline();
    renderChecksPanel();
  };

  const renderGrid = () => {
    if (!gridBox) return;
    releaseFrames(gridBox);
    gridBox.replaceChildren(...slides.map((slide, index) => {
      const item = el('li');
      const button = el('button', index === current ? 'pbd-stage__thumb is-selected' : 'pbd-stage__thumb');
      button.type = 'button';
      button.setAttribute('aria-label', `Slide ${slide.number}: ${slide.title}`);
      const caption = el('span', 'pbd-stage__thumb-caption');
      caption.append(el('b', null, String(slide.number)), el('span', null, slide.title));
      button.append(buildFrame(slide), caption);
      button.addEventListener('click', () => {
        setTab('slide');
        setView('slide');
        selectIndex(index);
      });
      item.append(button);
      return item;
    }));
  };

  const renderStage = () => {
    const slide = slides[current];
    const entry = entryForIndex(current);
    if (prevButton) prevButton.disabled = !slide || current === 0;
    if (nextButton) nextButton.disabled = !slide || current >= slides.length - 1;
    if (entry && entry.indices.length === 0) {
      const project = projects.get(entry.projectId);
      if (stageMeta) stageMeta.textContent = 'Not in the PowerPoint';
      if (stageTitle) stageTitle.textContent = project?.projectName || 'Project';
      if (view === 'slide' && slideBox) {
        releaseFrames(slideBox);
        slideBox.replaceChildren(el('p', 'pbd-stage__empty', project?.isDeletedOrArchived
          ? 'This project is deleted or archived, so the PowerPoint leaves it out. Remove it from the deck, or restore the project.'
          : 'This project is left out of the PowerPoint. Choose Own slides or Table row only in the inspector to bring it back.'));
      }
      renderSlideInspector(entry);
      return;
    }
    if (!slide) {
      if (stageMeta) stageMeta.textContent = 'Slide preview';
      releaseFrames(slideBox);
      slideBox?.replaceChildren();
      renderSlideInspector(null);
      return;
    }
    const role = ROLES[entry?.role] || { label: '' };
    if (stageMeta) stageMeta.textContent = `Slide ${slide.number} of ${slides.length}${role.label ? ` · ${role.label}` : ''}`;
    if (stageTitle) stageTitle.textContent = slide.title;
    if (view === 'slide' && slideBox) {
      releaseFrames(slideBox);
      slideBox.replaceChildren(buildFrame(slide));
    }
    renderSlideInspector(entry);
    gridBox?.querySelectorAll('.pbd-stage__thumb').forEach((thumb, index) => thumb.classList.toggle('is-selected', index === current));
  };

  const selectEntry = (entry, { openSlideTab = false } = {}) => {
    if (openSlideTab) setTab('slide');
    if (entry.indices.length) {
      selectIndex(entry.indices[0], { from: 'outline', key: entry.key });
      return;
    }
    selectedKey = entry.key;
    if (view !== 'slide') setView('slide');
    else renderStage();
    markOutlineSelection();
  };

  const selectIndex = (index, { from, key = '' } = {}) => {
    if (!slides.length) return;
    current = Math.max(0, Math.min(index, slides.length - 1));
    selectedKey = key;
    renderStage();
    markOutlineSelection();
    if (from !== 'outline') {
      outlineNav?.querySelector('.pbd-outline__row.is-selected')?.scrollIntoView({ block: 'nearest' });
    }
  };

  const setView = (next) => {
    view = next;
    viewButtons.forEach((button) => {
      const active = button.dataset.pbdStageView === view;
      button.classList.toggle('is-active', active);
      button.setAttribute('aria-pressed', String(active));
    });
    if (slideBox) slideBox.hidden = view !== 'slide';
    if (gridBox) gridBox.hidden = view !== 'grid';
    if (view === 'grid') renderGrid();
    else {
      releaseFrames(gridBox);
      gridBox?.replaceChildren();
    }
    renderStage();
  };

  const setTab = (name, { focus = false } = {}) => {
    tabs.forEach((tab) => {
      const active = tab.dataset.pbdInspectorTab === name;
      tab.classList.toggle('is-active', active);
      tab.setAttribute('aria-selected', String(active));
      tab.tabIndex = active ? 0 : -1;
      if (active && focus) tab.focus();
    });
    Object.entries(panels).forEach(([key, panel]) => { panel.hidden = key !== name; });
    rememberPlace();
  };

  const updateCounts = (payload) => {
    const label = plural(payload.slideCount, 'slide');
    if (countLabel) countLabel.textContent = String(payload.slideCount);
    if (slideTotal) slideTotal.textContent = label;
    if (slideBreakdown) slideBreakdown.textContent = 'Exact count from the slide preview.';
    // One line, so the slide starts higher; the promise about Generate stays on hover.
    if (status) {
      status.textContent = `${label} · built ${payload.generatedAt} from current PRISM data`;
      status.title = 'Generate PowerPoint downloads exactly these slides.';
    }
  };

  const renderEmpty = (message) => {
    slides = [];
    // Projects kept in the deck but not in the PowerPoint stay listed, so they can be brought back.
    entries = buildEntries([]);
    releaseFrames(gridBox);
    gridBox?.replaceChildren();
    if (presentButton) presentButton.disabled = true;
    if (status) status.textContent = message;
    if (entries.length) {
      if (!entries.some((entry) => entry.key === selectedKey)) selectedKey = entries[0].key;
      renderOutline();
      renderStage();
      renderChecksPanel();
      return;
    }
    outlineNav?.replaceChildren(el('p', 'pbd-outline__empty', 'No slides yet.'));
    renderStage();
    renderChecksPanel();
    if (countLabel) countLabel.textContent = '';
    if (status) status.textContent = '';
    // renderStage clears the slide area when there are no slides; say why after it, with the one
    // thing to do next.
    const empty = el('div', 'pbd-stage__start');
    empty.append(
      el('span', 'pbd-stage__start-icon'),
      el('h4', null, 'Add the projects to brief'),
      el('p', null, message));
    empty.querySelector('.pbd-stage__start-icon').append(icon('bi-collection'));
    if (!projects.size) {
      const add = el('button', 'btn btn-primary');
      add.type = 'button';
      add.dataset.pbdOpenSelector = '';
      add.append(icon('bi-plus-lg'), document.createTextNode(' Add projects'));
      empty.append(add);
    }
    slideBox?.replaceChildren(empty);
  };

  // The side panels take the window's height below the header (see --pbd-studio-offset in the
  // CSS); kept current as the header, the one-time note or the window change size.
  const setStudioOffset = () => {
    const top = root.getBoundingClientRect().top + window.scrollY;
    root.style.setProperty('--pbd-studio-offset', `${Math.round(top)}px`);
  };
  setStudioOffset();
  window.addEventListener('resize', setStudioOffset);
  const editor = root.closest('.pbd-editor');
  if (editor && typeof window.ResizeObserver === 'function') new window.ResizeObserver(setStudioOffset).observe(editor);

  // After a reload the outline scrolls (itself, not the page) to the slide the Studio reopened on.
  let outlinePlaced = false;
  const centreSelectedRow = () => {
    const row = outlineNav?.querySelector('.pbd-outline__row.is-selected');
    if (!row || !outlineNav) return;
    const listBox = outlineNav.getBoundingClientRect();
    const rowBox = row.getBoundingClientRect();
    outlineNav.scrollTop += (rowBox.top - listBox.top) - (listBox.height - rowBox.height) / 2;
  };

  const loadPreview = async () => {
    lastBuiltAt = Date.now();
    // Cancel a running build so an obsolete response cannot restore old slides.
    controller?.abort();
    controller = null;
    if (!hasProjects) {
      if (viewer?.open) viewer.close();
      root.classList.remove('is-busy', 'is-stale', 'is-error');
      if (refreshButton) refreshButton.disabled = false;
      renderEmpty(projects.size
        ? 'Every project in this deck is left out of the PowerPoint. Bring one back to build the slides.'
        : 'Pick ongoing projects, a category or individual projects. The slides build from current PRISM data.');
      return;
    }

    const request = new AbortController();
    controller = request;
    const keepEntry = slides.length ? entryForIndex(current) : null;
    const keepProject = slides.length ? keepEntry?.projectId : savedPlace.projectId;
    const keepNumber = slides.length ? slides[current]?.number : savedPlace.number;
    // The kind of slide in view (a project's brief, say), kept for that project after the rebuild.
    const keepRole = slides.length ? outlineItems[current]?.role : '';
    root.classList.add('is-busy');
    if (status) status.textContent = slides.length ? 'Updating the slides…' : 'Building the slides from current PRISM data…';
    if (refreshButton) refreshButton.disabled = true;
    try {
      const response = await fetch(root.dataset.previewUrl, {
        credentials: 'same-origin',
        headers: {
          Accept: 'application/json',
          'X-Requested-With': 'XMLHttpRequest',
          // Images are keyed by a hash of their bytes: the server skips the ones already here.
          'X-Pbd-Known-Images': Object.keys(images).join(',')
        },
        signal: request.signal
      });
      const payload = await response.json().catch(() => null);
      // A newer request (or an emptied deck) superseded this one: drop its response.
      if (request.signal.aborted) return;
      if (!response.ok || !payload) throw new Error(payload?.message || `The slides could not be built (${response.status}).`);
      slides = Array.isArray(payload.slides) ? payload.slides : [];
      images = { ...images, ...(payload.images || {}) };
      if (Array.isArray(payload.projects)) {
        readStudioData({ projects: payload.projects, contentGaps: payload.contentGaps });
      }
      outlineItems = Array.isArray(payload.outline) ? payload.outline : [];
      entries = buildEntries(outlineItems);
      // Keep the same project (or slide number) selected across rebuilds.
      const sameProject = keepProject ? entries.find((entry) => entry.projectId === keepProject) : null;
      const sameSlide = keepProject && keepRole
        ? outlineItems.findIndex((item) => item.projectId === keepProject && item.role === keepRole)
        : -1;
      current = sameSlide >= 0
        ? sameSlide
        : sameProject?.indices.length
          ? sameProject.indices[0]
          : Math.min(Math.max(0, (keepNumber || 1) - 1), Math.max(0, slides.length - 1));
      selectedKey = sameProject ? sameProject.key : '';
      updateCounts(payload);
      renderOutline();
      if (!outlinePlaced) {
        outlinePlaced = true;
        centreSelectedRow();
      }
      if (view === 'grid') renderGrid();
      renderStage();
      renderChecksPanel();
      // A settings change shows a slide it affects instead.
      followChange();
      if (presentButton) presentButton.disabled = slides.length === 0;
      if (viewer?.open) {
        if (slides.length === 0) viewer.close();
        else {
          buildStrip();
          showViewerSlide(Math.min(viewerIndex, slides.length - 1));
        }
      }
    } catch (error) {
      if (error.name === 'AbortError' || request.signal.aborted) return;
      if (status) status.textContent = error.message || 'The slides could not be built.';
      root.classList.add('is-error');
      return;
    } finally {
      if (controller === request) {
        controller = null;
        if (refreshButton) refreshButton.disabled = false;
        root.classList.remove('is-busy');
      }
    }
    if (request.signal.aborted) return;
    root.classList.remove('is-error', 'is-stale');
  };

  // Present: full screen where the browser allows it (otherwise the dialog fills the window),
  // the slide as large as it fits. The strip of every slide and the speaker notes open on
  // request; both choices are remembered on this browser.
  const viewerNotes = viewer?.querySelector('[data-pbd-viewer-notes]');
  const viewerNotesText = viewer?.querySelector('[data-pbd-viewer-notes-text]');
  const notesToggle = viewer?.querySelector('[data-pbd-viewer-notes-toggle]');
  const stripToggle = viewer?.querySelector('[data-pbd-viewer-strip-toggle]');
  // Keys go to the slide area, so Space moves on rather than pressing the last toolbar button.
  const viewerBody = viewer?.querySelector('[data-pbd-viewer-body]');
  const focusSlide = () => viewerBody?.focus({ preventScroll: true });
  const presentPrefsKey = 'prism.briefingDecks.present';
  const readPresentPrefs = () => {
    try { return JSON.parse(window.localStorage.getItem(presentPrefsKey) || 'null') || {}; } catch { return {}; }
  };
  const presentPrefs = { notes: false, strip: false, ...readPresentPrefs() };
  const savePresentPrefs = () => {
    try { window.localStorage.setItem(presentPrefsKey, JSON.stringify(presentPrefs)); } catch { /* not remembered */ }
  };
  // Whether this Present asked for full screen, so leaving full screen (Esc) ends it.
  let enteredFullscreen = false;

  // The notes the PowerPoint carries for the project on this slide (saved text, not a draft).
  // The slide's own outline says whose it is; the Studio's selection may be a left-out project.
  const viewerNotesFor = (index) => {
    const projectId = outlineItems[index]?.projectId;
    const project = projectId ? projects.get(projectId) : null;
    if (!project) return 'No speaker notes: this slide is not about one project.';
    return (project.speakerNotes || '').trim() || `No speaker notes for ${project.projectName}. Write them in This slide.`;
  };

  const showViewerSlide = (index) => {
    if (!viewerStage || slides.length === 0) return;
    viewerIndex = Math.max(0, Math.min(index, slides.length - 1));
    const slide = slides[viewerIndex];
    releaseFrames(viewerStage);
    viewerStage.replaceChildren(buildFrame(slide));
    if (viewerTitle) viewerTitle.textContent = slide.title;
    if (viewerCounter) viewerCounter.textContent = `${slide.number} / ${slides.length}`;
    if (viewerNotesText) viewerNotesText.textContent = viewerNotesFor(viewerIndex);
    viewerStrip?.querySelectorAll('button').forEach((button, buttonIndex) => {
      const active = buttonIndex === viewerIndex;
      button.classList.toggle('is-active', active);
      if (active) {
        button.setAttribute('aria-current', 'true');
        if (!viewerStrip.hidden) button.scrollIntoView({ block: 'nearest', inline: 'nearest' });
      } else {
        button.removeAttribute('aria-current');
      }
    });
  };

  const buildStrip = () => {
    if (!viewerStrip) return;
    releaseFrames(viewerStrip);
    viewerStrip.replaceChildren(...slides.map((slide, index) => {
      const item = el('li');
      const button = el('button', 'pbd-viewer__thumb');
      button.type = 'button';
      button.setAttribute('aria-label', `Slide ${slide.number}: ${slide.title}`);
      button.addEventListener('click', () => showViewerSlide(index));
      button.append(el('span', null, String(slide.number)), buildFrame(slide));
      item.append(button);
      return item;
    }));
  };

  const setViewerPanels = () => {
    if (viewerNotes) viewerNotes.hidden = !presentPrefs.notes;
    if (viewerStrip) viewerStrip.hidden = !presentPrefs.strip;
    notesToggle?.setAttribute('aria-pressed', String(presentPrefs.notes));
    stripToggle?.setAttribute('aria-pressed', String(presentPrefs.strip));
    viewer?.classList.toggle('has-notes', presentPrefs.notes);
    viewer?.classList.toggle('has-strip', presentPrefs.strip);
    if (presentPrefs.strip) viewerStrip?.querySelector('[aria-current="true"]')?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  };
  const toggleViewerPanel = (name) => {
    presentPrefs[name] = !presentPrefs[name];
    savePresentPrefs();
    setViewerPanels();
  };

  const openViewer = (index) => {
    if (!viewer || slides.length === 0) return;
    buildStrip();
    setViewerPanels();
    if (!viewer.open) viewer.showModal();
    showViewerSlide(index);
    focusSlide();
    // Full screen needs this click's user activation; when refused, the dialog already fills the window.
    enteredFullscreen = false;
    const screenRoot = document.documentElement;
    if (!document.fullscreenElement && typeof screenRoot.requestFullscreen === 'function') {
      screenRoot.requestFullscreen({ navigationUI: 'hide' })
        .then(() => { enteredFullscreen = true; })
        .catch(() => { /* stays in the window */ });
    }
  };

  const closeViewer = () => {
    if (viewer?.open) viewer.close();
  };

  // Esc first leaves full screen (the browser's own key); that also ends the presentation.
  document.addEventListener('fullscreenchange', () => {
    if (!document.fullscreenElement && enteredFullscreen) {
      enteredFullscreen = false;
      closeViewer();
    }
  });

  viewer?.addEventListener('keydown', (event) => {
    if (event.altKey || event.ctrlKey || event.metaKey) return;
    // Buttons keep Space and Enter for themselves.
    const onButton = event.target instanceof Element && event.target.closest('button');
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown' || event.key === 'PageDown' || (event.key === ' ' && !onButton)) {
      event.preventDefault(); showViewerSlide(viewerIndex + 1);
    } else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp' || event.key === 'PageUp') {
      event.preventDefault(); showViewerSlide(viewerIndex - 1);
    } else if (event.key === 'Home') { event.preventDefault(); showViewerSlide(0); }
    else if (event.key === 'End') { event.preventDefault(); showViewerSlide(slides.length - 1); }
    else if (event.key === 'n' || event.key === 'N') { event.preventDefault(); toggleViewerPanel('notes'); }
  });
  viewer?.addEventListener('click', (event) => { if (event.target === viewer) closeViewer(); });
  viewer?.addEventListener('close', () => {
    if (document.fullscreenElement && enteredFullscreen) {
      enteredFullscreen = false;
      document.exitFullscreen?.().catch(() => {});
    }
    [viewerStage, viewerStrip].forEach(releaseFrames);
    viewerStage?.replaceChildren();
    viewerStrip?.replaceChildren();
    // Return to the slide the presenter ended on.
    if (slides.length) selectIndex(viewerIndex);
    presentButton?.focus();
  });
  viewer?.querySelector('[data-pbd-viewer-close]')?.addEventListener('click', closeViewer);
  viewer?.querySelector('[data-pbd-viewer-prev]')?.addEventListener('click', () => showViewerSlide(viewerIndex - 1));
  viewer?.querySelector('[data-pbd-viewer-next]')?.addEventListener('click', () => showViewerSlide(viewerIndex + 1));
  viewer?.querySelector('[data-pbd-viewer-first]')?.addEventListener('click', () => { showViewerSlide(0); focusSlide(); });
  notesToggle?.addEventListener('click', () => { toggleViewerPanel('notes'); focusSlide(); });
  stripToggle?.addEventListener('click', () => { toggleViewerPanel('strip'); focusSlide(); });

  // Present starts at the slide in view; "From the start" is in the presenter's toolbar.
  presentButton?.addEventListener('click', () => openViewer(current));
  refreshButton?.addEventListener('click', loadPreview);
  prevButton?.addEventListener('click', () => selectIndex(current - 1));
  nextButton?.addEventListener('click', () => selectIndex(current + 1));
  viewButtons.forEach((button) => button.addEventListener('click', () => setView(button.dataset.pbdStageView)));
  tabs.forEach((tab) => tab.addEventListener('click', () => setTab(tab.dataset.pbdInspectorTab)));
  progress?.querySelectorAll('[data-pbd-progress-tab]').forEach((button) => button.addEventListener('click', () => {
    const name = button.dataset.pbdProgressTab;
    if (name === 'deck') showDeckSettings();
    else setTab(name, { focus: true });
    root.querySelector('.pbd-studio__inspector')?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }));
  progress?.querySelector('[data-pbd-progress-generate]')?.addEventListener('click', () => {
    const generate = document.querySelector('[data-pbd-generate]');
    generate?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    generate?.focus();
  });
  tabs.forEach((tab, index) => tab.addEventListener('keydown', (event) => {
    if (event.key !== 'ArrowRight' && event.key !== 'ArrowLeft') return;
    event.preventDefault();
    const next = tabs[(index + (event.key === 'ArrowRight' ? 1 : tabs.length - 1)) % tabs.length];
    setTab(next.dataset.pbdInspectorTab, { focus: true });
  }));
  filterClear?.addEventListener('click', () => setFilter(''));
  let searchTimer = 0;
  searchInput?.addEventListener('input', () => {
    window.clearTimeout(searchTimer);
    searchTimer = window.setTimeout(() => {
      query = searchInput.value.trim().toLowerCase();
      renderOutline();
    }, 120);
  });

  // Arrow keys move between slides while the Studio has focus, except inside form fields.
  // Left and right arrows move between slides from anywhere on the page (after clicking the
  // slide, focus is on the page itself), except in fields, tabs, menus, dialogs and drawers.
  document.addEventListener('keydown', (event) => {
    if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
    if (!slides.length || view !== 'slide') return;
    if (document.querySelector('.modal.show, dialog[open]')) return;
    const target = event.target;
    if (target instanceof Element && target.closest(
      'input, textarea, select, [contenteditable="true"], [role="tablist"], [role="menu"], .dropdown-menu, .modal, dialog, [class*="drawer"]')) return;
    event.preventDefault();
    selectIndex(current + (event.key === 'ArrowRight' ? 1 : -1));
  });

  // Speaker notes the server refused: keep the draft and say why.
  document.addEventListener('pbd:project-notes-failed', (event) => {
    const projectId = Number(event.detail?.projectId);
    const draft = notesDrafts.get(projectId);
    if (!draft) return;
    notesDrafts.set(projectId, { ...draft, saving: false, error: event.detail?.message || 'The speaker notes could not be saved.' });
    markUnsavedNotes();
    refreshInspector();
  });

  // A photo choice the server refused: say why and let the person choose again.
  document.addEventListener('pbd:project-photo-failed', (event) => {
    if (photoPicker.projectId !== Number(event.detail?.projectId)) return;
    photoPicker = { ...photoPicker, saving: false, error: event.detail?.message || 'The photograph could not be saved.' };
    refreshInspector();
  });

  // A presentation change the server refused: put back the saved choice before the rebuild.
  document.addEventListener('pbd:project-presentation-failed', (event) => {
    const project = projects.get(Number(event.detail?.projectId));
    if (!project || !event.detail?.previous) return;
    project.presentation = event.detail.previous;
    project.isPresented = project.presentation !== 'LeftOut' && !project.isDeletedOrArchived;
    if (!slides.length) entries = buildEntries([]);
    renderOutline();
    renderStage();
    renderChecksPanel();
  });

  document.addEventListener('pbd:settings-saved', (event) => {
    const changed = Array.isArray(event.detail?.changed) ? event.detail.changed : [];
    for (const name of [...changed].reverse()) {
      const roles = SETTING_SLIDES[name]?.();
      if (roles) {
        followRoles = roles;
        return;
      }
    }
  });

  // The builder announces in-place edits (membership, order, overrides); rebuild shortly after.
  document.addEventListener('pbd:deck-changed', (event) => {
    if (typeof event.detail?.hasProjects === 'boolean') hasProjects = event.detail.hasProjects;
    if (event.detail?.deck) {
      if (photoPicker.saving) photoPicker = { ...photoPicker, saving: false };
      // Saved notes come back with the deck; a draft that now matches them is done.
      const savedNotes = new Map((event.detail.deck.projects || []).map((project) => [project.projectId, project.speakerNotes || '']));
      notesDrafts.forEach((draft, projectId) => {
        if (!draft.saving) return;
        if ((savedNotes.get(projectId) || '').trim() === draft.text.trim()) notesDrafts.delete(projectId);
        else notesDrafts.set(projectId, { ...draft, saving: false });
      });
      if (event.detail.deck.layout) deckShape.layout = event.detail.deck.layout;
      if (event.detail.deck.presentationMode) deckShape.mode = event.detail.deck.presentationMode;
      readStudioData({ projects: event.detail.deck.projects, contentGaps: event.detail.deck.contentGaps });
      markUnsavedNotes();
      renderOutline();
      renderStage();
      renderChecksPanel();
    }
    root.classList.add('is-stale');
    if (status) status.textContent = 'Deck changed. Updating the slides…';
    window.clearTimeout(refreshTimer);
    refreshTimer = window.setTimeout(loadPreview, 900);
  });

  // A fix made in the project's own tab ("Fix in project") shows here when the person comes back:
  // a quiet rebuild once the page is in view again, at most every 15 seconds, never while a build
  // runs, while presenting or while typing in the Studio.
  const rebuildOnReturn = () => {
    if (document.visibilityState !== 'visible') return;
    if (controller || viewer?.open) return;
    if (Date.now() - lastBuiltAt < 15000) return;
    if (document.activeElement?.matches?.('input, textarea, select, [contenteditable="true"]')) return;
    loadPreview();
  };
  document.addEventListener('visibilitychange', rebuildOnReturn);
  window.addEventListener('focus', rebuildOnReturn);

  // The Checks filter comes back with the place (shown once the outline is built).
  if (savedPlace.filter && GAPS[savedPlace.filter]) {
    filterGap = savedPlace.filter;
    if (filterBox) filterBox.hidden = false;
    if (filterText) filterText.textContent = `Projects with a gap in ${GAPS[filterGap].label.toLowerCase()}`;
  }

  // All slides is not brought back on narrow screens, where the grid runs to thousands of pixels.
  setView(savedPlace.view === 'grid' && !narrowScreen() ? 'grid' : 'slide');
  // A new, empty deck opens on the Deck tab, where its template is chosen.
  const firstTab = root.dataset.pbdOpenSettings === 'true' ? 'deck' : 'slide';
  setTab(['slide', 'deck', 'checks'].includes(savedPlace.tab) ? savedPlace.tab : firstTab);
  renderChecksPanel();
  loadPreview();
}

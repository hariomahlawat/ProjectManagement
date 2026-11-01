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
  const checksBox = root.querySelector('[data-pbd-stage-checks]');
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
    RoleCharter: { section: 'opening', label: 'Role & Charter', editor: '[data-pbd-role-charter-open]', about: 'The role statements and charter items kept for this deck.' },
    PortfolioSummary: { section: 'portfolio', label: 'Portfolio summary', about: 'Selected, completed and ongoing projects, with the recorded costs.' },
    StageSummary: { section: 'portfolio', label: 'Stage summary', about: 'Ongoing projects by present stage, with the leading stage called out.' },
    StageDistributionTable: { section: 'portfolio', label: 'Stage table', about: 'The stage-wise counts as an editable table.' },
    CategorySummary: { section: 'portfolio', label: 'Category summary', fold: true, about: 'Projects by category. Left out when the deck holds a single category.' },
    StatusTable: { section: 'portfolio', label: 'Status table', fold: true, about: 'Every project with its cost, present stage and latest status remark, in deck order.' },
    ProjectCapability: { section: 'project', label: 'Capability overview' },
    ProjectCapabilityContinuation: { section: 'project', label: 'Capability overview, continued' },
    ProjectBrief: { section: 'project', label: 'Project brief' },
    ProjectUpdateSheet: { section: 'project', label: 'Update sheet' },
    ProjectUpdateSheetContinuation: { section: 'project', label: 'Update sheet, continued' },
    FfcGlobalFootprint: { section: 'closing', label: 'FFC footprint', editor: '[data-pbd-ffc-footprint-open]', about: 'Countries supplied with PRISM projects, from live FFC records.' },
    FfcCountryBreakdown: { section: 'closing', label: 'FFC country breakdown', fold: true, editor: '[data-pbd-ffc-footprint-open]', about: 'Country-wise detail behind the FFC footprint.' },
    Closing: { section: 'closing', label: 'Closing', about: 'The closing slide. Choose Jai Hind or Thank You in the deck settings.' }
  };
  const SECTIONS = [
    { key: 'opening', label: 'Opening' },
    { key: 'portfolio', label: 'Portfolio' },
    { key: 'project', label: 'Projects' },
    { key: 'closing', label: 'Closing' }
  ];
  const GAPS = {
    status: { label: 'Status remark', missing: 'No external status remark yet', fix: 'Add in project', href: (p) => p.openUrl },
    costRd: { label: 'Cost (R&D)', missing: 'Not recorded', fix: 'Record in project', href: (p) => p.openUrl },
    proliferationCost: { label: 'Proliferation cost', missing: 'Not recorded', fix: 'Record in project', href: (p) => p.openUrl },
    photo: { label: 'Photograph', missing: 'No usable cover photo', fix: 'Choose in Photos', href: (p) => `/Projects/${p.projectId}/Photos` },
    capabilityOverview: { label: 'Capability overview', missing: 'Not recorded', fix: 'Write for this deck', describe: true },
    projectBrief: { label: 'Project brief', missing: 'Not recorded', fix: 'Write in project', href: (p) => `${p.openUrl}?content=brief#content-brief` },
    updateSheetFacts: { label: 'Update-sheet facts', missing: 'Some selected rows are empty', fix: 'Open project', href: (p) => p.openUrl }
  };

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
  let hasProjects = root.dataset.hasProjects === 'true';
  let viewerIndex = 0;

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
    return result;
  };

  const entryTitle = (entry) => {
    if (entry.projectId) return projects.get(entry.projectId)?.projectName || slides[entry.indices[0]]?.title || 'Project';
    const title = (slides[entry.indices[0]]?.title || ROLES[entry.role]?.label || '').replace(/\s*\(\d+\/\d+\)$/, '');
    return entry.indices.length > 1 ? `${title} · ${plural(entry.indices.length, 'slide')}` : title;
  };

  const entryForIndex = (index) => entries.find((entry) => entry.indices.includes(index));

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

  const renderOutline = () => {
    if (!outlineNav) return;
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
          const number = slides[entry.indices[0]]?.number ?? entry.indices[0] + 1;
          const title = entryTitle(entry);
          row.title = title;
          row.append(el('span', 'pbd-outline__number', String(number)), el('span', 'pbd-outline__name', title));
          const missing = entry.projectId ? gapsFor(entry.projectId) : [];
          if (missing.length) {
            const badge = el('span', 'pbd-outline__gaps', String(missing.length));
            badge.title = `Missing: ${missing.map((key) => GAPS[key]?.label.toLowerCase() || key).join(', ')}`;
            badge.setAttribute('aria-label', `${plural(missing.length, 'item')} missing`);
            row.append(badge);
          }
          row.addEventListener('click', () => selectIndex(entry.indices[0], { from: 'outline' }));
          item.append(row);
          list.append(item);
        });
        wrapper.append(list);
      }
      nodes.push(wrapper);
    });
    outlineNav.replaceChildren(...nodes);
    markOutlineSelection();
  };

  const markOutlineSelection = () => {
    const entry = entryForIndex(current);
    outlineNav?.querySelectorAll('[data-pbd-outline-key]').forEach((row) => {
      const selected = entry && row.dataset.pbdOutlineKey === entry.key;
      row.classList.toggle('is-selected', Boolean(selected));
      if (selected) row.setAttribute('aria-current', 'true');
      else row.removeAttribute('aria-current');
    });
  };

  const settingsLauncher = () => page.querySelector('[data-pbd-settings-open]');

  const actionButton = (label, onClick, className = 'btn btn-sm btn-outline-secondary') => {
    const button = el('button', className, label);
    button.type = 'button';
    button.addEventListener('click', onClick);
    return button;
  };
  const actionLink = (label, href, className = 'btn btn-sm btn-outline-secondary') => {
    const link = el('a', className, label);
    link.href = href;
    link.target = '_blank';
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

  const renderChecks = (entry) => {
    if (!checksBox) return;
    const project = entry?.projectId ? projects.get(entry.projectId) : null;
    if (!project || view !== 'slide') {
      checksBox.replaceChildren();
      checksBox.hidden = true;
      return;
    }
    const missing = gapsFor(project.projectId);
    checksBox.hidden = false;
    if (missing.length === 0) {
      const done = el('p', 'pbd-stage__check is-ready');
      done.append(icon('bi-check-circle'), el('span', null, 'Everything this deck shows for the project is recorded.'));
      checksBox.replaceChildren(done);
      return;
    }
    checksBox.replaceChildren(...missing.map((key) => {
      const gap = GAPS[key] || { label: key, missing: 'Missing', fix: 'Open project', href: (p) => p.openUrl };
      const card = el('div', 'pbd-stage__check');
      const text = el('span', 'pbd-stage__check-text');
      text.append(el('strong', null, gap.label), el('small', null, key === 'photo' && project.coverPhotoReadinessReason ? project.coverPhotoReadinessReason : gap.missing));
      card.append(icon('bi-exclamation-circle'), text);
      if (gap.describe && usesCapability) card.append(describeButton(project, gap.fix));
      else if (gap.href) card.append(actionLink(gap.fix, gap.href(project)));
      return card;
    }));
  };

  const renderSlideInspector = (entry) => {
    const panel = panels.slide;
    if (!panel) return;
    const slide = slides[current];
    if (!slide || !entry) {
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
      const facts = el('dl', 'pbd-inspector__facts');
      const fact = (label, value, note) => {
        const row = el('div');
        const dd = el('dd', null, value);
        if (note) dd.append(el('small', null, note));
        row.append(el('dt', null, label), dd);
        return row;
      };
      facts.append(fact('Cost (R&D)', project.costRd?.displayValue || 'Not recorded', project.costRd?.basisDisplay));
      facts.append(fact('Proliferation', project.proliferationCost?.displayValue || 'Not recorded'));
      facts.append(fact('Slides', plural(entry.indices.length, 'slide'), entry.indices.map((index) => slides[index]?.number).join(', ')));
      nodes.push(facts);

      nodes.push(el('h5', 'pbd-inspector__heading', 'Present status'));
      nodes.push(project.externalStatus
        ? el('blockquote', 'pbd-inspector__quote', project.externalStatus)
        : el('p', 'pbd-inspector__note', 'No external status remark recorded. The slides show the placeholder.'));

      if (usesCapability) {
        nodes.push(el('h5', 'pbd-inspector__heading', 'Capability overview'));
        nodes.push(el('p', 'pbd-inspector__note', project.briefDescriptionOverride
          ? 'Written for this deck. The project’s own statements are unchanged.'
          : (project.capabilityStatements?.length ? `From the project: ${plural(project.capabilityStatements.length, 'statement')}.` : 'Not recorded in the project.')));
        nodes.push(describeButton(project, project.briefDescriptionOverride ? 'Edit the text for this deck' : 'Write for this deck'));
      }

      const actions = el('div', 'pbd-inspector__actions');
      actions.append(actionLink('Open project', project.openUrl));
      const remove = el('button', 'btn btn-sm btn-outline-danger', 'Remove from deck');
      remove.type = 'button';
      remove.dataset.pbdRemoveProject = '';
      remove.dataset.projectId = String(project.projectId);
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
      actions.append(actionButton('Deck settings', () => settingsLauncher()?.click()));
      nodes.push(actions);
    }
    panel.replaceChildren(...nodes);
  };

  const renderChecksPanel = () => {
    const panel = panels.checks;
    if (!panel) return;
    const presented = [...projects.values()].filter((project) => !project.isDeletedOrArchived);
    const total = presented.length;
    const counts = {};
    let projectsWithGaps = 0;
    presented.forEach((project) => {
      const missing = gapsFor(project.projectId);
      if (missing.length) projectsWithGaps += 1;
      missing.forEach((key) => { counts[key] = (counts[key] || 0) + 1; });
    });
    if (checksBadge) {
      checksBadge.hidden = projectsWithGaps === 0;
      checksBadge.textContent = String(projectsWithGaps);
    }
    if (total === 0) {
      panel.replaceChildren(el('p', 'pbd-inspector__empty', 'Add projects to check the deck.'));
      return;
    }
    const keys = Object.keys(GAPS).filter((key) => counts[key]);
    if (keys.length === 0) {
      const done = el('p', 'pbd-stage__check is-ready');
      done.append(icon('bi-check-circle'), el('span', null, 'Everything this deck shows is recorded for every project.'));
      panel.replaceChildren(done);
      return;
    }
    const list = el('ul', 'pbd-inspector__checks');
    keys.forEach((key) => {
      const item = el('li');
      const text = el('span');
      text.append(el('strong', null, GAPS[key].label), el('small', null, `${counts[key]} of ${plural(total, 'project')} missing`));
      const show = actionButton(filterGap === key ? 'Showing' : 'Show', () => setFilter(filterGap === key ? '' : key));
      show.setAttribute('aria-pressed', String(filterGap === key));
      item.append(text, show);
      list.append(item);
    });
    panel.replaceChildren(
      el('p', 'pbd-inspector__note', `${plural(projectsWithGaps, 'project')} of ${total} lack something the deck shows. Placeholders fill the gaps.`),
      list,
      el('p', 'pbd-inspector__note', 'Show narrows the outline to those projects; fix each one from This slide.'));
  };

  const setFilter = (key) => {
    filterGap = key;
    if (filterBox) filterBox.hidden = !key;
    if (filterText) filterText.textContent = key ? `Projects missing ${GAPS[key]?.label.toLowerCase() || key}` : '';
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
    if (!slide) {
      if (stageMeta) stageMeta.textContent = 'Slide preview';
      releaseFrames(slideBox);
      slideBox?.replaceChildren();
      renderChecks(null);
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
    renderChecks(entry);
    renderSlideInspector(entry);
    gridBox?.querySelectorAll('.pbd-stage__thumb').forEach((thumb, index) => thumb.classList.toggle('is-selected', index === current));
  };

  const selectIndex = (index, { from } = {}) => {
    if (!slides.length) return;
    current = Math.max(0, Math.min(index, slides.length - 1));
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
  };

  const updateCounts = (payload) => {
    const label = plural(payload.slideCount, 'slide');
    if (countLabel) countLabel.textContent = String(payload.slideCount);
    if (slideTotal) slideTotal.textContent = label;
    if (slideBreakdown) slideBreakdown.textContent = 'Exact count from the slide preview.';
    if (status) status.textContent = `${label}, built from current PRISM data at ${payload.generatedAt}. Generate PowerPoint downloads exactly these slides.`;
  };

  const renderEmpty = (message) => {
    slides = [];
    entries = [];
    releaseFrames(gridBox);
    gridBox?.replaceChildren();
    outlineNav?.replaceChildren(el('p', 'pbd-outline__empty', message));
    if (presentButton) presentButton.disabled = true;
    if (status) status.textContent = message;
    renderStage();
    renderChecksPanel();
    // renderStage clears the slide area when there are no slides; say why after it.
    slideBox?.replaceChildren(el('p', 'pbd-stage__empty', message));
  };

  const loadPreview = async () => {
    // Cancel a running build so an obsolete response cannot restore old slides.
    controller?.abort();
    controller = null;
    if (!hasProjects) {
      if (viewer?.open) viewer.close();
      root.classList.remove('is-busy', 'is-stale', 'is-error');
      if (refreshButton) refreshButton.disabled = false;
      renderEmpty('Add projects to build the slides.');
      return;
    }

    const request = new AbortController();
    controller = request;
    const keepEntry = entryForIndex(current);
    const keepProject = keepEntry?.projectId;
    const keepNumber = slides[current]?.number;
    root.classList.add('is-busy');
    if (status) status.textContent = slides.length ? 'Updating the slides…' : 'Building the slides from current PRISM data…';
    if (refreshButton) refreshButton.disabled = true;
    try {
      const response = await fetch(root.dataset.previewUrl, {
        credentials: 'same-origin',
        headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
        signal: request.signal
      });
      const payload = await response.json().catch(() => null);
      // A newer request (or an emptied deck) superseded this one: drop its response.
      if (request.signal.aborted) return;
      if (!response.ok || !payload) throw new Error(payload?.message || `The slides could not be built (${response.status}).`);
      slides = Array.isArray(payload.slides) ? payload.slides : [];
      images = payload.images || {};
      entries = buildEntries(Array.isArray(payload.outline) ? payload.outline : []);
      // Keep the same project (or slide number) selected across rebuilds.
      const sameProject = keepProject ? entries.find((entry) => entry.projectId === keepProject) : null;
      current = sameProject
        ? sameProject.indices[0]
        : Math.min(Math.max(0, (keepNumber || 1) - 1), Math.max(0, slides.length - 1));
      updateCounts(payload);
      renderOutline();
      if (view === 'grid') renderGrid();
      renderStage();
      renderChecksPanel();
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

  // Present: the full-window viewer with a strip of every slide.
  const showViewerSlide = (index) => {
    if (!viewerStage || slides.length === 0) return;
    viewerIndex = Math.max(0, Math.min(index, slides.length - 1));
    const slide = slides[viewerIndex];
    releaseFrames(viewerStage);
    viewerStage.replaceChildren(buildFrame(slide));
    if (viewerTitle) viewerTitle.textContent = slide.title;
    if (viewerCounter) viewerCounter.textContent = `${slide.number} / ${slides.length}`;
    viewerStrip?.querySelectorAll('button').forEach((button, buttonIndex) => {
      const active = buttonIndex === viewerIndex;
      button.classList.toggle('is-active', active);
      if (active) {
        button.setAttribute('aria-current', 'true');
        button.scrollIntoView({ block: 'nearest', inline: 'nearest' });
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

  const openViewer = (index) => {
    if (!viewer || slides.length === 0) return;
    buildStrip();
    if (!viewer.open) viewer.showModal();
    showViewerSlide(index);
  };

  viewer?.addEventListener('keydown', (event) => {
    if (event.key === 'ArrowRight' || event.key === 'PageDown') { event.preventDefault(); showViewerSlide(viewerIndex + 1); }
    else if (event.key === 'ArrowLeft' || event.key === 'PageUp') { event.preventDefault(); showViewerSlide(viewerIndex - 1); }
    else if (event.key === 'Home') { event.preventDefault(); showViewerSlide(0); }
    else if (event.key === 'End') { event.preventDefault(); showViewerSlide(slides.length - 1); }
  });
  viewer?.addEventListener('click', (event) => { if (event.target === viewer) viewer.close(); });
  viewer?.addEventListener('close', () => {
    [viewerStage, viewerStrip].forEach(releaseFrames);
    viewerStage?.replaceChildren();
    viewerStrip?.replaceChildren();
    // Return to the slide the presenter ended on.
    if (slides.length) selectIndex(viewerIndex);
  });
  viewer?.querySelector('[data-pbd-viewer-close]')?.addEventListener('click', () => viewer.close());
  viewer?.querySelector('[data-pbd-viewer-prev]')?.addEventListener('click', () => showViewerSlide(viewerIndex - 1));
  viewer?.querySelector('[data-pbd-viewer-next]')?.addEventListener('click', () => showViewerSlide(viewerIndex + 1));

  presentButton?.addEventListener('click', () => openViewer(current));
  refreshButton?.addEventListener('click', loadPreview);
  prevButton?.addEventListener('click', () => selectIndex(current - 1));
  nextButton?.addEventListener('click', () => selectIndex(current + 1));
  viewButtons.forEach((button) => button.addEventListener('click', () => setView(button.dataset.pbdStageView)));
  tabs.forEach((tab) => tab.addEventListener('click', () => setTab(tab.dataset.pbdInspectorTab)));
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
  root.addEventListener('keydown', (event) => {
    if (event.defaultPrevented || event.altKey || event.ctrlKey || event.metaKey) return;
    if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return;
    const target = event.target;
    if (target instanceof HTMLElement && (target.closest('input, textarea, select, [contenteditable="true"], [role="tablist"]'))) return;
    event.preventDefault();
    selectIndex(current + (event.key === 'ArrowRight' ? 1 : -1));
  });

  // The builder announces in-place edits (membership, order, overrides); rebuild shortly after.
  document.addEventListener('pbd:deck-changed', (event) => {
    if (typeof event.detail?.hasProjects === 'boolean') hasProjects = event.detail.hasProjects;
    if (event.detail?.deck) {
      readStudioData({ projects: event.detail.deck.projects, contentGaps: event.detail.deck.contentGaps });
      renderOutline();
      renderStage();
      renderChecksPanel();
    }
    root.classList.add('is-stale');
    if (status) status.textContent = 'Deck changed. Updating the slides…';
    window.clearTimeout(refreshTimer);
    refreshTimer = window.setTimeout(loadPreview, 900);
  });

  setView('slide');
  setTab('slide');
  renderChecksPanel();
  loadPreview();
}

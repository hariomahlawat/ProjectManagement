// Slide preview for the briefing deck builder. The server composes the same PowerPoint that
// Generate downloads and returns each slide as encoded, positioned HTML; this module lays the
// slides out as scaled thumbnails and a full-screen viewer, and rebuilds them after edits.
const root = document.querySelector('[data-pbd-preview]');

if (root) {
  const grid = root.querySelector('[data-pbd-preview-grid]');
  const countHeading = root.querySelector('[data-pbd-preview-count]');
  const status = root.querySelector('[data-pbd-preview-status]');
  const refreshButton = root.querySelector('[data-pbd-preview-refresh]');
  const openButton = root.querySelector('[data-pbd-preview-open]');
  const footer = root.querySelector('[data-pbd-preview-footer]');
  const expandButton = root.querySelector('[data-pbd-preview-expand]');
  const viewer = document.querySelector('[data-pbd-viewer]');
  const viewerStage = viewer?.querySelector('[data-pbd-viewer-stage]');
  const viewerStrip = viewer?.querySelector('[data-pbd-viewer-strip]');
  const viewerTitle = viewer?.querySelector('[data-pbd-viewer-title]');
  const viewerCounter = viewer?.querySelector('[data-pbd-viewer-counter]');
  const slideTotal = document.querySelector('[data-pbd-slide-total]');
  const slideBreakdown = document.querySelector('[data-pbd-slide-breakdown]');
  const defaultStatus = status?.textContent || '';
  const collapsedRows = 2;

  let slides = [];
  let images = {};
  let current = 0;
  let expanded = false;
  let controller = null;
  let refreshTimer = 0;
  let hasProjects = root.dataset.hasProjects === 'true';

  const plural = (count, word) => `${count} ${word}${count === 1 ? '' : 's'}`;

  // A rendered stage is a fixed 1280 × 720 canvas; each frame scales it to its own width.
  const scaleObserver = new ResizeObserver((entries) => {
    entries.forEach((entry) => scaleFrame(entry.target));
  });

  const scaleFrame = (frame) => {
    const stage = frame.querySelector('.pbd-slide-stage');
    if (!stage) return;
    const designWidth = parseFloat(stage.style.width) || 1280;
    const width = frame.clientWidth;
    if (width > 0) stage.style.transform = `scale(${width / designWidth})`;
  };

  const buildFrame = (slide) => {
    const frame = document.createElement('div');
    frame.className = 'pbd-slide-frame';
    // Slide HTML is produced server-side from the composed deck with all text encoded.
    frame.innerHTML = slide.html;
    frame.querySelectorAll('img[data-pbd-preview-image]').forEach((image) => {
      const source = images[image.dataset.pbdPreviewImage];
      if (source) image.src = source;
    });
    scaleObserver.observe(frame);
    return frame;
  };

  const columnCount = () => {
    if (!grid) return 1;
    const columns = getComputedStyle(grid).gridTemplateColumns.split(' ').filter(Boolean);
    return Math.max(1, columns.length);
  };

  const applyCollapse = () => {
    if (!grid) return;
    const limit = expanded ? Infinity : columnCount() * collapsedRows;
    [...grid.children].forEach((item, index) => { item.hidden = index >= limit; });
    const hiddenCount = Math.max(0, grid.children.length - limit);
    if (footer) footer.hidden = slides.length === 0 || (!expanded && hiddenCount === 0);
    if (expandButton) {
      expandButton.textContent = expanded ? 'Show fewer slides' : `Show all ${plural(slides.length, 'slide')}`;
      expandButton.setAttribute('aria-expanded', String(expanded));
    }
  };

  const renderSkeleton = () => {
    if (!grid) return;
    const estimated = Math.min(Math.max(Number(root.dataset.estimatedSlides || 0), 4), columnCount() * collapsedRows);
    grid.replaceChildren(...Array.from({ length: estimated }, () => {
      const item = document.createElement('li');
      item.className = 'pbd-preview__item is-loading';
      item.setAttribute('aria-hidden', 'true');
      const frame = document.createElement('div');
      frame.className = 'pbd-slide-frame';
      const caption = document.createElement('span');
      caption.className = 'pbd-preview__caption';
      item.append(frame, caption);
      return item;
    }));
  };

  const renderEmpty = (message) => {
    slides = [];
    grid?.replaceChildren();
    const item = document.createElement('li');
    item.className = 'pbd-preview__empty';
    item.textContent = message;
    grid?.append(item);
    if (countHeading) countHeading.textContent = 'Slides';
    if (openButton) openButton.disabled = true;
    applyCollapse();
  };

  const renderSlides = () => {
    if (!grid) return;
    grid.querySelectorAll('.pbd-slide-frame').forEach((frame) => scaleObserver.unobserve(frame));
    grid.replaceChildren(...slides.map((slide, index) => {
      const item = document.createElement('li');
      item.className = 'pbd-preview__item';
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'pbd-preview__thumb';
      button.setAttribute('aria-label', `Open slide ${slide.number}: ${slide.title}`);
      button.addEventListener('click', () => openViewer(index));
      const caption = document.createElement('span');
      caption.className = 'pbd-preview__caption';
      const number = document.createElement('b');
      number.textContent = String(slide.number);
      const title = document.createElement('span');
      title.textContent = slide.title;
      caption.append(number, title);
      button.append(buildFrame(slide), caption);
      item.append(button);
      return item;
    }));
    applyCollapse();
  };

  const updateCounts = (payload) => {
    const label = plural(payload.slideCount, 'slide');
    if (countHeading) countHeading.textContent = label;
    if (slideTotal) slideTotal.textContent = label;
    if (slideBreakdown) slideBreakdown.textContent = 'Exact count from the slide preview.';
    if (status) status.textContent = `Built from current PRISM data at ${payload.generatedAt}. This is exactly what Generate PowerPoint will download.`;
  };

  const loadPreview = async () => {
    if (!hasProjects) {
      renderEmpty('Add projects to see the slides.');
      return;
    }

    controller?.abort();
    controller = new AbortController();
    root.classList.add('is-busy');
    if (slides.length === 0) renderSkeleton();
    if (status) status.textContent = 'Building slides from current PRISM data…';
    if (refreshButton) refreshButton.disabled = true;
    try {
      const response = await fetch(root.dataset.previewUrl, {
        credentials: 'same-origin',
        headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
        signal: controller.signal
      });
      const payload = await response.json().catch(() => null);
      if (!response.ok || !payload) throw new Error(payload?.message || `The slide preview could not be built (${response.status}).`);
      slides = Array.isArray(payload.slides) ? payload.slides : [];
      images = payload.images || {};
      renderSlides();
      updateCounts(payload);
      if (openButton) openButton.disabled = slides.length === 0;
      if (viewer?.open) showSlide(Math.min(current, slides.length - 1));
    } catch (error) {
      if (error.name === 'AbortError') return;
      if (slides.length === 0) renderEmpty('The slide preview is not available.');
      if (status) status.textContent = error.message || 'The slide preview could not be built.';
      root.classList.add('is-error');
      return;
    } finally {
      if (refreshButton) refreshButton.disabled = false;
      root.classList.remove('is-busy');
    }
    root.classList.remove('is-error', 'is-stale');
  };

  // Viewer: one large slide, a strip of every slide, arrow keys to move.
  const showSlide = (index) => {
    if (!viewerStage || slides.length === 0) return;
    current = Math.max(0, Math.min(index, slides.length - 1));
    const slide = slides[current];
    viewerStage.querySelectorAll('.pbd-slide-frame').forEach((frame) => scaleObserver.unobserve(frame));
    viewerStage.replaceChildren(buildFrame(slide));
    if (viewerTitle) viewerTitle.textContent = slide.title;
    if (viewerCounter) viewerCounter.textContent = `${slide.number} / ${slides.length}`;
    viewerStrip?.querySelectorAll('button').forEach((button, buttonIndex) => {
      const active = buttonIndex === current;
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
    viewerStrip.querySelectorAll('.pbd-slide-frame').forEach((frame) => scaleObserver.unobserve(frame));
    viewerStrip.replaceChildren(...slides.map((slide, index) => {
      const item = document.createElement('li');
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'pbd-viewer__thumb';
      button.setAttribute('aria-label', `Slide ${slide.number}: ${slide.title}`);
      button.addEventListener('click', () => showSlide(index));
      const number = document.createElement('span');
      number.textContent = String(slide.number);
      button.append(number, buildFrame(slide));
      item.append(button);
      return item;
    }));
  };

  const openViewer = (index = 0) => {
    if (!viewer || slides.length === 0) return;
    buildStrip();
    if (!viewer.open) viewer.showModal();
    showSlide(index);
  };

  viewer?.addEventListener('keydown', (event) => {
    if (event.key === 'ArrowRight' || event.key === 'PageDown') { event.preventDefault(); showSlide(current + 1); }
    else if (event.key === 'ArrowLeft' || event.key === 'PageUp') { event.preventDefault(); showSlide(current - 1); }
    else if (event.key === 'Home') { event.preventDefault(); showSlide(0); }
    else if (event.key === 'End') { event.preventDefault(); showSlide(slides.length - 1); }
  });
  viewer?.addEventListener('click', (event) => {
    if (event.target === viewer) viewer.close();
  });
  viewer?.addEventListener('close', () => {
    [viewerStage, viewerStrip].forEach((container) =>
      container?.querySelectorAll('.pbd-slide-frame').forEach((frame) => scaleObserver.unobserve(frame)));
    viewerStage?.replaceChildren();
    viewerStrip?.replaceChildren();
  });
  viewer?.querySelector('[data-pbd-viewer-close]')?.addEventListener('click', () => viewer.close());
  viewer?.querySelector('[data-pbd-viewer-prev]')?.addEventListener('click', () => showSlide(current - 1));
  viewer?.querySelector('[data-pbd-viewer-next]')?.addEventListener('click', () => showSlide(current + 1));

  openButton?.addEventListener('click', () => openViewer(0));
  refreshButton?.addEventListener('click', loadPreview);
  expandButton?.addEventListener('click', () => {
    expanded = !expanded;
    applyCollapse();
  });
  new ResizeObserver(() => applyCollapse()).observe(grid);

  // The builder announces in-place edits (membership, order, overrides); rebuild shortly after.
  document.addEventListener('pbd:deck-changed', (event) => {
    if (typeof event.detail?.hasProjects === 'boolean') hasProjects = event.detail.hasProjects;
    root.classList.add('is-stale');
    if (status) status.textContent = 'Deck changed. Updating the slides…';
    window.clearTimeout(refreshTimer);
    refreshTimer = window.setTimeout(loadPreview, 900);
  });

  if (status) status.textContent = defaultStatus;
  loadPreview();
}

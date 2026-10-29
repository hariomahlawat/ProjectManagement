// PRISM PDF Tools — browser application shell and tools.
//
// Files are read with the File API, processed by pdf-lib (PrismPdfTools) and rendered by
// pdf.js. Nothing is sent to the server. pdf.js is an ES module and is imported on demand.
(() => {
  'use strict';

  const root = document.querySelector('[data-pdf-tools]');
  const core = window.PrismPdfTools;
  const PDFLib = window.PDFLib;
  if (!root || !core || !PDFLib) {
    return;
  }

  const LARGE_FILE_BYTES = 200 * 1024 * 1024;
  const MAX_RENDER_PIXELS = 24 * 1000 * 1000;
  const THUMBNAIL_SIZE = 128;

  // ---------------------------------------------------------------------------
  // Shared helpers
  // ---------------------------------------------------------------------------

  const $ = (selector, scope = document) => scope.querySelector(selector);
  const $$ = (selector, scope = document) => Array.from(scope.querySelectorAll(selector));

  const busyLayer = $('[data-busy]');
  const busyText = $('[data-busy-text]');
  const errorBox = $('[data-error]');

  function setBusy(message) {
    if (message) {
      busyText.textContent = message;
      busyLayer.hidden = false;
    } else {
      busyLayer.hidden = true;
    }
  }

  function showError(error) {
    const message = error instanceof core.PdfToolError
      ? error.message
      : 'Something went wrong while processing the file. Please check the file and try again.';
    if (!(error instanceof core.PdfToolError)) {
      console.error(error);
    }
    errorBox.textContent = message;
    errorBox.hidden = false;
    errorBox.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }

  function clearError() {
    errorBox.hidden = true;
    errorBox.textContent = '';
  }

  /** Runs an async task behind the busy overlay and reports failures to the user. */
  async function withBusy(message, task) {
    clearError();
    setBusy(message);
    try {
      return await task((text) => setBusy(text));
    } catch (error) {
      showError(error);
      return undefined;
    } finally {
      setBusy(null);
    }
  }

  async function readFile(file) {
    return new Uint8Array(await file.arrayBuffer());
  }

  function formatBytes(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  function isPdf(file) {
    return file.type === 'application/pdf' || /\.pdf$/i.test(file.name);
  }

  function confirmLarge(files) {
    const total = files.reduce((sum, file) => sum + file.size, 0);
    return total < LARGE_FILE_BYTES ||
      window.confirm(`These files total ${formatBytes(total)}. Large files may make this browser slow. Continue?`);
  }

  function saveBlob(blob, fileName) {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.rel = 'noopener';
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  }

  function canvasToBytes(canvas, type, quality) {
    return new Promise((resolve, reject) => {
      canvas.toBlob(async (blob) => {
        if (!blob) {
          reject(new core.PdfToolError('render', 'The page could not be turned into an image. Try a lower quality.'));
          return;
        }
        resolve(new Uint8Array(await blob.arrayBuffer()));
      }, type, quality);
    });
  }

  // ---------------------------------------------------------------------------
  // pdf.js (rendering only)
  // ---------------------------------------------------------------------------

  let pdfjsPromise = null;

  function loadPdfjs() {
    if (!pdfjsPromise) {
      pdfjsPromise = import(root.dataset.pdfjsSrc).then((pdfjs) => {
        pdfjs.GlobalWorkerOptions.workerSrc = root.dataset.pdfjsWorkerSrc;
        return pdfjs;
      });
      pdfjsPromise.catch(() => {
        pdfjsPromise = null;
      });
    }
    return pdfjsPromise;
  }

  /** Opens a PDF for rendering. pdf.js takes ownership of the buffer, so pass a copy. */
  async function openForRender(bytes) {
    const pdfjs = await loadPdfjs();
    try {
      return await pdfjs.getDocument({
        data: bytes.slice(),
        // Local character maps, so fonts that use predefined CMaps (common in CJK PDFs) render offline.
        cMapUrl: root.dataset.pdfjsCmapUrl,
        cMapPacked: true,
        isEvalSupported: false,
        enableXfa: false,
        disableAutoFetch: true
      }).promise;
    } catch (error) {
      if (error && error.name === 'PasswordException') {
        throw new core.PdfToolError('encrypted', 'This PDF is password-protected, so it cannot be opened here.');
      }
      throw new core.PdfToolError('unreadable', 'This file could not be read as a PDF. It may be damaged or not a PDF.');
    }
  }

  /** Renders a pdf.js page into a canvas at the given CSS width (or scale). */
  async function renderPage(page, canvas, { width, scale, maxPixels = MAX_RENDER_PIXELS, background = '#ffffff' } = {}) {
    const base = page.getViewport({ scale: 1 });
    const cssScale = scale || (width / base.width);
    const ratio = scale ? 1 : Math.min(window.devicePixelRatio || 1, 2);
    let pixelScale = cssScale * ratio;
    const pixels = base.width * base.height * pixelScale * pixelScale;
    if (pixels > maxPixels) {
      // Lower only the backing-store resolution: the displayed size must stay as requested
      // so the annotate overlay, sized from the same width, stays aligned with the page.
      pixelScale *= Math.sqrt(maxPixels / pixels);
    }
    const viewport = page.getViewport({ scale: pixelScale });
    canvas.width = Math.max(1, Math.floor(viewport.width));
    canvas.height = Math.max(1, Math.floor(viewport.height));
    if (!scale) {
      canvas.style.width = `${base.width * cssScale}px`;
      canvas.style.height = `${base.height * cssScale}px`;
    }
    const context = canvas.getContext('2d');
    context.fillStyle = background;
    context.fillRect(0, 0, canvas.width, canvas.height);
    await page.render({ canvasContext: context, viewport }).promise;
    return { viewport, scale: cssScale };
  }

  /** Renders thumbnails one at a time as they scroll into view, to bound memory use. */
  function createThumbnailQueue() {
    const pending = [];
    let running = false;
    const observer = 'IntersectionObserver' in window
      ? new IntersectionObserver((entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            observer.unobserve(entry.target);
            enqueue(entry.target.__thumbnailJob);
          }
        });
      }, { rootMargin: '400px' })
      : null;

    async function pump() {
      if (running) return;
      running = true;
      while (pending.length) {
        const job = pending.shift();
        try {
          await job();
        } catch (error) {
          console.warn('PDF Tools: thumbnail failed', error);
        }
      }
      running = false;
    }

    function enqueue(job) {
      if (job) {
        pending.push(job);
        pump();
      }
    }

    return {
      observe(element, job) {
        if (observer) {
          element.__thumbnailJob = job;
          observer.observe(element);
        } else {
          enqueue(job);
        }
      },
      reset() {
        pending.length = 0;
        observer?.disconnect();
      }
    };
  }

  // ---------------------------------------------------------------------------
  // Tool scaffolding
  // ---------------------------------------------------------------------------

  /** Reads data-option fields from a tool section into a plain object. */
  function readOptions(section) {
    const options = {};
    $$('[data-option]', section).forEach((field) => {
      options[field.dataset.option] = field.type === 'checkbox' ? field.checked : field.value;
    });
    const mode = $('input[type="radio"]:checked', section);
    if (mode) {
      options.mode = mode.value;
    }
    return options;
  }

  function createTool(section, { onFiles, onRun, onReset, accept }) {
    const drop = $('[data-drop]', section);
    const input = $('[data-file-input]', section);
    const workspace = $('[data-workspace]', section);
    const result = $('[data-result]', section);
    const resultName = $('[data-result-name]', result);
    const resultPreview = $('[data-result-preview]', result);
    let output = null;

    const state = {
      section,
      workspace,
      showWorkspace() {
        drop.hidden = true;
        result.hidden = true;
        workspace.hidden = false;
      },
      reset() {
        output = null;
        input.value = '';
        drop.hidden = false;
        workspace.hidden = true;
        result.hidden = true;
        clearError();
        onReset?.();
      },
      /** Shows the finished file and downloads it. */
      async finish(bytesOrBlob, fileName, mime) {
        const blob = bytesOrBlob instanceof Blob ? bytesOrBlob : new Blob([bytesOrBlob], { type: mime });
        output = { blob, fileName };
        workspace.hidden = true;
        result.hidden = false;
        resultName.textContent = `${fileName} · ${formatBytes(blob.size)}`;
        resultPreview.hidden = true;
        saveBlob(blob, fileName);
        if (mime === 'application/pdf') {
          try {
            const rendered = await openForRender(bytesOrBlob instanceof Blob ? new Uint8Array(await blob.arrayBuffer()) : bytesOrBlob);
            await renderPage(await rendered.getPage(1), resultPreview, { width: 220 });
            resultPreview.hidden = false;
            rendered.destroy();
          } catch {
            // A preview is a convenience only.
          }
        }
        result.scrollIntoView({ block: 'start', behavior: 'smooth' });
      },
      addFiles() {
        input.click();
      }
    };

    async function accept_(files) {
      const list = Array.from(files || []).filter(accept);
      if (list.length === 0) {
        showError(new core.PdfToolError('file-type', 'Those files are not supported by this tool.'));
        return;
      }
      if (!input.multiple) {
        list.length = 1;
      }
      if (!confirmLarge(list)) {
        return;
      }
      await onFiles(list, state);
    }

    $$('[data-action="choose-files"]', section).forEach((button) => button.addEventListener('click', () => input.click()));
    input.addEventListener('change', () => {
      const files = Array.from(input.files || []);
      input.value = '';
      accept_(files);
    });

    drop.addEventListener('dragover', (event) => {
      event.preventDefault();
      drop.classList.add('is-dragging');
    });
    drop.addEventListener('dragleave', () => drop.classList.remove('is-dragging'));
    drop.addEventListener('drop', (event) => {
      event.preventDefault();
      drop.classList.remove('is-dragging');
      accept_(event.dataTransfer?.files);
    });
    // Allow dropping more files straight onto the workspace for multi-file tools.
    if (input.multiple) {
      workspace.addEventListener('dragover', (event) => event.preventDefault());
      workspace.addEventListener('drop', (event) => {
        if (event.dataTransfer?.files?.length) {
          event.preventDefault();
          accept_(event.dataTransfer.files);
        }
      });
    }

    $$('[data-action="add-files"]', section).forEach((button) => button.addEventListener('click', () => input.click()));
    $$('[data-action="run"]', section).forEach((button) => button.addEventListener('click', () => onRun(readOptions(section), state)));
    $('[data-action="download"]', result).addEventListener('click', () => output && saveBlob(output.blob, output.fileName));
    $('[data-action="back-to-edit"]', result).addEventListener('click', () => state.showWorkspace());
    $('[data-action="start-over"]', result).addEventListener('click', () => state.reset());

    return state;
  }

  function fileSummary(section, file, pageCount) {
    const target = $('[data-file-summary]', section);
    target.replaceChildren();
    const icon = document.createElement('i');
    icon.className = 'bi bi-file-earmark-pdf';
    icon.setAttribute('aria-hidden', 'true');
    const name = document.createElement('strong');
    name.textContent = file.name;
    const meta = document.createElement('span');
    meta.textContent = `${pageCount} page${pageCount === 1 ? '' : 's'} · ${formatBytes(file.size)}`;
    const change = document.createElement('button');
    change.type = 'button';
    change.className = 'btn btn-link btn-sm';
    change.textContent = 'Choose another file';
    change.addEventListener('click', () => $('[data-file-input]', section).click());
    target.append(icon, name, meta, change);
  }

  /** A single-PDF tool: load one file, validate it with pdf-lib, then run. */
  function singlePdfTool(name, run) {
    const section = $(`[data-tool="${name}"]`);
    if (!section) return null;
    let current = null;
    return createTool(section, {
      accept: isPdf,
      async onFiles([file], tool) {
        await withBusy('Opening PDF…', async () => {
          const bytes = await readFile(file);
          const pages = await core.pageCount(PDFLib, bytes);
          current = { file, bytes, pages };
          fileSummary(section, file, pages);
          tool.showWorkspace();
        });
      },
      onRun(options, tool) {
        if (!current) return;
        withBusy('Working…', (progress) => run(current, options, tool, progress));
      },
      onReset() {
        current = null;
      }
    });
  }

  // ---------------------------------------------------------------------------
  // Organise & merge
  // ---------------------------------------------------------------------------

  (function organiseTool() {
    const section = $('[data-tool="organise"]');
    if (!section) return;
    const grid = $('[data-pages]', section);
    const summary = $('[data-summary]', section);
    const thumbnails = createThumbnailQueue();
    let sources = [];
    let pages = [];
    let nextId = 1;

    function updateSummary() {
      const selected = pages.filter((page) => page.selected).length;
      summary.textContent = `${pages.length} page${pages.length === 1 ? '' : 's'} from ${sources.filter(Boolean).length} file${sources.length === 1 ? '' : 's'}` +
        (selected ? ` · ${selected} selected` : '');
    }

    // Pages are labelled "Page n" for one file and "File k · pn" once several are merged.
    function relabel() {
      const many = sources.length > 1;
      const byId = new Map(pages.map((page) => [String(page.id), page]));
      $$('.pdft-page', grid).forEach((card) => {
        const page = byId.get(card.dataset.pageId);
        if (page) {
          $('.pdft-page__label', card).textContent = many
            ? `${sources[page.source].short} · p${page.index + 1}`
            : `Page ${page.index + 1}`;
        }
      });
    }

    function applyRotation(card, page) {
      $('.pdft-page__thumb canvas', card).style.transform = `rotate(${page.rotate}deg)`;
    }

    function buildCard(page) {
      const source = sources[page.source];
      const card = document.createElement('div');
      card.className = 'pdft-page';
      card.dataset.pageId = String(page.id);
      card.dataset.sourceColour = String(page.source % 6);

      const thumb = document.createElement('div');
      thumb.className = 'pdft-page__thumb';
      const canvas = document.createElement('canvas');
      canvas.setAttribute('aria-hidden', 'true');
      thumb.append(canvas);

      const select = document.createElement('input');
      select.type = 'checkbox';
      select.className = 'form-check-input pdft-page__select';
      select.setAttribute('aria-label', `Select page ${page.index + 1} of ${source.name}`);
      select.addEventListener('change', () => {
        page.selected = select.checked;
        card.classList.toggle('is-selected', page.selected);
        updateSummary();
      });

      const label = document.createElement('div');
      label.className = 'pdft-page__label';
      label.title = `${source.name}, page ${page.index + 1}`;

      const actions = document.createElement('div');
      actions.className = 'pdft-page__actions';
      const button = (icon, text, handler) => {
        const element = document.createElement('button');
        element.type = 'button';
        element.className = 'btn btn-light btn-sm';
        element.title = text;
        element.setAttribute('aria-label', `${text}: page ${page.index + 1} of ${source.name}`);
        element.innerHTML = `<i class="bi ${icon}" aria-hidden="true"></i>`;
        element.addEventListener('click', handler);
        return element;
      };
      actions.append(
        button('bi-arrow-counterclockwise', 'Rotate left', () => { page.rotate = (page.rotate + 270) % 360; applyRotation(card, page); }),
        button('bi-arrow-clockwise', 'Rotate right', () => { page.rotate = (page.rotate + 90) % 360; applyRotation(card, page); }),
        button('bi-trash3', 'Delete', () => {
          pages = pages.filter((item) => item !== page);
          card.remove();
          updateSummary();
          if (pages.length === 0) {
            tool.reset();
          }
        })
      );

      card.append(thumb, select, label, actions);
      thumbnails.observe(card, async () => {
        const pdfPage = await source.render.getPage(page.index + 1);
        // Fit the longest side so the thumbnail still fits its square frame when rotated.
        const base = pdfPage.getViewport({ scale: 1 });
        await renderPage(pdfPage, canvas, { width: (THUMBNAIL_SIZE * base.width) / Math.max(base.width, base.height) });
        pdfPage.cleanup();
      });
      return card;
    }

    function syncOrderFromDom() {
      const byId = new Map(pages.map((page) => [String(page.id), page]));
      pages = $$('.pdft-page', grid).map((card) => byId.get(card.dataset.pageId)).filter(Boolean);
    }

    if (window.Sortable) {
      window.Sortable.create(grid, {
        animation: 150,
        draggable: '.pdft-page',
        filter: 'button, input',
        preventOnFilter: false,
        onEnd: syncOrderFromDom
      });
    }

    const tool = createTool(section, {
      accept: isPdf,
      async onFiles(files, state) {
        await withBusy('Opening PDFs…', async (progress) => {
          for (const [position, file] of files.entries()) {
            progress(`Opening ${file.name} (${position + 1} of ${files.length})…`);
            const bytes = await readFile(file);
            const count = await core.pageCount(PDFLib, bytes);
            const render = await openForRender(bytes);
            const sourceIndex = sources.length;
            sources.push({ name: file.name, short: `File ${sourceIndex + 1}`, bytes, render });
            for (let index = 0; index < count; index += 1) {
              const page = { id: nextId++, source: sourceIndex, index, rotate: 0, selected: false };
              pages.push(page);
              grid.append(buildCard(page));
            }
          }
          relabel();
          updateSummary();
          state.showWorkspace();
        });
      },
      onRun() {
        save(pages, sources.length > 1 ? 'merged' : `${core.baseName(sources[0]?.name)}-organised`);
      },
      onReset() {
        thumbnails.reset();
        sources.forEach((source) => source?.render?.destroy());
        sources = [];
        pages = [];
        grid.replaceChildren();
      }
    });

    function save(selection, name) {
      withBusy('Building PDF…', async () => {
        const bytes = await core.assemblePages(PDFLib, sources.map((source) => source.bytes), selection.map((page) => ({
          source: page.source,
          index: page.index,
          rotate: page.rotate
        })));
        await tool.finish(bytes, `${name}.pdf`, 'application/pdf');
      });
    }

    $('[data-action="extract"]', section).addEventListener('click', () => {
      const selected = pages.filter((page) => page.selected);
      if (selected.length === 0) {
        showError(new core.PdfToolError('no-selection', 'Tick the pages you want to extract first.'));
        return;
      }
      save(selected, `${core.baseName(sources[0]?.name)}-extract`);
    });

    $('[data-action="select-all"]', section).addEventListener('click', () => {
      const select = pages.some((page) => !page.selected);
      pages.forEach((page) => { page.selected = select; });
      $$('.pdft-page', grid).forEach((card) => {
        $('.pdft-page__select', card).checked = select;
        card.classList.toggle('is-selected', select);
      });
      updateSummary();
    });

    $('[data-action="rotate-selected"]', section).addEventListener('click', () => {
      const targets = pages.some((page) => page.selected) ? pages.filter((page) => page.selected) : pages;
      const cards = new Map($$('.pdft-page', grid).map((card) => [card.dataset.pageId, card]));
      targets.forEach((page) => {
        page.rotate = (page.rotate + 90) % 360;
        applyRotation(cards.get(String(page.id)), page);
      });
    });

    $('[data-action="delete-selected"]', section).addEventListener('click', () => {
      const selected = pages.filter((page) => page.selected);
      if (selected.length === 0) {
        showError(new core.PdfToolError('no-selection', 'Tick the pages you want to delete first.'));
        return;
      }
      clearError();
      const ids = new Set(selected.map((page) => String(page.id)));
      pages = pages.filter((page) => !page.selected);
      $$('.pdft-page', grid).forEach((card) => ids.has(card.dataset.pageId) && card.remove());
      updateSummary();
      if (pages.length === 0) {
        tool.reset();
      }
    });
  })();

  // ---------------------------------------------------------------------------
  // Split
  // ---------------------------------------------------------------------------

  singlePdfTool('split', async (current, options, tool) => {
    let groups;
    if (options.mode === 'every') {
      groups = core.chunkPages(current.pages, 1);
    } else if (options.mode === 'chunk') {
      groups = core.chunkPages(current.pages, options.chunk);
    } else {
      groups = core.parsePageRanges(options.ranges, current.pages);
      if (options.combine) {
        groups = [groups.flat()];
      }
    }

    const base = core.baseName(current.file.name);
    const parts = await core.splitPdf(PDFLib, current.bytes, groups);
    if (parts.length === 1) {
      await tool.finish(parts[0], `${base}-pages.pdf`, 'application/pdf');
      return;
    }
    const label = (group) => (group.length === 1 ? `${group[0] + 1}` : `${group[0] + 1}-${group[group.length - 1] + 1}`);
    const used = new Set();
    const entries = parts.map((data, index) => {
      let name = `${base}-pages-${label(groups[index])}.pdf`;
      if (used.has(name)) name = `${base}-part-${index + 1}.pdf`;
      used.add(name);
      return { name, data };
    });
    await tool.finish(core.createZip(entries), `${base}-split.zip`, 'application/zip');
  });

  // ---------------------------------------------------------------------------
  // Images to PDF
  // ---------------------------------------------------------------------------

  (function imagesTool() {
    const section = $('[data-tool="images"]');
    if (!section) return;
    const grid = $('[data-pages]', section);
    const summary = $('[data-summary]', section);
    let images = [];
    let nextId = 1;

    const updateSummary = () => {
      summary.textContent = `${images.length} image${images.length === 1 ? '' : 's'}`;
    };

    if (window.Sortable) {
      window.Sortable.create(grid, {
        animation: 150,
        draggable: '.pdft-page',
        filter: 'button',
        preventOnFilter: false,
        onEnd() {
          const byId = new Map(images.map((image) => [String(image.id), image]));
          images = $$('.pdft-page', grid).map((card) => byId.get(card.dataset.pageId)).filter(Boolean);
        }
      });
    }

    const tool = createTool(section, {
      accept: (file) => file.type.startsWith('image/') || /\.(jpe?g|png|gif|webp|bmp)$/i.test(file.name),
      async onFiles(files, state) {
        for (const file of files) {
          const image = { id: nextId++, file, url: URL.createObjectURL(file) };
          images.push(image);

          const card = document.createElement('div');
          card.className = 'pdft-page';
          card.dataset.pageId = String(image.id);
          const thumb = document.createElement('div');
          thumb.className = 'pdft-page__thumb';
          const img = document.createElement('img');
          img.src = image.url;
          img.alt = '';
          img.decoding = 'async';
          thumb.append(img);
          const label = document.createElement('div');
          label.className = 'pdft-page__label';
          label.textContent = file.name;
          label.title = file.name;
          const actions = document.createElement('div');
          actions.className = 'pdft-page__actions';
          const remove = document.createElement('button');
          remove.type = 'button';
          remove.className = 'btn btn-light btn-sm';
          remove.title = 'Remove';
          remove.setAttribute('aria-label', `Remove ${file.name}`);
          remove.innerHTML = '<i class="bi bi-trash3" aria-hidden="true"></i>';
          remove.addEventListener('click', () => {
            URL.revokeObjectURL(image.url);
            images = images.filter((item) => item !== image);
            card.remove();
            updateSummary();
            if (images.length === 0) tool.reset();
          });
          actions.append(remove);
          card.append(thumb, label, actions);
          grid.append(card);
        }
        updateSummary();
        state.showWorkspace();
      },
      onRun(options, state) {
        withBusy('Creating PDF…', async (progress) => {
          const prepared = [];
          for (const [position, image] of images.entries()) {
            progress(`Adding image ${position + 1} of ${images.length}…`);
            prepared.push(await prepareImage(image.file));
          }
          const bytes = await core.imagesToPdf(PDFLib, prepared, {
            pageSize: options.pageSize,
            orientation: options.orientation,
            margin: Number(options.margin)
          });
          const name = images.length === 1 ? core.baseName(images[0].file.name, 'images') : 'images';
          await state.finish(bytes, `${name}.pdf`, 'application/pdf');
        });
      },
      onReset() {
        images.forEach((image) => URL.revokeObjectURL(image.url));
        images = [];
        grid.replaceChildren();
      }
    });

    /** JPG and PNG are embedded as-is; other formats are re-encoded to PNG by the browser. */
    async function prepareImage(file) {
      const bytes = await readFile(file);
      const isJpeg = bytes[0] === 0xff && bytes[1] === 0xd8;
      const isPng = bytes[0] === 0x89 && bytes[1] === 0x50 && bytes[2] === 0x4e && bytes[3] === 0x47;
      if (isJpeg) return { bytes, type: 'jpg' };
      if (isPng) return { bytes, type: 'png' };
      try {
        const bitmap = await createImageBitmap(file);
        const canvas = document.createElement('canvas');
        canvas.width = bitmap.width;
        canvas.height = bitmap.height;
        canvas.getContext('2d').drawImage(bitmap, 0, 0);
        bitmap.close?.();
        return { bytes: await canvasToBytes(canvas, 'image/png'), type: 'png' };
      } catch {
        throw new core.PdfToolError('image-unsupported', `${file.name} could not be read as an image.`);
      }
    }
  })();

  // ---------------------------------------------------------------------------
  // PDF to images
  // ---------------------------------------------------------------------------

  singlePdfTool('to-images', async (current, options, tool, progress) => {
    const indexes = String(options.pages || '').trim()
      ? core.pageSetFromRanges(options.pages, current.pages)
      : Array.from({ length: current.pages }, (_, index) => index);
    const format = options.format === 'jpg' ? 'jpg' : 'png';
    const mime = format === 'jpg' ? 'image/jpeg' : 'image/png';
    const dpi = Number(options.dpi) || 150;
    const base = core.baseName(current.file.name);

    const rendered = await openForRender(current.bytes);
    const canvas = document.createElement('canvas');
    const entries = [];
    try {
      for (const [position, index] of indexes.entries()) {
        progress(`Rendering page ${index + 1} (${position + 1} of ${indexes.length})…`);
        const page = await rendered.getPage(index + 1);
        await renderPage(page, canvas, { scale: dpi / 72 });
        page.cleanup();
        entries.push({ name: `${base}-page-${index + 1}.${format}`, data: await canvasToBytes(canvas, mime, 0.92) });
      }
    } finally {
      rendered.destroy();
      canvas.width = 0;
      canvas.height = 0;
    }

    if (entries.length === 1) {
      await tool.finish(entries[0].data, entries[0].name, mime);
    } else {
      await tool.finish(core.createZip(entries), `${base}-images.zip`, 'application/zip');
    }
  });

  // ---------------------------------------------------------------------------
  // Watermark
  // ---------------------------------------------------------------------------

  singlePdfTool('watermark', async (current, options, tool) => {
    const bytes = await core.addWatermark(PDFLib, current.bytes, {
      text: options.text,
      fontSize: Number(options.fontSize),
      opacity: Number(options.opacity),
      color: options.color,
      angle: Number(options.angle),
      position: options.position,
      pages: options.pages
    });
    await tool.finish(bytes, `${core.baseName(current.file.name)}-watermarked.pdf`, 'application/pdf');
  });

  // ---------------------------------------------------------------------------
  // Page numbers & header/footer
  // ---------------------------------------------------------------------------

  (function numbersTool() {
    const section = $('[data-tool="numbers"]');
    if (!section) return;
    const format = $('[data-option="format"]', section);
    const custom = $('[data-show-when="format=custom"]', section);
    format.addEventListener('change', () => {
      custom.hidden = format.value !== 'custom';
    });
  })();

  singlePdfTool('numbers', async (current, options, tool) => {
    const numberFormat = options.format === 'custom' ? options.customFormat : options.format;
    const stamps = [
      { template: numberFormat, position: options.position },
      { template: options.header, position: options.headerPosition },
      { template: options.footer, position: options.footerPosition }
    ];
    const bytes = await core.addTextStamps(PDFLib, current.bytes, {
      stamps,
      fontSize: Number(options.fontSize),
      margin: Number(options.margin),
      color: options.color,
      startAt: Number(options.startAt),
      skipFirst: options.skipFirst,
      pages: options.pages,
      date: new Date().toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })
    });
    await tool.finish(bytes, `${core.baseName(current.file.name)}-numbered.pdf`, 'application/pdf');
  });

  // ---------------------------------------------------------------------------
  // Shared API for the annotate tool (pdf-tools-annotate.js)
  // ---------------------------------------------------------------------------

  window.PrismPdfToolsUi = Object.freeze({
    $,
    $$,
    createTool,
    withBusy,
    showError,
    clearError,
    readFile,
    isPdf,
    openForRender,
    renderPage,
    canvasToBytes,
    createThumbnailQueue
  });

  // ---------------------------------------------------------------------------
  // Routing (#tool in the URL so the browser Back button works)
  // ---------------------------------------------------------------------------

  const views = $$('[data-view]');

  function route() {
    const name = (window.location.hash || '').replace(/^#/, '') || 'home';
    const target = views.find((view) => view.dataset.view === name) || views.find((view) => view.dataset.view === 'home');
    views.forEach((view) => { view.hidden = view !== target; });
    clearError();
    const heading = $('h1', target);
    document.title = target.dataset.view === 'home' ? 'PDF Tools | PRISM ERP' : `${heading?.textContent?.trim()} | PDF Tools`;
    window.scrollTo({ top: 0 });
  }

  $$('[data-nav-home]').forEach((link) => link.addEventListener('click', (event) => {
    event.preventDefault();
    if (window.location.hash) {
      window.location.hash = '';
    }
    route();
  }));
  window.addEventListener('hashchange', route);
  route();

  // Stop a stray drop outside a drop zone from navigating away and losing work.
  window.addEventListener('dragover', (event) => event.preventDefault());
  window.addEventListener('drop', (event) => event.preventDefault());
})();

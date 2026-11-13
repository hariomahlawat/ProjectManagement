// PRISM PDF Tools — Reduce size.
//
// The rules (what to change, how far, what to leave, how to reach a size) are in
// pdf-tools-core.js (`reducePdf`) and tested in Node; re-saving one picture is in
// pdf-tools-encode.js. This file is the screen: presets and "Keep under", running the work in a
// background worker (on the page when the browser cannot), Stop, the result and a page before and
// after. Nothing leaves the browser. Plan: docs/ux/pdf-tools-reduce-size.md.
(() => {
  'use strict';

  const ui = window.PrismPdfToolsUi;
  const core = window.PrismPdfTools;
  const encoder = window.PrismPdfEncode;
  const PDFLib = window.PDFLib;
  const section = document.querySelector('[data-tool="compress"]');
  if (!ui || !core || !encoder || !PDFLib || !section || !core.reducePdf) {
    return;
  }

  const { $, $$ } = ui;
  const about = $('[data-compress-about]', section);
  const outcome = $('[data-compress-outcome]', section);
  const presetsBox = $('[data-compress-presets]', section);
  const targetInput = $('[data-compress-target]', section);
  const targetHint = $('[data-compress-target-hint]', section);
  const unitButtons = $$('[data-compress-unit]', section);
  const quickButtons = $$('[data-compress-quick]', section);
  const resultBox = $('[data-result]', section);
  // Below this share of the file, pictures are not what makes it large.
  const MOSTLY_TEXT_SHARE = 0.25;
  const STORAGE_KEY = 'prism.pdfTools.reduce';
  const TARGET_HINT = targetHint.textContent;

  // The presets come from the core, so the screen and the rules cannot disagree.
  for (const preset of core.PDF_SIZE_PRESETS) {
    const label = document.createElement('label');
    label.className = 'pdft-preset';
    const input = document.createElement('input');
    input.type = 'radio';
    input.name = 'compressPreset';
    input.value = preset.key;
    input.className = 'pdft-preset__input';
    input.checked = preset.key === 'standard';
    const title = document.createElement('span');
    title.className = 'pdft-preset__title';
    title.textContent = preset.label;
    const note = document.createElement('span');
    note.className = 'pdft-preset__note';
    note.textContent = `${preset.note} · pictures at ${preset.dpi} dpi`;
    label.append(input, title, note);
    presetsBox.append(label);
  }

  let current = null;

  function say(element, text) {
    element.textContent = text || '';
    element.hidden = !text;
  }

  /** What the summary says before any work, so a text-only file does not promise a saving. */
  function describe(info) {
    const share = info.bytes ? info.imageBytes / info.bytes : 0;
    if (!info.imageCount || share < MOSTLY_TEXT_SHARE) {
      return 'This PDF is mostly text, so it may not get much smaller. Repeated fonts and unused parts can still be removed.';
    }
    return `Pictures are ${ui.formatBytes(info.imageBytes)} of this ${ui.formatBytes(info.bytes)} file (${info.imageCount} picture${info.imageCount === 1 ? '' : 's'}).`;
  }

  // ---------------------------------------------------------------------------
  // Keep under (remembered in this browser only)
  // ---------------------------------------------------------------------------

  let unit = 'MB';

  function remember() {
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify({ target: targetInput.value.trim(), unit }));
    } catch {
      // A private window may refuse storage; nothing depends on it.
    }
  }

  function restore() {
    try {
      const saved = JSON.parse(window.localStorage.getItem(STORAGE_KEY) || 'null');
      if (saved && typeof saved.target === 'string') targetInput.value = saved.target;
      if (saved && (saved.unit === 'KB' || saved.unit === 'MB')) unit = saved.unit;
    } catch {
      // The page's defaults are used instead.
    }
  }

  /** The size to keep under: null for none, NaN for something that is not a size. */
  function readTarget() {
    const text = targetInput.value.trim();
    if (!text) return null;
    return core.parseSizeLimit(text, unit) || NaN;
  }

  function targetText() {
    return `${targetInput.value.trim()} ${unit}`;
  }

  function syncTarget() {
    for (const button of unitButtons) button.setAttribute('aria-pressed', String(button.dataset.compressUnit === unit));
    const value = `${targetInput.value.trim()}:${unit}`;
    for (const button of quickButtons) {
      const quick = button.dataset.compressQuick;
      button.setAttribute('aria-pressed', String(quick ? quick === value : !targetInput.value.trim()));
    }
    const bad = Number.isNaN(readTarget());
    targetInput.classList.toggle('is-invalid', bad);
    targetInput.setAttribute('aria-invalid', String(bad));
    targetHint.classList.toggle('text-danger', bad);
    targetHint.textContent = bad ? 'Enter the size as a number, for example 2 or 1.5, or leave it empty.' : TARGET_HINT;
  }

  targetInput.addEventListener('input', () => { syncTarget(); remember(); });
  for (const button of unitButtons) {
    button.addEventListener('click', () => {
      unit = button.dataset.compressUnit;
      syncTarget();
      remember();
    });
  }
  for (const button of quickButtons) {
    button.addEventListener('click', () => {
      const [value, quickUnit] = (button.dataset.compressQuick || ':').split(':');
      targetInput.value = value || '';
      if (quickUnit) unit = quickUnit;
      syncTarget();
      remember();
    });
  }
  restore();
  syncTarget();

  // ---------------------------------------------------------------------------
  // Running the work: in a background worker where the browser can, on the page otherwise
  // ---------------------------------------------------------------------------

  const workerSource = document.body.dataset.compressWorkerSrc;

  /** The addresses of the scripts this page loaded, versioned as loaded, for the worker. */
  function workerScripts() {
    const find = (part) => Array.from(document.scripts).find((script) => script.src.includes(part))?.src;
    const scripts = ['/pdf-lib.min.js', '/tools-shared/zip-writer.js', '/pdf-tools/pdf-tools-core.js', '/pdf-tools/pdf-tools-encode.js'].map(find);
    return scripts.every(Boolean) ? scripts : null;
  }

  const canUseWorker = () => Boolean(workerSource && typeof Worker === 'function'
    && typeof OffscreenCanvas === 'function' && typeof createImageBitmap === 'function' && workerScripts());

  const stopped = () => new core.PdfToolError('cancelled', 'Stopped. No file was made.');

  /** On the page: the same work, stopped between pictures. */
  function runOnPage(bytes, options, onProgress) {
    const controller = new AbortController();
    return {
      // Once Stop is pressed, no result is used, however late the work ends.
      promise: core.reducePdf(PDFLib, bytes, { ...options, encodeImage: encoder.encodeImage, onProgress, signal: controller.signal })
        .then((result) => { if (controller.signal.aborted) throw stopped(); return result; }),
      stop: () => controller.abort()
    };
  }

  /** In a worker: Stop ends it at once. If the worker cannot start, the page does the work. */
  function runInWorker(bytes, options, onProgress) {
    let finish;
    let fail;
    let worker = null;
    let started = false;
    let fallback = null;
    const promise = new Promise((resolve, reject) => { finish = resolve; fail = reject; });
    const useThePage = () => {
      fallback = runOnPage(bytes, options, onProgress);
      fallback.promise.then(finish, fail);
    };
    try {
      worker = new Worker(workerSource);
    } catch {
      useThePage();
      return { promise, stop: () => fallback?.stop() };
    }
    // A worker that dies without a word (the browser ends it when it runs out of memory) would
    // leave "Working…" up. The worker says it is alive every few seconds; after a long silence the
    // work ends and the screen says so.
    let watchdog = 0;
    const SILENCE_MS = 90000;
    const listen = () => {
      clearTimeout(watchdog);
      watchdog = setTimeout(() => {
        end();
        fail(new core.PdfToolError('worker-silent', 'The work stopped without finishing; the file may be too large for this computer. No file was made.'));
      }, SILENCE_MS);
    };
    const end = () => { clearTimeout(watchdog); worker?.terminate(); worker = null; };
    worker.onmessage = (event) => {
      listen();
      const message = event.data || {};
      if (message.type === 'alive') return;
      if (message.type === 'progress') {
        started = true;
        onProgress(message);
      } else if (message.type === 'done') {
        end();
        finish(message.result);
      } else if (message.type === 'error') {
        end();
        // The worker could not load what it needs: the page does the work instead.
        if (!message.loaded && !started) {
          useThePage();
          return;
        }
        fail(message.known ? new core.PdfToolError(message.code, message.message) : new Error(message.message));
      }
    };
    worker.onerror = (event) => {
      event.preventDefault?.();
      end();
      if (!started) useThePage();
      else fail(new Error('The background work stopped.'));
    };
    // The page keeps its own copy, for the before-and-after preview.
    const copy = bytes.slice();
    worker.postMessage({ type: 'reduce', scripts: workerScripts(), bytes: copy, ...options }, [copy.buffer]);
    listen();
    return {
      promise,
      stop() {
        if (fallback) {
          fallback.stop();
          return;
        }
        end();
        fail(stopped());
      }
    };
  }

  // ---------------------------------------------------------------------------
  // What the result says
  // ---------------------------------------------------------------------------

  const SKIP_WORDS = {
    [core.REDUCE_SKIP.printColour]: ['print-colour picture', 'print-colour pictures'],
    [core.REDUCE_SKIP.blackWhite]: ['black-and-white scan', 'black-and-white scans'],
    [core.REDUCE_SKIP.mask]: ['transparency mask', 'transparency masks'],
    [core.REDUCE_SKIP.small]: ['small picture', 'small pictures'],
    [core.REDUCE_SKIP.other]: ['picture in a format this tool does not change', 'pictures in a format this tool does not change'],
    [core.REDUCE_SKIP.notSmaller]: ['picture already as small as it can be made', 'pictures already as small as they can be made']
  };

  const plural = (count, [one, many]) => `${count} ${count === 1 ? one : many}`;

  function resultDetail(before, after, report) {
    const saved = before - after;
    const parts = [`${ui.formatBytes(before)} → ${ui.formatBytes(after)}, ${Math.round((saved / before) * 100)}% smaller.`];
    if (report.target && report.target.reached) {
      parts.push(`Under ${limitWords}, with pictures at ${report.target.dpi} dpi and quality ${Math.round(report.target.quality * 100)}%.`);
    }
    const done = [];
    if (report.resaved) done.push(`${plural(report.resaved, ['picture', 'pictures'])} re-saved${report.resized ? ` (${report.resized} at ${report.dpi} dpi)` : ''}`);
    if (report.merged) done.push(`${plural(report.merged, ['repeated item', 'repeated items'])} stored once`);
    if (report.compressed) done.push(`${plural(report.compressed, ['part', 'parts'])} compressed`);
    if (report.thumbnails) done.push(`${plural(report.thumbnails, ['page thumbnail', 'page thumbnails'])} left out`);
    if (done.length) parts.push(`${done.join(', ')}.`);
    const left = Object.entries(report.skipped)
      .filter(([reason, count]) => count && SKIP_WORDS[reason])
      .map(([reason, count]) => plural(count, SKIP_WORDS[reason]));
    if (left.length) parts.push(`Left as they were: ${left.join(', ')}.`);
    return parts.join(' ');
  }

  function resultWarning(after, report) {
    const warnings = [];
    if (report.target && !report.target.reached) {
      const kept = report.target.keptSize
        ? ` ${plural(report.target.keptSize, ['picture keeps its own size', 'pictures keep their own size'])}, because where ${report.target.keptSize === 1 ? 'it is' : 'they are'} shown could not be read.`
        : '';
      warnings.push(report.target.atLowest
        ? `This is the smallest this tool can make it: ${ui.formatBytes(after)}, which is over ${limitWords}. Pictures are at ${report.target.dpi} dpi, the lowest it goes.${kept} Splitting the PDF into parts may help.`
        : `This could not be made smaller than ${ui.formatBytes(after)}, which is over ${limitWords}.${kept} Splitting the PDF into parts may help.`);
    }
    if (report.pdfA) {
      warnings.push('This file said it was PDF/A. After the change it may no longer meet PDF/A, so check before sending it where PDF/A is required.');
    }
    return warnings.join(' ');
  }

  let limitWords = '';

  // ---------------------------------------------------------------------------
  // A page before and after
  // ---------------------------------------------------------------------------

  const compare = document.createElement('div');
  compare.className = 'pdft-compare';
  compare.hidden = true;
  compare.innerHTML = `
    <p class="pdft-compare__title" data-compare-title></p>
    <div class="pdft-compare__frame" data-compare-frame>
      <div class="pdft-compare__page">
        <canvas data-compare-before aria-hidden="true"></canvas>
        <canvas class="pdft-compare__after" data-compare-after aria-hidden="true"></canvas>
        <span class="pdft-compare__line" data-compare-line aria-hidden="true"></span>
      </div>
    </div>
    <div class="pdft-compare__labels" aria-hidden="true"><span>Original</span><span>Smaller</span></div>
    <label class="pdft-compare__slider">Move the line to compare
      <input type="range" class="form-range" min="0" max="100" value="50" data-compare-slider />
    </label>
    <div class="form-check">
      <input class="form-check-input" type="checkbox" id="pdftCompareZoom" data-compare-zoom />
      <label class="form-check-label" for="pdftCompareZoom">Zoom in</label>
    </div>`;
  resultBox.insertBefore(compare, $('.pdft-result__actions', resultBox));
  const compareTitle = $('[data-compare-title]', compare);
  const beforeCanvas = $('[data-compare-before]', compare);
  const afterCanvas = $('[data-compare-after]', compare);
  const compareLine = $('[data-compare-line]', compare);
  const compareSlider = $('[data-compare-slider]', compare);
  const compareZoom = $('[data-compare-zoom]', compare);
  let compared = null;
  let compareRun = 0;

  function placeLine() {
    const position = Number(compareSlider.value);
    compareLine.style.left = `${position}%`;
    afterCanvas.style.clipPath = `inset(0 0 0 ${position}%)`;
  }

  function clearCompare() {
    compareRun += 1;
    compared = null;
    compare.hidden = true;
    for (const canvas of [beforeCanvas, afterCanvas]) { canvas.width = 0; canvas.height = 0; }
  }

  /** Draws one page of both files at the same size. Only a convenience: a failure hides it. */
  async function drawCompare() {
    if (!compared) return;
    const mine = ++compareRun;
    const { before, after, page } = compared;
    // The result box's padding and the frame's 1px border each side, so the page fits the frame.
    const width = Math.max(200, Math.min(560, resultBox.clientWidth - 50)) * (compareZoom.checked ? 2 : 1);
    const opened = [];
    try {
      for (const [bytes, canvas] of [[before, beforeCanvas], [after, afterCanvas]]) {
        const rendered = await ui.openForRender(bytes);
        opened.push(rendered);
        if (mine !== compareRun) return;
        await ui.renderPage(await rendered.getPage(page + 1), canvas, { width });
      }
      if (mine !== compareRun) return;
      compareTitle.textContent = `Page ${page + 1}, where the largest picture is, before and after`;
      compare.hidden = false;
      placeLine();
    } catch {
      if (mine === compareRun) compare.hidden = true;
    } finally {
      opened.forEach((rendered) => rendered.destroy());
    }
  }

  compareSlider.addEventListener('input', placeLine);
  compareZoom.addEventListener('change', () => { drawCompare(); });

  // ---------------------------------------------------------------------------
  // The tool
  // ---------------------------------------------------------------------------

  const tool = ui.createTool(section, {
    accept: ui.isPdf,
    async onFiles([file], state) {
      say(outcome, '');
      clearCompare();
      await ui.withBusy('Opening PDF…', async () => {
        try {
          const bytes = await ui.readFile(file);
          const info = await core.describePdf(PDFLib, bytes);
          // Refused before any choice is made: changing a signed PDF breaks its signature.
          if (info.signed) throw new core.PdfToolError('signed', core.SIGNED_PDF_MESSAGE);
          current = { file, bytes, info };
          ui.fileSummary(section, file, info.pages);
          say(about, describe(info));
          state.showWorkspace();
        } catch (error) {
          // A file chosen earlier must not stay on the screen under this one's message, ready to
          // be reduced instead of it.
          state.reset();
          throw error;
        }
      });
    },
    onRun(options, state) {
      if (!current) return;
      say(outcome, '');
      clearCompare();
      const targetBytes = readTarget();
      if (Number.isNaN(targetBytes)) {
        syncTarget();
        targetInput.focus();
        return;
      }
      limitWords = targetBytes ? targetText() : '';
      const { file, bytes } = current;
      let job = null;
      ui.withBusy('Reducing size…', async (progress) => {
        const onProgress = ({ step, done, total }) => {
          progress(step > 1
            ? `Trying smaller settings to fit under ${limitWords} (try ${step}): picture ${done} of ${total}…`
            : `Re-saving picture ${done} of ${total}…`);
        };
        const run = canUseWorker() ? runInWorker : runOnPage;
        job = run(bytes, { preset: options.mode, targetBytes: targetBytes || undefined }, onProgress);
        let result;
        try {
          result = await job.promise;
        } catch (error) {
          if (error instanceof core.PdfToolError && error.code === 'cancelled') {
            say(outcome, error.message);
            ui.moveFocus(outcome);
            return;
          }
          throw error;
        }
        if (result.unchanged) {
          // Never a bigger (or equal) file offered as a saving, and no new file at all.
          say(outcome, 'This PDF is already as small as this tool can make it, so no new file was made.');
          ui.moveFocus(outcome);
          return;
        }
        await state.finish(result.bytes, `${core.baseName(file.name)}-smaller.pdf`, 'application/pdf', {
          detail: resultDetail(bytes.length, result.bytes.length, result.report),
          warning: resultWarning(result.bytes.length, result.report)
        });
        if (Number.isInteger(result.report.previewPage)) {
          compared = { before: bytes, after: result.bytes, page: result.report.previewPage };
          // After the work, not part of it: the page is not kept busy for a convenience.
          setTimeout(drawCompare, 0);
        }
      }, { onStop: () => job?.stop() });
    },
    onReset() {
      current = null;
      say(about, '');
      say(outcome, '');
      clearCompare();
    }
  });
})();

// PRISM PDF Tools — Reduce size, in a background worker.
//
// Runs `reducePdf` off the page, so a large PDF does not freeze the screen, and so Stop can end
// the work at once (the page ends this worker). Nothing leaves the browser: the PDF arrives from
// the page and goes back to it.
//
// The page sends the addresses of the very scripts it loaded (pdf-lib, the ZIP writer, the core
// and the encoder), versioned as it loaded them, so the worker never runs older code than the page.
'use strict';

let loaded = false;

self.onmessage = async (event) => {
  const message = event.data || {};
  if (message.type !== 'reduce') return;
  // Tells the page it is still working, so a worker the browser ended can be told apart.
  const alive = setInterval(() => self.postMessage({ type: 'alive' }), 5000);
  try {
    if (!loaded) {
      self.importScripts(...message.scripts);
      loaded = true;
    }
    const core = self.PrismPdfTools;
    const result = await core.reducePdf(self.PDFLib, message.bytes, {
      preset: message.preset,
      targetBytes: message.targetBytes,
      encodeImage: self.PrismPdfEncode.encodeImage,
      onProgress: (progress) => self.postMessage({ type: 'progress', ...progress })
    });
    self.postMessage(
      { type: 'done', result: { bytes: result.bytes, unchanged: result.unchanged, report: result.report } },
      [result.bytes.buffer]);
  } catch (error) {
    const known = Boolean(error && error.name === 'PdfToolError');
    self.postMessage({
      type: 'error',
      known,
      code: known ? error.code : 'failed',
      message: known ? error.message : 'This PDF could not be made smaller here.',
      loaded
    });
  } finally {
    clearInterval(alive);
  }
};

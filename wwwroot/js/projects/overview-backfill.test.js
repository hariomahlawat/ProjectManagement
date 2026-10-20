const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');
const source = fs.readFileSync(path.join(__dirname, 'overview.js'), 'utf8');

// Markup shaped like _BackfillModal/_BackfillForm output: Razor renders
// data-backfill-carried="" (not omitted) on rows that are not carried forward.
function backfillModal(mode) {
  const dom = new JSDOM(`<div id="backfillModal"><form data-backfill-form novalidate>
    <input type="hidden" value="42" data-backfill-project>
    <input type="hidden" value="3" data-backfill-baseline>
    <input type="hidden" value="${mode}" data-backfill-mode>
    <input type="hidden" value="" data-backfill-based-on>
    <input type="hidden" value="" data-backfill-based-on-epoch>
    <input type="hidden" value="csrf" data-backfill-token>
    <div class="alert alert-danger d-none" data-backfill-errors></div>
    <div data-backfill-stage-list>
      <section data-backfill-row data-stage-code="FS" data-stage-token="tok-fs" data-backfill-carried="">
        <input type="date" data-backfill-start value="">
        <input type="date" data-backfill-completed value="2024-01-25" required>
      </section>
      <section data-backfill-row data-stage-code="IPA" data-stage-token="tok-ipa" data-backfill-carried="true">
        <input type="date" data-backfill-start value="" disabled>
        <input type="date" data-backfill-completed value="2024-01-28" required disabled>
      </section>
    </div>
  </form><button type="button" id="submitBackfillBtn">Save</button></div>`, { runScripts: 'outside-only' });

  const requests = [];
  const modal = { show() {}, hide() {} };
  dom.window.bootstrap = {
    Modal: { getOrCreateInstance: () => modal, getInstance: () => modal },
    Offcanvas: { getOrCreateInstance: () => ({ show() {}, hide() {} }) }
  };
  dom.window.fetch = (url, init) => {
    requests.push({ url, init });
    return Promise.resolve({ ok: true, status: 200, json: () => Promise.resolve({ ok: true }) });
  };
  dom.window.setTimeout = () => 0;
  dom.window.alert = () => {};
  dom.window.eval(source);

  return {
    dom,
    requests,
    errors: () => dom.window.document.querySelector('[data-backfill-errors]').textContent,
    async submit() {
      dom.window.document.getElementById('submitBackfillBtn').click();
      await new Promise((resolve) => setImmediate(resolve));
    }
  };
}

for (const mode of ['Proposal', 'Direct']) {
  test(`${mode} backfill posts non-carried rows and leaves carried-forward rows out`, async () => {
    const ui = backfillModal(mode);
    await ui.submit();

    assert.equal(ui.errors(), '');
    assert.equal(ui.requests.length, 1);
    assert.equal(ui.requests[0].url, '/Projects/Stages/BackfillApply');
    const payload = JSON.parse(ui.requests[0].init.body);
    assert.equal(payload.submissionMode, mode);
    assert.deepEqual(payload.stages, [
      { stageCode: 'FS', stageToken: 'tok-fs', actualStart: null, completedOn: '2024-01-25' }
    ]);
    ui.dom.window.close();
  });
}

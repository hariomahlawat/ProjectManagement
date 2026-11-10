// The IT project stage editor (it-projects-details.js): Save must reach the server whether or not
// it asks first. jsdom does not enforce the browser rule that a submit started while a submit
// event is being dispatched is ignored, so createEditor() adds that rule to requestSubmit.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM, VirtualConsole } = require('jsdom');

const source = fs.readFileSync(path.resolve(__dirname, 'it-projects-details.js'), 'utf8');

const markup = `
<div data-itp-details data-itp-open="">
  <form method="post" action="/ItProjects/1?handler=Stages" data-page-handled data-itp-stage-editor data-itp-preview-url="/ItProjects/1?handler=Preview" novalidate>
    <input type="hidden" name="__RequestVerificationToken" value="token" />
    <input type="hidden" name="reopen" value="" data-itp-reopen />
    <ol>
      <li data-itp-stage data-itp-code="PPP"><input type="checkbox" data-itp-completed /><input type="date" data-itp-date /></li>
      <li data-itp-stage data-itp-code="SO"><input type="checkbox" data-itp-completed /><input type="date" data-itp-date /><input type="number" data-itp-cost /></li>
    </ol>
    <p data-itp-stage-note hidden></p>
    <button type="submit">Save stages</button>
  </form>
</div>`;

function createEditor({ preview = { valid: true, stages: [], completed: [], reopened: [], summary: '' }, confirmResult = true } = {}) {
    const dom = new JSDOM(markup, { runScripts: 'outside-only', virtualConsole: new VirtualConsole() });
    const { window } = dom;
    const form = window.document.querySelector('form');
    const state = { posted: 0, previews: 0, confirms: 0 };

    // Browser rule: requestSubmit() during a submit event's dispatch does nothing.
    let dispatching = false;
    form.addEventListener('submit', () => { dispatching = true; }, true);
    const original = window.HTMLFormElement.prototype.requestSubmit;
    window.HTMLFormElement.prototype.requestSubmit = function () {
        if (dispatching) {
            return;
        }
        return original.apply(this, arguments);
    };
    // Last listener: clears the flag and counts submissions that would really go to the server.
    window.addEventListener('submit', (event) => {
        dispatching = false;
        if (!event.defaultPrevented) {
            state.posted++;
            event.preventDefault();
        }
    });

    window.fetch = () => {
        state.previews++;
        return Promise.resolve({ ok: true, json: () => Promise.resolve(preview) });
    };
    window.PrismConfirm = {
        show: () => {
            state.confirms++;
            return Promise.resolve(confirmResult);
        }
    };

    window.eval(source);
    return { window, form, state };
}

const settle = (window) => new Promise((resolve) => window.setTimeout(resolve, 20));

test('Save with nothing to confirm posts the form', async () => {
    const { window, form, state } = createEditor();
    form.querySelector('button').click();
    await settle(window);
    assert.equal(state.posted, 1);
    assert.equal(state.confirms, 0);
});

test('Save after a preview with no consequence posts the form', async () => {
    const { window, form, state } = createEditor();
    const date = form.querySelector('[data-itp-code="PPP"] [data-itp-date]');
    date.value = '2026-11-01';
    date.dispatchEvent(new window.Event('change'));
    await settle(window);
    form.querySelector('button').click();
    await settle(window);
    assert.equal(state.previews, 1);
    assert.equal(state.posted, 1);
});

test('Save that completes earlier stages asks first, then posts', async () => {
    const preview = { valid: true, stages: [], completed: ['PPP'], reopened: [], summary: 'Saving will also complete IT ARPPP / IPA.' };
    const { window, form, state } = createEditor({ preview });
    const tick = form.querySelector('[data-itp-code="SO"] [data-itp-completed]');
    tick.checked = true;
    tick.dispatchEvent(new window.Event('change'));
    await settle(window);
    form.querySelector('button').click();
    await settle(window);
    assert.equal(state.confirms, 1);
    assert.equal(state.posted, 1);
});

test('Go back in the confirm leaves the form unsent', async () => {
    const preview = { valid: true, stages: [], completed: ['PPP'], reopened: [], summary: 'Saving will also complete IT ARPPP / IPA.' };
    const { window, form, state } = createEditor({ preview, confirmResult: false });
    const tick = form.querySelector('[data-itp-code="SO"] [data-itp-completed]');
    tick.checked = true;
    tick.dispatchEvent(new window.Event('change'));
    await settle(window);
    form.querySelector('button').click();
    await settle(window);
    assert.equal(state.confirms, 1);
    assert.equal(state.posted, 0);
});

test('a changed SO value is previewed, so a bad value is reported before Save', async () => {
    const preview = { valid: false, errors: ['The SO cost cannot be negative.'] };
    const { window, form, state } = createEditor({ preview });
    const cost = form.querySelector('[data-itp-cost]');
    cost.value = '-3';
    cost.dispatchEvent(new window.Event('change'));
    await settle(window);
    const note = form.querySelector('[data-itp-stage-note]');
    assert.equal(state.previews, 1);
    assert.equal(note.hidden, false);
    assert.equal(note.textContent, 'The SO cost cannot be negative.');
});

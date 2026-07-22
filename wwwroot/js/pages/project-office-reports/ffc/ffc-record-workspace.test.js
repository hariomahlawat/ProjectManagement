const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const workspaceScript = fs.readFileSync(
  path.join(__dirname, 'ffc-record-workspace.js'),
  'utf8');

function createDom(body) {
  const dom = new JSDOM(`<!doctype html><html><body>${body}</body></html>`, {
    runScripts: 'outside-only',
    url: 'https://prism.test/ProjectOfficeReports/FFC'
  });
  dom.window.bootstrap = {
    Offcanvas: {
      getOrCreateInstance: () => ({ hide() {}, show() {} })
    },
    Modal: {
      getOrCreateInstance: () => ({ show() {} })
    }
  };
  return dom;
}

function nextTurn() {
  return new Promise(resolve => setTimeout(resolve, 0));
}

test('untouched attachment drawer closes without a discard confirmation', async () => {
  const dom = createDom(`
    <main data-ffc-workspace>
      <div class="offcanvas ffc-workspace-drawer" id="ffcAttachmentEditor">
        <form method="post" data-ffc-dirty-form data-ffc-attachment-form>
          <input type="file" data-ffc-upload-input data-max-file-size="20971520">
          <input name="AttachmentInput.Caption">
          <button type="submit" data-ffc-upload-submit>Upload</button>
        </form>
      </div>
    </main>`);

  let confirmations = 0;
  dom.window.PrismConfirm = { show: async () => { confirmations += 1; return false; } };
  dom.window.eval(workspaceScript);

  const drawer = dom.window.document.getElementById('ffcAttachmentEditor');
  drawer.classList.add('show');
  drawer.dispatchEvent(new dom.window.Event('shown.bs.offcanvas'));
  const hideEvent = new dom.window.Event('hide.bs.offcanvas', { cancelable: true });
  drawer.dispatchEvent(hideEvent);
  await nextTurn();

  assert.equal(hideEvent.defaultPrevented, false);
  assert.equal(confirmations, 0);
});

test('dirty drawer uses the styled asynchronous confirmation', async () => {
  const dom = createDom(`
    <main data-ffc-workspace>
      <div class="offcanvas ffc-workspace-drawer" id="ffcRecordEditor">
        <form method="post" data-ffc-dirty-form>
          <input name="RecordInput.OverallRemarks" value="">
          <button type="submit">Save</button>
        </form>
      </div>
    </main>`);

  let confirmations = 0;
  dom.window.PrismConfirm = { show: async () => { confirmations += 1; return false; } };
  dom.window.eval(workspaceScript);

  const drawer = dom.window.document.getElementById('ffcRecordEditor');
  drawer.classList.add('show');
  drawer.dispatchEvent(new dom.window.Event('shown.bs.offcanvas'));
  const input = drawer.querySelector('input');
  input.value = 'Changed';
  input.dispatchEvent(new dom.window.Event('input', { bubbles: true }));

  const hideEvent = new dom.window.Event('hide.bs.offcanvas', { cancelable: true });
  drawer.dispatchEvent(hideEvent);
  await nextTurn();

  assert.equal(hideEvent.defaultPrevented, true);
  assert.equal(confirmations, 1);
});

test('typing a different country clears the stale native selection', () => {
  const dom = createDom(`
    <main class="ffc-record-create">
      <form method="post" data-ffc-dirty-form>
        <label for="Input_CountryId">Country</label>
        <select id="Input_CountryId" name="Input.CountryId" data-ffc-search-select>
          <option value="">Select country</option>
          <option value="1" selected>Myanmar</option>
          <option value="2">Ethiopia</option>
        </select>
        <button type="submit">Create</button>
      </form>
    </main>`);

  dom.window.PrismConfirm = { show: async () => false };
  dom.window.eval(workspaceScript);

  const select = dom.window.document.getElementById('Input_CountryId');
  const search = dom.window.document.getElementById('Input_CountryId-search');
  search.value = 'Ethiopia';
  search.dispatchEvent(new dom.window.Event('input', { bubbles: true }));

  assert.equal(select.value, '');
  assert.equal(search.getAttribute('aria-invalid'), 'false');
});

test('valid post forms are locked after the first submit', () => {
  const dom = createDom(`
    <main class="ffc-record-create">
      <form method="post">
        <input name="Name" value="Record">
        <button type="submit" data-ffc-submitting-text="Saving…">Save</button>
      </form>
    </main>`);

  dom.window.PrismConfirm = { show: async () => false };
  dom.window.eval(workspaceScript);

  const form = dom.window.document.querySelector('form');
  const button = form.querySelector('button');
  const first = new dom.window.SubmitEvent('submit', { bubbles: true, cancelable: true, submitter: button });
  form.dispatchEvent(first);

  assert.equal(form.dataset.ffcSubmitLocked, 'true');
  assert.equal(button.disabled, true);
  assert.match(button.textContent, /Saving/);

  const second = new dom.window.SubmitEvent('submit', { bubbles: true, cancelable: true, submitter: button });
  form.dispatchEvent(second);
  assert.equal(second.defaultPrevented, true);
});

test('discarding drawer changes restores the captured editor values before close', async () => {
  let hideCalls = 0;
  const dom = createDom(`
    <main data-ffc-workspace>
      <div class="offcanvas ffc-workspace-drawer show" id="ffcRecordEditor">
        <form method="post" data-ffc-dirty-form>
          <input name="RecordInput.OverallRemarks" value="Original">
          <button type="submit">Save</button>
        </form>
      </div>
    </main>`);

  dom.window.bootstrap.Offcanvas.getOrCreateInstance = () => ({ hide() { hideCalls += 1; } });
  dom.window.PrismConfirm = { show: async () => true };
  dom.window.eval(workspaceScript);

  const drawer = dom.window.document.getElementById('ffcRecordEditor');
  drawer.dispatchEvent(new dom.window.Event('shown.bs.offcanvas'));
  const input = drawer.querySelector('input');
  input.value = 'Changed';

  const hideEvent = new dom.window.Event('hide.bs.offcanvas', { cancelable: true });
  drawer.dispatchEvent(hideEvent);
  await nextTurn();

  assert.equal(hideEvent.defaultPrevented, true);
  assert.equal(input.value, 'Original');
  assert.equal(hideCalls, 1);
});

test('an invalid submission does not lock the form or suppress later dirty protection', () => {
  const dom = createDom(`
    <main class="ffc-record-create">
      <form method="post" data-ffc-dirty-form>
        <input name="Name" required value="">
        <button type="submit" data-ffc-submitting-text="Saving…">Save</button>
      </form>
    </main>`);

  dom.window.PrismConfirm = { show: async () => false };
  dom.window.eval(workspaceScript);

  const form = dom.window.document.querySelector('form');
  const button = form.querySelector('button');
  const submit = new dom.window.SubmitEvent('submit', { bubbles: true, cancelable: true, submitter: button });
  form.dispatchEvent(submit);

  assert.equal(form.dataset.ffcSubmitLocked, undefined);
  assert.equal(button.disabled, false);
  assert.doesNotMatch(button.textContent, /Saving/);
});

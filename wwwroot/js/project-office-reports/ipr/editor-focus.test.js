const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, 'index.js'), 'utf8');

function createDom() {
  const dom = new JSDOM(`<!doctype html><html><body>
    <a id="outside" href="/elsewhere">Site navigation</a>
    <aside id="iprRecordEditor" role="dialog" aria-modal="true" data-ipr-close-url="/ProjectOfficeReports/Ipr?selectedRecordId=1" tabindex="-1">
      <a id="close" href="/ProjectOfficeReports/Ipr?selectedRecordId=1" data-ipr-editor-close>Close</a>
      <form data-ipr-record-form>
        <input id="number" name="Input.FilingNumber" />
        <button id="save" type="submit" data-ipr-submit-button><span data-ipr-submit-label>Save</span></button>
      </form>
    </aside>
  </body></html>`, { url: 'http://localhost/ProjectOfficeReports/Ipr?mode=edit&id=1', runScripts: 'outside-only' });
  dom.window.eval(script);
  return dom;
}

const tab = (dom, shiftKey = false) => {
  const event = new dom.window.KeyboardEvent('keydown', { key: 'Tab', shiftKey, bubbles: true, cancelable: true });
  dom.window.document.dispatchEvent(event);
  return event;
};

test('editor keeps Tab and Shift+Tab inside the dialog', () => {
  const dom = createDom();
  const doc = dom.window.document;

  doc.getElementById('save').focus();
  assert.equal(tab(dom).defaultPrevented, true);
  assert.equal(doc.activeElement.id, 'close');

  assert.equal(tab(dom, true).defaultPrevented, true);
  assert.equal(doc.activeElement.id, 'save');

  doc.getElementById('outside').focus();
  tab(dom);
  assert.equal(doc.activeElement.id, 'close');
});

test('editor warns before leaving the page with unsaved changes', () => {
  const dom = createDom();
  const doc = dom.window.document;
  const clean = new dom.window.Event('beforeunload', { cancelable: true });
  dom.window.dispatchEvent(clean);
  assert.equal(clean.defaultPrevented, false);

  doc.getElementById('number').dispatchEvent(new dom.window.Event('input', { bubbles: true }));
  const dirty = new dom.window.Event('beforeunload', { cancelable: true });
  dom.window.dispatchEvent(dirty);
  assert.equal(dirty.defaultPrevented, true);
});

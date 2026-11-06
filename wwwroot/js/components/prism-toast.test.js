const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, 'prism-toast.js'), 'utf8');

// A fake clock, so timers can be driven from the test.
function createDom(body = '') {
  const dom = new JSDOM(`<!doctype html><html><body>${body}</body></html>`, { runScripts: 'outside-only' });
  const { window } = dom;
  let now = 0;
  let nextId = 0;
  const timers = new Map();
  window.Date.now = () => now;
  window.setTimeout = (fn, ms = 0) => { timers.set(++nextId, { fn, at: now + ms }); return nextId; };
  window.clearTimeout = (id) => timers.delete(id);
  window.matchMedia = () => ({ matches: true }); // Reduce Motion: dismissal is immediate
  window.eval(script);
  const advance = (ms) => {
    now += ms;
    for (const [id, timer] of [...timers]) {
      if (timer.at <= now) { timers.delete(id); timer.fn(); }
    }
  };
  return { window, document: window.document, advance };
}

const toasts = (document) => [...document.querySelectorAll('[data-prism-toast]')];

test('show builds a glass toast with tone, text, subject, detail and action', () => {
  const { window, document } = createDom();
  let undone = 0;
  window.PrismToast.show({
    tone: 'success',
    subject: 'Bid Opening Minutes.pdf',
    text: 'moved to Trash.',
    detail: 'An administrator can restore it for 30 days.',
    action: { label: 'Undo', run: () => { undone += 1; } }
  });

  const [toast] = toasts(document);
  assert.equal(toast.dataset.tone, 'success');
  assert.equal(toast.getAttribute('role'), 'status');
  assert.equal(toast.querySelector('b').textContent, 'Bid Opening Minutes.pdf');
  assert.match(toast.querySelector('.prism-toast__text').textContent, /moved to Trash\./);
  assert.equal(toast.querySelector('.prism-toast__detail').textContent, 'An administrator can restore it for 30 days.');
  assert.equal(document.querySelector('[data-prism-toasts]').getAttribute('aria-label'), 'Notifications');

  toast.querySelector('[data-prism-toast-action]').click();
  assert.equal(undone, 1);
  assert.equal(toasts(document).length, 0, 'the action also dismisses the toast');
});

test('text is set as text, never as markup', () => {
  const { window, document } = createDom();
  window.PrismToast.show({ text: '<img src=x onerror=alert(1)>' });
  assert.equal(document.querySelector('img'), null);
});

test('success fades after its life; an error stays until dismissed', () => {
  const { window, document, advance } = createDom();
  window.PrismToast.show({ tone: 'success', text: 'Remark saved.' });
  window.PrismToast.show({ tone: 'error', text: "Couldn't save the remark." });

  const error = toasts(document).find((el) => el.dataset.tone === 'danger');
  assert.equal(error.getAttribute('role'), 'alert');
  assert.equal(error.querySelector('.prism-toast__timer'), null);

  advance(5000);
  assert.deepEqual(toasts(document).map((el) => el.dataset.tone), ['danger']);
  advance(60000);
  assert.equal(toasts(document).length, 1);

  error.querySelector('[data-prism-toast-close]').click();
  assert.equal(toasts(document).length, 0);
});

test('hovering the stack pauses the timers', () => {
  const { window, document, advance } = createDom();
  window.PrismToast.show({ text: 'Plan saved as draft.' });
  const region = document.querySelector('[data-prism-toasts]');

  region.dispatchEvent(new window.MouseEvent('mouseenter'));
  advance(10000);
  assert.equal(toasts(document).length, 1, 'still there while hovered');

  region.dispatchEvent(new window.MouseEvent('mouseleave'));
  advance(4000);
  assert.equal(toasts(document).length, 1, 'the time left runs again only after the pointer leaves');
  advance(1000);
  assert.equal(toasts(document).length, 0);
});

test('newest in front, an error stays in front, at most three show', () => {
  const { window, document } = createDom();
  const error = window.PrismToast.show({ tone: 'danger', text: 'Upload failed.' }).element;
  const first = window.PrismToast.show({ text: 'One.' }).element;
  const second = window.PrismToast.show({ text: 'Two.' }).element;
  const third = window.PrismToast.show({ text: 'Three.' }).element;

  assert.equal(error.style.getPropertyValue('--i'), '0');
  assert.equal(third.style.getPropertyValue('--i'), '1');
  assert.equal(second.style.getPropertyValue('--i'), '2');
  assert.equal(first.style.visibility, 'hidden', 'the fourth toast waits behind');
});

test('toasts the server rendered are adopted: they fade and can be closed', async () => {
  const { window, document, advance } = createDom(`
    <div class="prism-toasts" data-prism-toasts role="region" aria-label="Notifications">
      <div class="prism-toast" data-prism-toast data-tone="success" role="status" data-life="5500">
        <p class="prism-toast__text">Remark saved.</p>
        <button type="button" data-prism-toast-close aria-label="Dismiss"></button>
      </div>
      <div class="prism-toast" data-prism-toast data-tone="danger" role="alert" data-life="0">
        <p class="prism-toast__text">You do not have access to that project.</p>
        <button type="button" data-prism-toast-close aria-label="Dismiss"></button>
      </div>
    </div>`);
  // The layout loads the script while the page is still loading; it adopts on DOMContentLoaded.
  if (document.readyState === 'loading') {
    await new Promise((resolve) => window.addEventListener('DOMContentLoaded', resolve, { once: true }));
  }

  advance(6000);
  assert.deepEqual(toasts(document).map((el) => el.dataset.tone), ['danger']);
  toasts(document)[0].querySelector('[data-prism-toast-close]').click();
  assert.equal(toasts(document).length, 0);
});

test('Escape inside a toast dismisses it', () => {
  const { window, document } = createDom();
  window.PrismToast.show({ tone: 'danger', text: 'Upload failed.' });
  const close = document.querySelector('[data-prism-toast-close]');
  close.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
  assert.equal(toasts(document).length, 0);
});

test('a server-rendered stack also pauses while hovered', async () => {
  const { window, document, advance } = createDom(`
    <div class="prism-toasts" data-prism-toasts role="region" aria-label="Notifications">
      <div class="prism-toast" data-prism-toast data-tone="success" role="status" data-life="5500">
        <p class="prism-toast__text">Remark saved.</p>
        <button type="button" data-prism-toast-close aria-label="Dismiss"></button>
      </div>
    </div>`);
  if (document.readyState === 'loading') {
    await new Promise((resolve) => window.addEventListener('DOMContentLoaded', resolve, { once: true }));
  }

  document.querySelector('[data-prism-toasts]').dispatchEvent(new window.MouseEvent('mouseenter'));
  advance(20000);
  assert.equal(toasts(document).length, 1);
});

test('closing a focused toast from the keyboard lets the others run out again', () => {
  const { window, document, advance } = createDom();
  window.PrismToast.show({ text: 'First.' });
  window.PrismToast.show({ text: 'Second.' });
  const [, second] = toasts(document);

  const close = second.querySelector('[data-prism-toast-close]');
  close.focus();
  close.dispatchEvent(new window.FocusEvent('focusin', { bubbles: true }));
  close.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
  assert.deepEqual(toasts(document).map((el) => el.textContent.trim()), ['First.']);

  advance(60000);
  assert.equal(toasts(document).length, 0, 'the remaining toast is not left paused');
});

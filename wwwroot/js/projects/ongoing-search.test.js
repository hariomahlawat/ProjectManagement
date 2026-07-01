const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

class MockElement {
  constructor(value = '') {
    this.value = value;
    this.dataset = {};
    this.listeners = new Map();
    this.form = null;
  }

  addEventListener(type, handler) {
    const handlers = this.listeners.get(type) ?? [];
    handlers.push(handler);
    this.listeners.set(type, handlers);
  }

  dispatch(type, event = {}) {
    const payload = {
      key: undefined,
      isComposing: false,
      repeat: false,
      preventDefault() { this.defaultPrevented = true; },
      defaultPrevented: false,
      ...event,
    };

    for (const handler of this.listeners.get(type) ?? []) {
      handler(payload);
    }

    return payload;
  }

  querySelector() { return null; }
  querySelectorAll() { return []; }
  classList = { add() {}, remove() {} };
}

function createHarness() {
  const controls = {
    ProjectCategoryId: new MockElement(),
    PresentStageCode: new MockElement(),
    StageBucket: new MockElement(),
    ProjectOfficerId: new MockElement(),
    StageFlow: new MockElement('reverse'),
    Search: new MockElement(),
  };

  let submitCount = 0;
  let timerCount = 0;

  const form = new MockElement();
  form.querySelector = selector => {
    const match = selector.match(/^\[name="(.+)"\]$/);
    return match ? controls[match[1]] ?? null : null;
  };
  form.requestSubmit = () => { submitCount += 1; };
  form.submit = () => { submitCount += 1; };

  Object.values(controls).forEach(control => { control.form = form; });

  const document = {
    getElementById: id => id === 'ongoingProjectsFilterForm' ? form : null,
    querySelectorAll: () => [],
  };

  const window = {
    clearTimeout() {},
    setTimeout() { timerCount += 1; return timerCount; },
    getSelection: () => ({ toString: () => '' }),
  };

  const scriptPath = path.resolve(__dirname, 'ongoing.js');
  const script = fs.readFileSync(scriptPath, 'utf8');
  vm.runInNewContext(script, { document, window, console });

  return {
    search: controls.Search,
    category: controls.ProjectCategoryId,
    submitCount: () => submitCount,
    timerCount: () => timerCount,
  };
}

test('typing in ongoing-project search does not submit or schedule a search', () => {
  const harness = createHarness();
  harness.search.value = 'drone';
  harness.search.dispatch('input');

  assert.equal(harness.submitCount(), 0);
  assert.equal(harness.timerCount(), 0);
});

test('Enter submits the ongoing-project search exactly once', () => {
  const harness = createHarness();
  const event = harness.search.dispatch('keydown', { key: 'Enter' });

  assert.equal(event.defaultPrevented, true);
  assert.equal(harness.submitCount(), 1);
});

test('composition, held Enter, and other keys do not submit', () => {
  const harness = createHarness();

  harness.search.dispatch('keydown', { key: 'a' });
  harness.search.dispatch('keydown', { key: 'Enter', isComposing: true });
  harness.search.dispatch('keydown', { key: 'Enter', repeat: true });

  assert.equal(harness.submitCount(), 0);
});

test('non-search filters continue to auto-submit', () => {
  const harness = createHarness();
  harness.category.value = '4';
  harness.category.dispatch('change');

  assert.equal(harness.submitCount(), 1);
});

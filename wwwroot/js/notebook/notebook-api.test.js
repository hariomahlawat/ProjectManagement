const { test, beforeEach, afterEach } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

// SECTION: ESM loader helper for Notebook browser modules.
async function loadApiModule() {
  const apiPath = path.resolve(__dirname, 'notebook-api.js');
  const sessionPath = path.resolve(__dirname, '../core/session-auth.js');
  const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'notebook-api-test-'));
  fs.writeFileSync(path.join(tempDir, 'package.json'), '{"type":"module"}');
  fs.writeFileSync(path.join(tempDir, 'session-auth.mjs'), fs.readFileSync(sessionPath, 'utf8'));
  const source = fs.readFileSync(apiPath, 'utf8').replace("import { notifySessionExpired } from '../core/session-auth.js';", "import { notifySessionExpired } from './session-auth.mjs';");
  const modulePath = path.join(tempDir, `notebook-api-${Date.now()}-${Math.random()}.mjs`);
  fs.writeFileSync(modulePath, source);
  return import(`file://${modulePath}`);
}

// SECTION: Fetch response helpers.
function jsonResponse(status, payload, contentType = 'application/json; charset=utf-8') {
  return new Response(JSON.stringify(payload), { status, headers: { 'content-type': contentType } });
}

function mutationResponse() {
  return { item: { id: 'note-1', version: 'version-2', title: 'Updated' } };
}

function updatePayload() {
  return {
    title: 'Updated',
    body: 'Body',
    type: 'Note',
    priority: 'Normal',
    reminderAtUtc: null,
    colorKey: null,
    labels: [],
    checklistRows: [],
    version: 'version-1'
  };
}

beforeEach(() => {
  global.document = {
    documentElement: { dataset: { environment: 'Production' } },
    querySelector: () => ({ value: 'anti-forgery-token' }),
    dispatchEvent: () => {}
  };
  global.location = { hostname: 'example.test' };
});

afterEach(() => {
  delete global.fetch;
  delete global.document;
  delete global.location;
});

// SECTION: JSON mutation request tests.
test('updateItem sends JSON content type, serialised body, and anti-forgery token', async () => {
  const { NotebookApi } = await loadApiModule();
  let captured;
  global.fetch = async (url, options) => {
    captured = { url, options };
    return jsonResponse(200, mutationResponse());
  };

  await NotebookApi.updateContent('note-1', { title: 'Updated', body: 'Body', version: 'version-1' });

  const headers = new Headers(captured.options.headers);
  assert.equal(captured.url, '/api/notebook/items/note-1/content');
  assert.equal(captured.options.method, 'PATCH');
  assert.equal(headers.get('Content-Type'), 'application/json; charset=utf-8');
  assert.equal(headers.get('X-CSRF-TOKEN'), 'anti-forgery-token');
  assert.equal(captured.options.credentials, 'same-origin');
  assert.deepEqual(JSON.parse(captured.options.body), { title: 'Updated', body: 'Body', version: 'version-1' });
});

test('all Notebook JSON mutations declare application/json content type', async () => {
  const { NotebookApi } = await loadApiModule();
  const calls = [];
  global.fetch = async (url, options) => {
    calls.push({ url, options });
    return jsonResponse(200, mutationResponse());
  };

  await NotebookApi.createItem(updatePayload());
  await NotebookApi.updateItem('note-1', updatePayload());
  await NotebookApi.updateContent('note-1', { title: 'Updated', body: 'Body', version: 'version-1' });
  await NotebookApi.updateChecklist('note-1', { title: 'Updated', body: 'Body', version: 'version-1', checklistRows: [{ id: 7, text: 'Task', isDone: false, sortOrder: 0 }] });
  await NotebookApi.setPinned('note-1', true, 'version-1');
  await NotebookApi.setColour('note-1', 'amber', 'version-1');
  await NotebookApi.archiveItem('note-1', 'version-1');
  await NotebookApi.completeItem('note-1', 'version-1');
  await NotebookApi.reopenItem('note-1', 'version-1');
  await NotebookApi.duplicateItem('note-1');
  await NotebookApi.deleteItem('note-1', 'version-1');
  await NotebookApi.restoreItem('note-1', 'version-1');
  await NotebookApi.showCheckboxes('note-1', 'version-1');
  await NotebookApi.hideCheckboxes('note-1', 'version-1');
  await NotebookApi.toggleChecklistItem('note-1', 7, true, 'version-1');

  assert.ok(calls.length >= 12);
  for (const call of calls) {
    assert.equal(new Headers(call.options.headers).get('Content-Type'), 'application/json; charset=utf-8', call.url);
  }
});


test('setColour sends a dedicated colour mutation payload', async () => {
  const { NotebookApi } = await loadApiModule();
  let captured;
  global.fetch = async (url, options) => {
    captured = { url, options };
    return jsonResponse(200, mutationResponse());
  };

  await NotebookApi.setColour('note-1', 'green', 'version-1');

  assert.equal(captured.url, '/api/notebook/items/note-1/colour');
  assert.equal(captured.options.method, 'POST');
  assert.deepEqual(JSON.parse(captured.options.body), { colorKey: 'green', version: 'version-1' });
});

test('request honors case-insensitive caller content-type and does not duplicate it', async () => {
  const { request } = await loadApiModule();
  let captured;
  global.fetch = async (url, options) => {
    captured = { url, options };
    return jsonResponse(200, {});
  };

  await request('/api/notebook/items/note-1', { method: 'PATCH', headers: { 'content-type': 'application/json' }, body: '{}' });

  const headers = new Headers(captured.options.headers);
  assert.equal(headers.get('Content-Type'), 'application/json');
  assert.equal([...headers.keys()].filter((key) => key.toLowerCase() === 'content-type').length, 1);
});

test('request does not set a manual content type for FormData', async () => {
  const { request } = await loadApiModule();
  let captured;
  global.fetch = async (url, options) => {
    captured = { url, options };
    return jsonResponse(200, {});
  };

  await request('/api/notebook/items/import', { method: 'POST', body: new FormData() });

  assert.equal(new Headers(captured.options.headers).has('Content-Type'), false);
});

test('HTTP 415 produces a structured NotebookApiError with reload guidance', async () => {
  const { NotebookApi, NotebookApiError } = await loadApiModule();
  global.fetch = async () => new Response('', { status: 415, headers: { 'content-type': 'text/plain' } });

  await assert.rejects(
    () => NotebookApi.updateItem('note-1', updatePayload()),
    (error) => {
      assert.ok(error instanceof NotebookApiError);
      assert.equal(error.status, 415);
      assert.match(error.message, /request format is not supported/);
      assert.equal(error.method, 'PATCH');
      return true;
    }
  );
});


test('unexpected login redirect is reported as a session-expired NotebookApiError', async () => {
  const { NotebookApi, NotebookApiError } = await loadApiModule();
  global.fetch = async () => {
    const response = jsonResponse(200, { html: true }, 'text/html; charset=utf-8');
    Object.defineProperty(response, 'redirected', { value: true });
    Object.defineProperty(response, 'url', { value: 'http://localhost/Identity/Account/Login?ReturnUrl=%2FNotebook' });
    return response;
  };

  await assert.rejects(
    () => NotebookApi.updateItem('note-1', updatePayload()),
    (error) => {
      assert.ok(error instanceof NotebookApiError);
      assert.equal(error.status, 401);
      assert.equal(error.code, 'notebook_session_expired');
      return true;
    }
  );
});


test('jsonRequestOptions rejects function payloads with a typed client error', async () => {
  const { jsonRequestOptions } = await loadApiModule();

  assert.throws(
    () => jsonRequestOptions('PATCH', () => ({})),
    (error) => error.code === 'notebook_invalid_client_payload'
  );
});

test('HTTP 409 exposes currentVersion for deterministic conflict recovery', async () => {
  const { NotebookApi, NotebookApiError } = await loadApiModule();
  global.fetch = async () => jsonResponse(409, {
    code: 'notebook_concurrency_conflict',
    message: 'This note was changed elsewhere.',
    currentVersion: '123e4567-e89b-12d3-a456-426614174000'
  });

  await assert.rejects(
    () => NotebookApi.updateContent('note-1', { title: 'Updated', body: 'Body', version: '223e4567-e89b-12d3-a456-426614174000' }),
    (error) => {
      assert.ok(error instanceof NotebookApiError);
      assert.equal(error.status, 409);
      assert.equal(error.currentVersion, '123e4567-e89b-12d3-a456-426614174000');
      return true;
    }
  );
});

test('aborted Notebook request is reported with a typed cancellation code', async () => {
  const { NotebookApi, NotebookApiError } = await loadApiModule();
  global.fetch = async (_url, options) => new Promise((_resolve, reject) => {
    options.signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true });
  });

  const controller = new AbortController();
  const requestPromise = NotebookApi.updateContent(
    'note-1',
    { title: 'Updated', body: 'Body', version: 'version-1' },
    { signal: controller.signal }
  );
  controller.abort();

  await assert.rejects(requestPromise, (error) => {
    assert.ok(error instanceof NotebookApiError);
    assert.equal(error.code, 'notebook_request_aborted');
    return true;
  });
});

test('setLabels sends a dedicated labels mutation payload', async () => {
  const api = await loadApiModule();
  let captured;
  global.fetch = async (url, options) => { captured = { url, options }; return new Response(JSON.stringify({ item: {} }), { status: 200, headers: { 'content-type': 'application/json' } }); };
  global.document = { querySelector: () => ({ value: 'token' }), documentElement: { dataset: {} } };
  await api.NotebookApi.setLabels('abc', ['Docs', 'Ops'], '123e4567-e89b-12d3-a456-426614174000');
  assert.equal(captured.url, '/api/notebook/items/abc/labels');
  assert.deepEqual(JSON.parse(captured.options.body), { labels: ['Docs', 'Ops'], version: '123e4567-e89b-12d3-a456-426614174000' });
});

test('createLabel sends a dedicated server-backed label creation request', async () => {
  const { NotebookApi } = await loadApiModule();
  let captured;
  global.fetch = async (url, options) => {
    captured = { url, options };
    return jsonResponse(200, { label: { id: 4, name: 'Work', count: 0 }, labels: [] });
  };

  await NotebookApi.createLabel('Work');

  assert.equal(captured.url, '/api/notebook/labels');
  assert.equal(captured.options.method, 'POST');
  assert.deepEqual(JSON.parse(captured.options.body), { name: 'Work' });
});

test('reorderItems sends one batch order mutation', async () => {
  const { NotebookApi } = await loadApiModule();
  let captured;
  global.fetch = async (url, options) => {
    captured = { url, options };
    return jsonResponse(200, { section: 'others', itemIds: [] });
  };

  await NotebookApi.reorderItems('others', [
    { id: '11111111-1111-4111-8111-111111111111', version: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' }
  ]);

  assert.equal(captured.url, '/api/notebook/order');
  assert.equal(captured.options.method, 'PUT');
  assert.deepEqual(JSON.parse(captured.options.body), {
    section: 'others',
    items: [{ id: '11111111-1111-4111-8111-111111111111', version: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa' }]
  });
});

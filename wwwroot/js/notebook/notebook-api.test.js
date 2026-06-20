const { test, beforeEach, afterEach } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

// SECTION: ESM loader helper for Notebook browser modules.
async function loadApiModule() {
  const filePath = path.resolve(__dirname, 'notebook-api.js');
  const source = fs.readFileSync(filePath, 'utf8');
  return import(`data:text/javascript;charset=utf-8,${encodeURIComponent(source)}`);
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
    querySelector: () => ({ value: 'anti-forgery-token' })
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

  await NotebookApi.updateItem('note-1', updatePayload());

  const headers = new Headers(captured.options.headers);
  assert.equal(captured.url, '/api/notebook/items/note-1');
  assert.equal(captured.options.method, 'PATCH');
  assert.equal(headers.get('Content-Type'), 'application/json; charset=utf-8');
  assert.equal(headers.get('RequestVerificationToken'), 'anti-forgery-token');
  assert.equal(captured.options.credentials, 'same-origin');
  assert.deepEqual(JSON.parse(captured.options.body), updatePayload());
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
  await NotebookApi.setPinned('note-1', true, 'version-1');
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
      assert.match(error.message, /Reload the page/);
      assert.equal(error.method, 'PATCH');
      return true;
    }
  );
});

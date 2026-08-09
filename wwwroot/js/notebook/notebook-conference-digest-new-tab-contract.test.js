import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const projectRoot = process.cwd();
const modalPath = path.join(projectRoot, 'Pages', 'Notebook', '_NotebookConferenceDigestModal.cshtml');
const modal = fs.readFileSync(modalPath, 'utf8');

test('conference review drill-down opens in a new tab safely', () => {
  const reviewLink = modal.match(/<a href="@group\.ConferenceReviewUrl"[\s\S]*?<\/a>/)?.[0] ?? '';
  assert.match(reviewLink, /target="_blank"/);
  assert.match(reviewLink, /rel="noopener noreferrer"/);
  assert.match(reviewLink, /visually-hidden">\(opens in a new tab\)<\/span>/);
});

test('project idea and task drill-down links open in a new tab safely', () => {
  const itemLink = modal.match(/<a href="@item\.OpenUrl"[\s\S]*?<\/a>/)?.[0] ?? '';
  assert.match(itemLink, /target="_blank"/);
  assert.match(itemLink, /rel="noopener noreferrer"/);
  assert.match(itemLink, /visually-hidden">\(opens in a new tab\)<\/span>/);
});

test('digest-local View all action remains an in-page modal action', () => {
  assert.match(modal, /id="notebookConferenceDigestModal"/);
  assert.doesNotMatch(modal, /data-bs-dismiss="modal"[^>]*target="_blank"/);
});

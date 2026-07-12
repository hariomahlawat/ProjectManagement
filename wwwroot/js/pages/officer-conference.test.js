const fs = require('node:fs');
const path = require('node:path');
const { test } = require('node:test');
const assert = require('node:assert/strict');

const source = fs.readFileSync(path.resolve(__dirname, 'officer-conference.js'), 'utf8');

test('conference editor supports keyboard save and cancel', () => {
    assert.match(source, /event\.key === 'Escape'/);
    assert.match(source, /event\.ctrlKey \|\| event\.metaKey/);
});

test('conference save uses antiforgery and same-origin credentials', () => {
    assert.match(source, /X-CSRF-TOKEN/);
    assert.match(source, /credentials: 'same-origin'/);
});

test('conference direction rendering uses textContent rather than user HTML', () => {
    assert.match(source, /body\.textContent = direction\.body/);
    assert.doesNotMatch(source, /direction\.body.*innerHTML/);
});

test('officer navigation uses server-generated route values', () => {
    assert.match(source, /window\.location\.assign\(destination\)/);
    assert.doesNotMatch(source, /`\/Workspace\/Conference\//);
});

test('conference editor restores focus and exposes busy and invalid states', () => {
    assert.match(source, /restoreFocus: true/);
    assert.match(source, /aria-busy/);
    assert.match(source, /aria-invalid/);
});

test('conference save surfaces server trace references and row-local feedback', () => {
    assert.match(source, /payload\.traceId/);
    assert.match(source, /setRowStatus\(item, 'Direction saved\.'/);
    assert.match(source, /setRowStatus\(item, 'Save failed\.', true\)/);
});


test('conference direction metadata includes time', () => {
    assert.match(source, /hour: '2-digit'/);
    assert.match(source, /minute: '2-digit'/);
});

test('conference directions expose accessible more and less controls', () => {
    assert.match(source, /data-oc-direction-toggle/);
    assert.match(source, /aria-expanded/);
    assert.match(source, /toggle\.textContent = expanded \? 'Less' : 'More'/);
});

test('conference save uses only row-local success feedback', () => {
    assert.doesNotMatch(source, /setPageFeedback/);
    assert.match(source, /setRowStatus\(item, 'Direction saved\.'/);
});

import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const cssPath = path.resolve(here, '../../css/notebook.css');
const css = fs.readFileSync(cssPath, 'utf8');

function rule(selector) {
    const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const match = css.match(new RegExp(`${escaped}\\s*\\{([^}]*)\\}`, 'm'));
    assert.ok(match, `Missing CSS rule for ${selector}`);
    return match[1];
}

test('conference digest uses restrained hierarchy for the expanded register', () => {
    assert.match(rule('.notebook-conference-digest-modal__header h2'), /font-weight:\s*650\s*;/);
    assert.match(rule('.notebook-conference-digest-officer__header h3'), /font-weight:\s*600\s*;/);
    assert.match(rule('.notebook-conference-digest-item__title'), /font-weight:\s*500\s*;/);
    assert.match(rule('.notebook-conference-digest-item p'), /font-weight:\s*400\s*;/);
    assert.match(rule('.notebook-conference-digest-item__heading time'), /font-weight:\s*400\s*;/);
});

test('compact digest follows the same PO-to-item-to-direction hierarchy', () => {
    assert.match(rule('.notebook-conference-digest-preview-group__heading strong'), /font-weight:\s*600\s*;/);
    assert.match(rule('.notebook-conference-digest-preview-item__titleline strong'), /font-weight:\s*500\s*;/);
    assert.match(rule('.notebook-conference-digest-preview-item__direction'), /font-weight:\s*400\s*;/);
    assert.match(rule('.notebook-conference-digest-preview-item__titleline time'), /font-weight:\s*500\s*;/);
});

test('supporting actions and metadata no longer compete with content', () => {
    assert.match(rule('.notebook-conference-digest-officer__review-link'), /font-weight:\s*500\s*;/);
    assert.match(rule('.notebook-conference-digest-card__topline small'), /font-weight:\s*500\s*;/);
    assert.match(rule('.notebook-conference-digest-card__footer'), /font-weight:\s*500\s*;/);
});

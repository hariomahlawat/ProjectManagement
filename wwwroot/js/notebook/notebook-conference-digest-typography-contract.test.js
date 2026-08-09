const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

const here = __dirname;
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

test('PRISM-shared compact card follows the same PO-to-item-to-direction hierarchy', () => {
    assert.match(rule('.notebook-conference-shared-card__officer'), /font-weight:\s*600\s*;/);
    assert.match(rule('.notebook-conference-shared-card__item strong'), /font-weight:\s*500\s*;/);
    assert.match(rule('.notebook-conference-shared-card__item > span'), /font-weight:\s*400\s*;/);
});

test('supporting actions do not compete with content', () => {
    assert.match(rule('.notebook-conference-digest-officer__review-link'), /font-weight:\s*500\s*;/);
    assert.match(rule('.notebook-conference-shared-card__more'), /font-weight:\s*500\s*;/);
});

const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const { describe, init } = require('./procurement-inr-preview.js');

test('reads rupee amounts back in crore, lakh or rupees', () => {
    assert.equal(describe('48000000').text, 'Reads as ₹4.8 crore');
    assert.equal(describe(' 46500000 ').text, 'Reads as ₹4.65 crore');
    assert.equal(describe('1850000').text, 'Reads as ₹18.5 lakh');
    assert.equal(describe('75000').text, 'Reads as ₹75,000');
    assert.equal(describe(''), null);
});

test('flags input the server would not accept', () => {
    const result = describe('4.8 Cr');
    assert.equal(result.valid, false);
    assert.equal(describe('12.345').valid, false);
    assert.equal(describe('4,65,00,000').valid, false);
});

test('updates the hint as the amount is typed and restores it when cleared', async () => {
    const dom = new JSDOM(`<!DOCTYPE html><body>
        <input id="Input_AonCost" aria-describedby="Input_AonCost_Hint" data-inr-preview-source />
        <div id="Input_AonCost_Hint"><span data-inr-preview>In rupees.</span></div>
    </body>`);
    const doc = dom.window.document;
    init(doc);
    if (doc.readyState === 'loading') {
        await new Promise((resolve) => doc.addEventListener('DOMContentLoaded', resolve, { once: true }));
    }
    const input = doc.getElementById('Input_AonCost');
    const hint = doc.querySelector('[data-inr-preview]');

    input.value = '46500000';
    input.dispatchEvent(new dom.window.Event('input'));
    assert.equal(hint.textContent, 'Reads as ₹4.65 crore');

    input.value = 'abc';
    input.dispatchEvent(new dom.window.Event('input'));
    assert.ok(hint.classList.contains('text-danger'));

    input.value = '';
    input.dispatchEvent(new dom.window.Event('input'));
    assert.equal(hint.textContent, 'In rupees.');
    assert.ok(!hint.classList.contains('text-danger'));
});

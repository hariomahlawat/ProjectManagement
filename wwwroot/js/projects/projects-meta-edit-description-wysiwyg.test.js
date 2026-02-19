const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

const scriptPath = path.resolve(__dirname, '../pages/projects-meta-edit-description-wysiwyg.js');
const scriptContent = fs.readFileSync(scriptPath, 'utf8');

function createEditorPage({ maxLength = 10, initialHidden = '' } = {}) {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <form id="project-form">
            <div data-pm-desc-editor data-initial-markdown=""></div>
            <input type="hidden" id="Description" data-pm-desc-hidden data-maxlength="${maxLength}" value="${initialHidden}" />
            <textarea data-pm-desc-fallback class="d-none"></textarea>
            <small data-char-count-for="Description"></small>
            <div data-pm-desc-limit-message class="d-none"></div>
            <div data-pm-desc-unavailable-message class="d-none"></div>
            <button type="submit">Save</button>
        </form>
    </body></html>`, { url: 'https://example.test/', runScripts: 'dangerously' });

    const { window } = dom;

    class FakeEditor {
        constructor(options) {
            this.value = options.initialValue || '';
            this.handlers = new Map();
            this.focusCalls = 0;
            window.__editor = this;
        }

        getMarkdown() {
            return this.value;
        }

        setMarkdown(value) {
            this.value = value;
        }

        on(eventName, handler) {
            this.handlers.set(eventName, handler);
        }

        triggerChange(value) {
            this.value = value;
            const handler = this.handlers.get('change');
            if (handler) {
                handler();
            }
        }

        focus() {
            this.focusCalls += 1;
        }
    }

    window.toastui = { Editor: FakeEditor };

    const scriptEl = window.document.createElement('script');
    scriptEl.textContent = scriptContent;
    window.document.body.appendChild(scriptEl);
    window.document.dispatchEvent(new window.Event('DOMContentLoaded', { bubbles: true }));

    return { dom, window, document: window.document, editor: window.__editor };
}

test('blocks submit for oversized edit right before submit and focuses editor', () => {
    const { document, editor } = createEditorPage({ maxLength: 10 });
    const form = document.getElementById('project-form');
    const hidden = document.querySelector('[data-pm-desc-hidden]');
    const counter = document.querySelector('[data-char-count-for="Description"]');
    const limitMessage = document.querySelector('[data-pm-desc-limit-message]');

    editor.triggerChange('1234567890');
    editor.value = '12345678901';

    const submitEvent = new document.defaultView.Event('submit', { bubbles: true, cancelable: true });
    form.dispatchEvent(submitEvent);

    assert.equal(submitEvent.defaultPrevented, true);
    assert.equal(limitMessage.classList.contains('d-none'), false);
    assert.equal(editor.focusCalls, 1);
    assert.equal(hidden.value, '1234567890');
    assert.equal(counter.textContent, '10/10');
});

test('normalizes line endings so counter length matches submitted payload length', () => {
    const { document, editor } = createEditorPage({ maxLength: 12 });
    const form = document.getElementById('project-form');
    const hidden = document.querySelector('[data-pm-desc-hidden]');
    const counter = document.querySelector('[data-char-count-for="Description"]');
    const limitMessage = document.querySelector('[data-pm-desc-limit-message]');

    editor.triggerChange('ab\ncd\nef');

    const submitEvent = new document.defaultView.Event('submit', { bubbles: true, cancelable: true });
    form.dispatchEvent(submitEvent);

    assert.equal(submitEvent.defaultPrevented, false);
    assert.equal(hidden.value, 'ab\r\ncd\r\nef');
    assert.equal(hidden.value.length, 10);
    assert.equal(counter.textContent, '10/12');
    assert.equal(limitMessage.classList.contains('d-none'), true);
});

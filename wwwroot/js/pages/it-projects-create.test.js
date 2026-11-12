// New IT project form (it-projects-create.js): the fields shown follow the New / CL / CF choice.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM, VirtualConsole } = require('jsdom');

const source = fs.readFileSync(path.resolve(__dirname, 'it-projects-create.js'), 'utf8');
// The form is a view shared by the IT, MOLTI and SRE pages; each page loads the script.
const view = fs.readFileSync(path.resolve(__dirname, '../../../Pages/ItProjects/_ModuleCreate.cshtml'), 'utf8');
const pages = ['ItProjects', 'MoltiProjects', 'SreProjects'].map((folder) =>
    fs.readFileSync(path.resolve(__dirname, `../../../Pages/${folder}/Create.cshtml`), 'utf8'));

function createForm(checked = 'New') {
    const radio = (value) => `<input type="radio" name="Input.Kind" value="${value}" data-itp-kind-option ${value === checked ? 'checked' : ''} />`;
    const dom = new JSDOM(`
      <form>
        <fieldset data-itp-kind>${radio('New')}${radio('CL')}${radio('CF')}</fieldset>
        <div data-itp-kind-show="new" id="year"></div>
        <div data-itp-kind-show="carried" id="first"></div>
      </form>`, { runScripts: 'outside-only', virtualConsole: new VirtualConsole() });
    dom.window.eval(source);
    return dom.window;
}

const shown = (window, id) => !window.document.getElementById(id).hidden;

test('New shows the financial year and hides the carried-over fields', () => {
    const window = createForm('New');
    assert.equal(shown(window, 'year'), true);
    assert.equal(shown(window, 'first'), false);
});

test('CL or CF shows the first-listed fields and hides the New year picker', () => {
    const window = createForm('New');
    const cf = window.document.querySelector('input[value="CF"]');
    cf.checked = true;
    cf.dispatchEvent(new window.Event('change'));
    assert.equal(shown(window, 'year'), false);
    assert.equal(shown(window, 'first'), true);
});

test('a form posted back as CL opens on the carried-over fields', () => {
    const window = createForm('CL');
    assert.equal(shown(window, 'first'), true);
    assert.equal(shown(window, 'year'), false);
});

test('the view offers New, CL and CF and loads the script', () => {
    for (const value of ['KindNew', 'KindCommittedLiability', 'KindCarriedForward']) {
        assert.match(view, new RegExp(`value="@CreateModel\\.${value}"`));
    }
    for (const page of pages) {
        assert.match(page, /_ModuleCreate\.cshtml/);
        assert.match(page, /js\/pages\/it-projects-create\.js/);
    }
});

// One name field (photos-people-name-field.js): known people or a new name, sent to the
// handler that fits, on top of the plain select and text box that work without script.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM, VirtualConsole } = require('jsdom');

const source = fs.readFileSync(path.resolve(__dirname, 'photos-people-name-field.js'), 'utf8');
const { resolveName, matchPeople } = require('./photos-people-name-field.js');

const people = [{ id: 'a', name: 'Asha Verma' }, { id: 'b', name: 'Vikram  Nair' }, { id: 'c', name: 'Vera Asher' }];

test('typed text is a known person, a new name or nothing', () => {
    assert.deepEqual(resolveName(people, '   '), { kind: 'empty' });
    assert.deepEqual(resolveName(people, ' vikram nair '), { kind: 'known', id: 'b', name: 'Vikram  Nair' }, 'case and spacing do not make a second person');
    assert.deepEqual(resolveName(people, 'Vikram'), { kind: 'new', name: 'Vikram' }, 'part of a name is a new name, never a guess');
    assert.deepEqual(matchPeople(people, 'ver').map(person => person.id), ['c', 'a'], 'names that start with the text come first');
    assert.equal(matchPeople(people, '').length, 3);
});

function createForm() {
    const dom = new JSDOM(`
      <form method="post" action="/Photos/People/Review">
        <div data-name-field data-assign-action="/Photos/People/Review?handler=AssignGroup" data-create-action="/Photos/People/Review?handler=CreateGroup">
          <div data-name-field-ui hidden>
            <input type="text" data-name-field-text aria-expanded="false" />
            <ul id="names" data-name-field-list hidden></ul>
            <button type="submit" data-name-field-confirm>Confirm</button>
          </div>
          <div data-name-field-fallback>
            <select name="personId"><option value="">Select a person</option><option value="a" data-portrait="/p/a">Asha Verma</option><option value="b">Vikram Nair</option></select>
            <button type="submit" formaction="/Photos/People/Review?handler=AssignGroup">Assign selected</button>
          </div>
          <div data-name-field-fallback>
            <input name="displayName" />
            <button type="submit" formaction="/Photos/People/Review?handler=CreateGroup">Create and confirm selected</button>
          </div>
        </div>
      </form>`, { url: 'http://localhost/Photos/People/Review', runScripts: 'outside-only', virtualConsole: new VirtualConsole() });
    const { window } = dom;
    window.eval(source);
    const doc = window.document;
    const q = (selector) => doc.querySelector(selector);
    const type = (value) => { q('[data-name-field-text]').value = value; q('[data-name-field-text]').dispatchEvent(new window.Event('input', { bubbles: true })); };
    const sent = [];
    q('form').addEventListener('submit', (event) => {
        event.preventDefault();
        sent.push({ action: new URL(q('[data-name-field-confirm]').formAction, 'http://localhost').search, personId: q('select').value, displayName: q('[name="displayName"]').value });
    });
    const confirm = () => q('[data-name-field-confirm]').click();
    return { window, q, type, confirm, sent, items: () => [...q('[data-name-field-list]').children].map(item => item.textContent) };
}

test('with script, one field replaces the two plain controls', () => {
    const { window, q, type, items } = createForm();
    assert.equal(q('[data-name-field-ui]').hidden, false);
    assert.deepEqual([...window.document.querySelectorAll('[data-name-field-fallback]')].map(block => block.hidden), [true, true]);

    type('a');
    assert.deepEqual(items(), ['Asha Verma', 'Vikram Nair', 'New person: a'], 'known people that match, then the new name');
    assert.equal(q('[data-name-field-list] img').getAttribute('src'), '/p/a');
    type('asha verma');
    assert.deepEqual(items(), ['Asha Verma'], 'a known name is not also offered as new');
    window.close();
});

test('a known name goes to the assign handler, any other to the create handler, and nothing is sent empty', async () => {
    const { window, q, type, confirm, sent } = createForm();
    confirm();
    assert.deepEqual(sent, [], 'an empty field is not sent');
    // The "type a name" message is cleared a moment later, as in a browser.
    await new Promise(resolve => window.setTimeout(resolve, 5));

    type('  vikram nair ');
    confirm();
    type('Lt Col Meera Pillai');
    confirm();
    assert.deepEqual(sent, [
        { action: '?handler=AssignGroup', personId: 'b', displayName: '' },
        { action: '?handler=CreateGroup', personId: '', displayName: 'Lt Col Meera Pillai' }
    ]);

    // A person chosen elsewhere on the card (the select is set by a suggestion's button) shows in the field.
    q('select').value = 'a';
    q('select').dispatchEvent(new window.Event('change', { bubbles: true }));
    assert.equal(q('[data-name-field-text]').value, 'Asha Verma');
    window.close();
});

test('arrow keys move through the list and Enter picks the highlighted name', () => {
    const { window, q, type } = createForm();
    const box = q('[data-name-field-text]');
    const press = (key) => { const event = new window.KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }); box.dispatchEvent(event); return event; };
    type('v');
    press('ArrowDown');
    assert.equal(q('[data-name-field-list] .is-active').textContent, 'Vikram Nair');
    assert.equal(press('Enter').defaultPrevented, true, 'Enter picks, it does not send the form');
    assert.equal(box.value, 'Vikram Nair');
    assert.equal(q('[data-name-field-list]').hidden, true);
    assert.equal(press('Enter').defaultPrevented, false, 'with the list closed Enter confirms as usual');
    window.close();
});

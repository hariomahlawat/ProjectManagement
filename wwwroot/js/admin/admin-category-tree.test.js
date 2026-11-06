const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(path.join(__dirname, 'admin-category-tree.js'), 'utf8');

// Mirrors Areas/Admin/Pages/Shared/_AdminCategoryDirectory.cshtml: one row per category with its
// id and parent id, a toggle on rows that have children, and the Expand all / Collapse all buttons.
function row(id, parentId, hasToggle = true) {
  return `<tr data-category-row data-category-id="${id}" data-parent-id="${parentId}">
    <td><div class="admin-category-cell__copy"><strong>Category ${id}</strong></div>
    ${hasToggle ? '<button type="button" data-admin-tree-toggle aria-expanded="true"><i class="bi bi-chevron-down"></i></button>' : ''}
    </td></tr>`;
}

function createDom(rows, stored) {
  const dom = new JSDOM(`<!doctype html><html><body>
    <section data-admin-category-tree>
      <input name="q" value="">
      <button data-admin-tree-expand>Expand all</button>
      <button data-admin-tree-collapse>Collapse all</button>
      <table><tbody>${rows.join('')}</tbody></table>
    </section></body></html>`, {
    runScripts: 'outside-only',
    url: 'https://prism.local/Admin/Categories'
  });
  if (stored) {
    dom.window.localStorage.setItem('prism.admin.category-tree./Admin/Categories', JSON.stringify(stored));
  }
  dom.window.eval(script);
  return dom.window;
}

const rowById = (window, id) => window.document.querySelector(`[data-category-id="${id}"]`);

test('a category that is its own parent collapses without hanging and stays visible', () => {
  const window = createDom([row('1', ''), row('2', '1'), row('3', '3')]);

  rowById(window, '3').querySelector('[data-admin-tree-toggle]').click();
  assert.equal(rowById(window, '3').hidden, false);

  window.document.querySelector('[data-admin-tree-collapse]').click();
  assert.equal(rowById(window, '2').hidden, true);
  assert.equal(rowById(window, '3').hidden, false);
});

test('a two-category cycle collapses without hanging', () => {
  const window = createDom([row('10', '11'), row('11', '10')]);

  rowById(window, '10').querySelector('[data-admin-tree-toggle]').click();
  assert.equal(rowById(window, '10').hidden, false);
  assert.equal(rowById(window, '11').hidden, true);

  window.document.querySelector('[data-admin-tree-collapse]').click();
  window.document.querySelector('[data-admin-tree-expand]').click();
  assert.equal(rowById(window, '10').hidden, false);
  assert.equal(rowById(window, '11').hidden, false);
});

test('a saved collapsed cycle is re-applied on load without hanging', () => {
  const window = createDom([row('10', '11'), row('11', '10'), row('12', '12')], ['10', '11', '12']);

  assert.equal(rowById(window, '12').hidden, false);
  // Both rows of the cycle are collapsed, so each hides the other.
  assert.equal(rowById(window, '10').hidden, true);
  assert.equal(rowById(window, '11').hidden, true);
});

test('collapsing a normal parent still hides every descendant', () => {
  const window = createDom([row('1', ''), row('2', '1'), row('3', '2', false), row('4', '')]);

  rowById(window, '1').querySelector('[data-admin-tree-toggle]').click();
  assert.equal(rowById(window, '2').hidden, true);
  assert.equal(rowById(window, '3').hidden, true);
  assert.equal(rowById(window, '4').hidden, false);
});

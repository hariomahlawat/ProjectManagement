const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { findDead, fix } = require('./css-dead-declarations');

const deadProps = (css) => findDead(css).dead.map(({ rule, decl }) => `${rule.selectors.join(', ')} ${decl.prop}`);

test('a later rule with the same selector kills the earlier declaration', () => {
  assert.deepEqual(deadProps('.a { color: red; padding: 1px; } .b { color: blue; } .a { color: green; }'), ['.a color']);
});

test('importance, media scope and selector lists are respected', () => {
  // A later normal declaration cannot override an earlier !important one.
  assert.deepEqual(deadProps('.a { color: red !important; } .a { color: blue; }'), []);
  // A later media rule only applies sometimes, so the unconditional one survives.
  assert.deepEqual(deadProps('.a { color: red; } @media (max-width: 600px) { .a { color: blue; } }'), []);
  // A later unconditional rule overrides the media one in every state.
  assert.deepEqual(deadProps('@media (max-width: 600px) { .a { color: blue; } } .a { color: red; }'), ['.a color']);
  // A selector list is dead only when every selector in it is overridden.
  assert.deepEqual(deadProps('.a, .b { color: red; } .a { color: blue; }'), []);
});

test('a later shorthand kills earlier longhands, not the reverse', () => {
  assert.deepEqual(deadProps('.a { padding-top: 1px; } .a { padding: 0; }'), ['.a padding-top']);
  assert.deepEqual(deadProps('.a { padding: 0; } .a { padding-top: 1px; }'), []);
});

test('fix removes dead declarations and only folds rules with nothing competing in between', () => {
  const css = '.a {\n    color: red;\n    margin-top: 0;\n}\n\n.b {\n    margin: 0;\n}\n\n.a {\n    padding: 1px;\n}\n\n.c {\n    color: blue;\n}\n\n.a {\n    color: green;\n}\n';
  const { out, removed, folded } = fix(css);
  assert.equal(removed, 1);
  assert.equal(folded, 1);
  // padding folds into the first .a (nothing in between sets padding); the last .a stays
  // after .c because .c also sets colour.
  assert.equal(out, '.a {\n    margin-top: 0;\n    padding: 1px;\n}\n\n.b {\n    margin: 0;\n}\n\n.c {\n    color: blue;\n}\n\n.a {\n    color: green;\n}\n');
});

test('notebook.css has no dead declarations', () => {
  const css = fs.readFileSync(path.join(__dirname, '..', 'wwwroot', 'css', 'notebook.css'), 'utf8').replace(/\r\n/g, '\n');
  const dead = deadProps(css);
  assert.deepEqual(dead, [], 'Run: node tools/css-dead-declarations.js wwwroot/css/notebook.css --fix');
});

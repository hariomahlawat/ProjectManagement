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

test('shorthands only cover their actual longhands', () => {
  assert.deepEqual(deadProps('.a { flex-direction: column; } .a { flex: 1; }'), []);
  assert.deepEqual(deadProps('.a { outline-offset: 2px; } .a { outline: 0; }'), []);
  assert.deepEqual(deadProps('.a { border-top-left-radius: 4px; } .a { border-top: 0; }'), []);
  assert.deepEqual(deadProps('.a { flex-grow: 2; } .a { flex: 1; }'), ['.a flex-grow']);
  assert.deepEqual(deadProps('.a { border-top-color: red; } .a { border: 0; }'), ['.a border-top-color']);
  assert.deepEqual(deadProps('.a { color: red; } .a { all: unset; }'), ['.a color']);
});

test('fix never moves a declaration past a rule it interacts with through a shorthand', () => {
  // columns resets column-width, so moving it above .b would change an element with both classes.
  const css = '.a { color: red; }\n.b { column-width: 100px; }\n.a { columns: 3; }\n';
  assert.equal(fix(css).folded, 0);
  const all = '.a { color: red; }\n.b { padding: 1px; }\n.a { all: unset; }\n';
  assert.equal(fix(all).folded, 0);
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

test('@supports is a condition like @media, and nested conditions combine', () => {
  // A later @supports rule applies only where supported, so the unconditional one survives.
  assert.deepEqual(deadProps('.a { color: red; } @supports (display: grid) { .a { color: blue; } }'), []);
  // A later unconditional rule overrides the @supports one wherever it applies.
  assert.deepEqual(deadProps('@supports (display: grid) { .a { color: blue; } } .a { color: red; }'), ['.a color']);
  // The same condition twice: the later one wins.
  assert.deepEqual(deadProps('@supports (gap: 1px) { .a { gap: 1px; } } @supports (gap: 1px) { .a { gap: 2px; } }'), ['.a gap']);
  // Different conditions, or the same condition at another media scope, do not interact.
  assert.deepEqual(deadProps('@supports (gap: 1px) { .a { gap: 1px; } } @supports not (gap: 1px) { .a { gap: 2px; } }'), []);
  assert.deepEqual(deadProps('@media (max-width: 600px) { @supports (gap: 1px) { .a { gap: 1px; } } } @supports (gap: 1px) { .a { gap: 2px; } }'), []);
  assert.deepEqual(deadProps('@media (max-width: 600px) { @supports (gap: 1px) { .a { gap: 1px; } } } .a { gap: 2px; }'), ['.a gap']);
});

test('@keyframes steps are not style rules', () => {
  assert.deepEqual(deadProps('@keyframes spin { from { opacity: 0; } to { opacity: 1; } } .a { opacity: 1; } @keyframes spin { from { opacity: 0; } }'), []);
  // fix leaves keyframes exactly as they are.
  const css = '@keyframes spin {\n    from { opacity: 0; }\n    to { opacity: 1; }\n}\n\n.a {\n    color: red;\n}\n';
  assert.equal(fix(css).out, css);
});

test('fix keeps a condition group that holds @keyframes', () => {
  // The group has no style rules, so every rule in it is trivially dead; it still must not go.
  const css = '@media (prefers-reduced-motion: no-preference) {\n    @keyframes spin {\n        from { opacity: 0; }\n        to { opacity: 1; }\n    }\n}\n\n.a {\n    animation: spin 1s;\n}\n';
  assert.equal(fix(css).out, css);
  const supports = '@supports (animation-name: spin) {\n    @keyframes spin {\n        from { opacity: 0; }\n    }\n\n    .a {\n        color: red;\n    }\n}\n\n.a {\n    color: blue;\n}\n';
  // The dead colour goes, the keyframes and their group stay.
  assert.equal(fix(supports).out, '@supports (animation-name: spin) {\n    @keyframes spin {\n        from { opacity: 0; }\n    }\n}\n\n.a {\n    color: blue;\n}\n');
});

test('navigation-shell.css has no dead declarations', () => {
  const css = fs.readFileSync(path.join(__dirname, '..', 'wwwroot', 'css', 'navigation-shell.css'), 'utf8').replace(/\r\n/g, '\n');
  assert.deepEqual(deadProps(css), [], 'Run: node tools/css-dead-declarations.js wwwroot/css/navigation-shell.css');
});

test('notebook.css has no dead declarations', () => {
  const css = fs.readFileSync(path.join(__dirname, '..', 'wwwroot', 'css', 'notebook.css'), 'utf8').replace(/\r\n/g, '\n');
  const dead = deadProps(css);
  assert.deepEqual(dead, [], 'Run: node tools/css-dead-declarations.js wwwroot/css/notebook.css --fix');
});

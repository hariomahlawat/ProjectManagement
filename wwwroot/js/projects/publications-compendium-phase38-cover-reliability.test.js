const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = (...parts) => fs.readFileSync(path.join(root, ...parts), 'utf8');

test('main Compendium round-trips publication theme and cover background', () => {
  const source = read('wwwroot', 'js', 'pages', 'projects-compendium.js');
  assert.match(source, /publicationTheme:\s*String\(coverDesignSeed\?\.publicationTheme \|\| "InstitutionalGreen"\)/);
  assert.match(source, /backgroundTreatment:\s*String\(coverDesignSeed\?\.backgroundTreatment \|\| "Solid"\)/);
  assert.match(source, /\.\.\.\(coverDesignSeed && typeof coverDesignSeed === "object" \? coverDesignSeed : \{\}\)/);
  assert.match(source, /coverDesignInput\.value = JSON\.stringify\(coverDesignState\)/);
});

test('cover editor dirty signature excludes preview-only image metadata', () => {
  const source = read('wwwroot', 'js', 'pages', 'projects-compendium-cover-editor.js');
  assert.match(source, /function buildCoverSavePayload\(\)/);
  assert.match(source, /const \{ previewUrl, sourceWidth, sourceHeight, \.\.\.persisted \} = item/);
  assert.match(source, /const persistedSignature = \(\) => JSON\.stringify/);
  assert.doesNotMatch(source, /const initialSignature = \(\) => JSON\.stringify\(\{ design: state\.design/);
});

test('automatic cover resolution comes from the shared server candidate contract', () => {
  const editor = read('wwwroot', 'js', 'pages', 'projects-compendium-cover-editor.js');
  assert.match(editor, /automaticCandidates:\s*normaliseAutomaticCandidates\(boot\.automaticCandidates/);
  assert.match(editor, /automaticCandidatesUrl/);
  assert.match(editor, /async function ensureAutomaticCandidates\(\)/);
  assert.match(editor, /function automaticCandidateSequence\(/);
  assert.doesNotMatch(editor, /function chooseAutomaticCandidate\(/);
});

test('required slot and proof parity safeguards are present', () => {
  const editor = read('wwwroot', 'js', 'pages', 'projects-compendium-cover-editor.js');
  const css = read('wwwroot', 'css', 'pages', 'projects-publications.css');
  assert.match(editor, /strictRequiredSlots\(state\.activeSurface\)/);
  assert.match(editor, /This image slot is required by the selected cover template/);
  assert.match(css, /background:\s*var\(--cover-theme-secondary/);
  assert.match(css, /data-template="FullBleedHero"[^}]*cover-proof-image--full/s);
  assert.match(css, /data-template="InstitutionalHero"[^}]*gap:\s*12px/s);
  assert.match(css, /--cover-editor-viewport-top/);
});

test('cover identity typography adapts without truncating formal wording', () => {
  const editor = read('wwwroot', 'js', 'pages', 'projects-compendium-cover-editor.js');
  assert.match(editor, /function resolveTitleSize\(/);
  assert.match(editor, /function resolveSubtitleSize\(/);
  assert.match(editor, /Dense cover wording/);
  assert.doesNotMatch(editor, /substring\([^\n]*cover-proof-identity/);
});


test('dense cover identity receives a server preflight warning as well as adaptive typography', () => {
  const page = read('Pages', 'Projects', 'Publications', 'Compendium', 'Index.cshtml.cs');
  assert.match(page, /CompendiumCoverTypographyPolicy\.NeedsAdvisory/);
  assert.match(page, /"coverIdentityDense"/);
  assert.match(page, /inspect the Front and Back proofs before final issue/);
});

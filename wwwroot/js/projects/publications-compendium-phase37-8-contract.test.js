const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const landing = read('Pages/Projects/Publications/Index.cshtml');
const compendium = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const coverJs = read('wwwroot/js/pages/projects-compendium-cover-editor.js');
const structureView = read('Pages/Projects/Publications/Compendium/Structure.cshtml');
const coverView = read('Pages/Projects/Publications/Compendium/Cover.cshtml');
const structureJs = read('wwwroot/js/pages/projects-compendium-structure-editor.js');
const css = read('wwwroot/css/pages/projects-publications.css');

test('phase 37.8 makes the mature Compendium capability set discoverable from Publications', () => {
  assert.match(landing, /View capabilities/);
  assert.match(landing, /asp-route-guide="1"/);
  assert.match(landing, /cover layouts, themes, patterns and publication imagery/i);
  assert.match(landing, /project briefs, particulars, specifications and publication notes/i);
  assert.match(landing, /verify the PDF/i);
  assert.match(css, /publication-choice__capabilities/);
});

test('phase 37.8 adds an on-demand capability guide instead of a permanent workspace feature panel', () => {
  assert.match(compendium, /id="compendiumGuide"/);
  assert.match(compendium, /What this workspace can do/);
  for (const heading of ['Build the publication', 'Compose the dossiers', 'Design with controlled flexibility', 'Review before issue', 'Publish and reuse']) {
    assert.match(compendium, new RegExp(heading));
  }
  assert.match(compendium, /Controlled publication system/);
  assert.match(compendium, /data-bs-toggle="offcanvas"/);
  assert.match(css, /compendium-guide__workflow/);
  assert.match(css, /compendium-guide__map/);
});

test('phase 37.8 opens the guide from landing deep-link and then cleans the one-shot query flag', () => {
  assert.match(compendium, /data-compendium-guide-open/);
  assert.match(mainJs, /data-compendium-guide-open="true"/);
  assert.match(mainJs, /bootstrap\.Offcanvas\.getOrCreateInstance\(guideNode\)\.show\(\)/);
  assert.match(mainJs, /url\.searchParams\.delete\("guide"\)/);
  assert.match(mainJs, /history\.replaceState/);
});

test('phase 37.8 suppresses the browser beforeunload prompt for intentional Cover Editor navigation', () => {
  assert.match(coverJs, /navigatingAway:\s*false/);
  assert.match(coverJs, /state\.navigatingAway\s*=\s*true/);
  assert.match(coverJs, /if \(state\.navigatingAway \|\| !state\.dirty\) return/);
  assert.match(coverJs, /data-cover-return-unsaved[\s\S]*modal\('compendiumCoverLeaveModal'\)\?\.hide\(\)[\s\S]*goBack\(\)/);
  assert.match(coverJs, /data-cover-save-return[\s\S]*if \(await save\(\)\)[\s\S]*goBack\(\)/);
});

test('phase 37.8 gives Cover and Structure return actions accurate non-overlapping semantics', () => {
  assert.match(coverView, /Discard changes and return/);
  assert.match(coverView, /discard the unsaved cover changes/);
  assert.match(structureView, /Keep changes and return/);
  assert.match(structureView, /changes kept locally/);
  assert.match(structureJs, /let navigatingAway = false/);
  assert.match(structureJs, /navigatingAway = true/);
  assert.match(structureJs, /if \(navigatingAway \|\| !isDirty\(\)\) return/);
});

test('phase 37.8 keeps guide controls compact and accessible on narrower screens', () => {
  assert.match(compendium, /aria-controls="compendiumGuide"/);
  assert.match(compendium, /aria-labelledby="compendiumGuideLabel"/);
  assert.match(compendium, /aria-label="Close guide"/);
  assert.match(css, /@media \(max-width: 720px\)[\s\S]*compendium-guide-trigger span/);
});

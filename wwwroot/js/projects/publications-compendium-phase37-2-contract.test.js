const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const editorial = read('Services/Compendiums/CompendiumDossierEditorialPolicy.cs');
const pagination = read('Services/Compendiums/CompendiumDossierPaginationPlanner.cs');
const flow = read('Services/Compendiums/CompendiumDossierNarrativeFlowPlanner.cs');
const readiness = read('Services/Compendiums/CompendiumReadinessPolicy.cs');
const readService = read('Services/Compendiums/CompendiumReadService.cs');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');
const indexPage = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const indexModel = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const css = read('wwwroot/css/pages/projects-publications.css');

test('phase 37.2 separates physical fit from editorial validity', () => {
  assert.match(editorial, /MinimumEditorialFillHeightPoints/);
  assert.match(editorial, /AssessSideColumn/);
  assert.match(editorial, /OverflowHeightPoints/);
  assert.match(editorial, /UnderfillHeightPoints/);
  assert.match(pagination, /IsEditoriallyValid/);
  assert.match(pagination, /editorialCandidates/);
  assert.match(pagination, /selectableCandidates/);
});

test('phase 37.2 treats small Fill frames as invalid normal editorial candidates', () => {
  assert.match(editorial, /VisualHero\s*=>\s*185f/);
  assert.match(editorial, /MultiImageEditorial\s*=>\s*185f/);
  assert.match(editorial, /_\s*=>\s*165f/);
  assert.doesNotMatch(pagination, /Balanced[^\n]*96f/);
  assert.match(pagination, /protectPrintFidelity:\s*!explicitLayout/);
});

test('phase 37.2 measures both side-column overflow and underfill', () => {
  assert.match(flow, /SideOverflowHeightPoints/);
  assert.match(flow, /SideBalanceRatio/);
  assert.match(pagination, /SideOverflowHeightPoints/);
  assert.match(pagination, /SideRemainingHeightPoints/);
  assert.match(pagination, /CompendiumBalancedTextFlowMode\.SideColumn/);
});

test('phase 37.2 surfaces manual composition imbalance as a publication warning', () => {
  assert.match(readiness, /dossierCompositionImbalance/);
  assert.match(readService, /DossierEditorialWarning\s*=\s*paginationDecision\.EditorialWarning/);
  assert.match(indexModel, /review\.DossierEditorialWarning/);
  assert.match(mainJs, /dossierEditorialWarning/);
  assert.match(mainJs, /Layout needs editorial attention/);
  assert.match(css, /compendium-dossier-fit-insight\.is-warning/);
});

test('phase 37.2 hardens duplicate narrative preflight', () => {
  assert.match(readiness, /ContainsRepeatedLongWordBlock/);
  assert.match(readiness, /matches \/ \(double\)compared >= \.94d/);
  assert.match(readiness, /duplicateNarrativeParagraph/);
});

test('phase 37.2 restores compact four-column publication composition on desktop', () => {
  assert.match(indexPage, />Project narrative</);
  assert.match(indexPage, />Narrative alignment</);
  assert.match(indexPage, />Grouping</);
  assert.match(indexPage, />Order</);
  assert.match(css, /grid-template-columns:\s*1\.12fr 1fr \.9fr \.9fr/);
  assert.match(css, /@media \(max-width: 1320px\)/);
});

test('phase 37.2 invalidates old review/render identity without a schema migration', () => {
  assert.match(fingerprint, /compendium-review-v(?:14-editorial-constraints|15-additional-note-final-hardening)/);
  assert.match(readService, /CompendiumPdf_2026-08-16_(?:editorial-constraints-v21|final-editorial-v22)/);
});

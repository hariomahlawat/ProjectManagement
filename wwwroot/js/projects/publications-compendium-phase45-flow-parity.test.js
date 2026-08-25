const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const css = read('wwwroot/css/pages/projects-publications.css');
const readService = read('Services/Compendiums/CompendiumReadService.cs');
const dto = read('Services/Compendiums/CompendiumDtos.cs');
const indexPage = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const pagination = read('Services/Compendiums/CompendiumDossierPaginationPlanner.cs');
const editorial = read('Services/Compendiums/CompendiumDossierEditorialPolicy.cs');
const buildIdentity = read('Utilities/Reporting/CompendiumBuildIdentity.cs');

const compact = value => value.replace(/\s+/g, ' ');

test('phase 45 makes Live Page narrative typography scale from authoritative A4 points', () => {
  assert.match(mainJs, /CompendiumLiveProofPageWidthPoints\s*=\s*595\.28/);
  assert.match(mainJs, /updateLiveProofScale/);
  assert.match(mainJs, /ResizeObserver/);
  assert.match(mainJs, /--proof-scale/);
  assert.match(css, /--proof-scale\s*:\s*1/);
  assert.match(css, /font-size:\s*calc\(10px\s*\*\s*var\(--narrative-scale,\s*1\)\s*\*\s*var\(--proof-scale,\s*1\)\)/);
  assert.match(css, /line-height:\s*1\.25/);
});

test('phase 45 uses actual rendered Fit height for flow, proof geometry and diagnostics', () => {
  assert.match(dto, /DossierPrimaryImageRenderedHeightPoints/);
  assert.match(readService, /ResolveRenderedPrimaryImageHeightPoints/);
  assert.match(readService, /CompendiumDossierImageGeometryPolicy\.Resolve/);
  assert.match(readService, /paginationDecision\.PrimaryImageHeightPoints,[\s\S]*selectedProbe\?\.Width,[\s\S]*selectedProbe\?\.Height,[\s\S]*resolved\.Selection\.ImageFitMode/);
  assert.match(indexPage, /review\.DossierPrimaryImageRenderedHeightPoints/);
  assert.match(mainJs, /dossierPrimaryImageRenderedHeightPoints/);
});

test('phase 45 optimises Flow Below around a preferred semantic gap before excessive-gap fallback', () => {
  assert.match(editorial, /PreferredFlowBelowGapPoints\s*=\s*18f/);
  assert.match(pagination, /FlowBelowGapScore/);
  assert.match(editorial, /PreferredFlowBelowGapPoints/);
  assert.match(pagination, /candidate\.SideRemainingHeightPoints/);
  assert.match(pagination, /BalancedFlowImageHeights/);
  assert.match(pagination, /Enumerable\.Range/);
});

test('phase 45 advances build identity for flow-proof parity', () => {
  assert.match(buildIdentity, /Phase\s*=\s*"45"/);
  assert.match(buildIdentity, /physical-a4-v45/);
  assert.match(compact(buildIdentity), /flow-proof-parity/i);
});

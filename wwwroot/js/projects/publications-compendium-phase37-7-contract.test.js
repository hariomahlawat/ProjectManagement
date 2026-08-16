const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const dtos = read('Services/Compendiums/CompendiumDtos.cs');
const policy = read('Services/Compendiums/CompendiumCoverIdentityPolicy.cs');
const model = read('Models/Publications/CompendiumPreset.cs');
const presetService = read('Services/Publications/CompendiumPresetService.cs');
const contracts = read('Services/Publications/CompendiumPresetContracts.cs');
const db = read('Data/ApplicationDbContext.cs');
const migration = read('Migrations/20261216160000_AddCompendiumCoverIdentity.cs');
const manifest = read('Migrations/immutable-migration-ids.txt');
const coverModel = read('Pages/Projects/Publications/Compendium/Cover.cshtml.cs');
const indexModel = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const coverView = read('Pages/Projects/Publications/Compendium/Cover.cshtml');
const coverJs = read('wwwroot/js/pages/projects-compendium-cover-editor.js');
const css = read('wwwroot/css/pages/projects-publications.css');
const exportService = read('Services/Compendiums/CompendiumExportService.cs');
const pdf = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');
const readService = read('Services/Compendiums/CompendiumReadService.cs');

 test('phase 37.7 defines six curated themes and six controlled background treatments', () => {
  for (const value of ['InstitutionalGreen', 'DeepNavy', 'Burgundy', 'Graphite', 'DeepTeal', 'Slate']) {
    assert.match(dtos, new RegExp(`CompendiumPublicationTheme[\\s\\S]*${value}`));
  }
  for (const value of ['Solid', 'SubtleGradient', 'TopographicContours', 'TechnicalGrid', 'GeometricMesh', 'Camouflage']) {
    assert.match(dtos, new RegExp(`CompendiumCoverBackgroundTreatment[\\s\\S]*${value}`));
  }
  assert.match(policy, /public const string Gold\s*=\s*"#C9A646"/);
});

test('phase 37.7 keeps camouflage curated rather than universally available', () => {
  assert.match(policy, /SupportsCamouflage/);
  assert.match(policy, /Burgundy[\s\S]*false/);
  assert.match(policy, /DeepTeal[\s\S]*false/);
  assert.match(policy, /treatment != CompendiumCoverBackgroundTreatment\.Camouflage/);
  assert.match(coverJs, /backgroundAllowed\(/);
  assert.match(coverJs, /button\.disabled = !allowed/);
});

test('phase 37.7 persists cover identity through schema v12 with legacy Green and Solid defaults', () => {
  assert.match(model, /SettingsSchemaVersion\s*\{\s*get;\s*set;\s*\}\s*=\s*12/);
  assert.match(model, /PublicationTheme[\s\S]*InstitutionalGreen/);
  assert.match(model, /CoverBackgroundTreatment[\s\S]*Solid/);
  assert.match(presetService, /CurrentSchemaVersion\s*=\s*12/);
  assert.match(presetService, /SettingsSchemaVersion\s*<\s*12[\s\S]*InstitutionalGreen/);
  assert.match(presetService, /SettingsSchemaVersion\s*<\s*12[\s\S]*Solid/);
  assert.match(contracts, /CompendiumPublicationTheme PublicationTheme/);
  assert.match(contracts, /CompendiumCoverBackgroundTreatment BackgroundTreatment/);
  assert.match(db, /PublicationTheme\)\.HasMaxLength\(32\)\.HasDefaultValue\("InstitutionalGreen"\)/);
  assert.match(db, /CoverBackgroundTreatment\)\.HasMaxLength\(32\)\.HasDefaultValue\("Solid"\)/);
  assert.match(migration, /AddColumn<string>[\s\S]*PublicationTheme/);
  assert.match(migration, /AddColumn<string>[\s\S]*CoverBackgroundTreatment/);
  assert.match(migration, /SET "SettingsSchemaVersion" = 12/);
  assert.match(manifest, /20261216160000_AddCompendiumCoverIdentity/);
});

test('phase 37.7 main workspace and Cover Editor both round-trip theme and treatment', () => {
  assert.match(coverModel, /publicationTheme = design\.PublicationTheme\.ToString\(\)/);
  assert.match(coverModel, /backgroundTreatment = design\.BackgroundTreatment\.ToString\(\)/);
  assert.match(coverModel, /PublicationTheme = publicationTheme/);
  assert.match(coverModel, /BackgroundTreatment = backgroundTreatment/);
  assert.match(indexModel, /PublicationTheme = design\.PublicationTheme/);
  assert.match(indexModel, /BackgroundTreatment = design\.BackgroundTreatment/);
  assert.match(indexModel, /PublicationTheme = design\.PublicationTheme\.ToString\(\)/);
  assert.match(indexModel, /BackgroundTreatment = design\.BackgroundTreatment\.ToString\(\)/);
  assert.match(exportService, /PublicationTheme = configured\.PublicationTheme/);
  assert.match(exportService, /BackgroundTreatment = configured\.BackgroundTreatment/);
});

test('phase 37.7 Cover Editor exposes compact publication-wide theme and background controls', () => {
  assert.match(coverView, /Publication identity/);
  assert.match(coverView, /data-cover-theme="InstitutionalGreen"/);
  assert.match(coverView, /data-cover-theme="DeepNavy"/);
  assert.match(coverView, /data-cover-theme="Burgundy"/);
  assert.match(coverView, /data-cover-theme="Graphite"/);
  assert.match(coverView, /data-cover-theme="DeepTeal"/);
  assert.match(coverView, /data-cover-theme="Slate"/);
  assert.match(coverView, /data-cover-background="TopographicContours"/);
  assert.match(coverView, /data-cover-background="TechnicalGrid"/);
  assert.match(coverView, /data-cover-background="GeometricMesh"/);
  assert.match(coverView, /data-cover-background="Camouflage"/);
  assert.match(css, /compendium-cover-theme-grid/);
  assert.match(css, /compendium-cover-background-grid/);
});

test('phase 37.7 uses one deterministic server SVG policy in browser proof and QuestPDF', () => {
  assert.match(coverModel, /CompendiumCoverIdentityPolicy\.BuildSurfaceSvg/);
  assert.match(coverModel, /patternUrl/);
  assert.match(coverJs, /data-cover-proof-pattern/);
  assert.match(coverJs, /url\.searchParams\.set\('theme'/);
  assert.match(coverJs, /url\.searchParams\.set\('treatment'/);
  assert.match(policy, /viewBox=\\"0 0 1000 1000\\"/);
  assert.match(pdf, /CompendiumCoverIdentityPolicy\.BuildSurfaceSvg/);
  assert.match(pdf, /ComposeThemedCoverRegion/);
  assert.match(pdf, /ResolveEffectiveTreatment/);
});

test('phase 37.7 Clean Back starts clean without continuously enforcing destructive defaults', () => {
  assert.match(coverJs, /previous !== 'Clean' && next === 'Clean'/);
  assert.match(coverJs, /showBackTitle: false/);
  assert.match(coverJs, /showBackSubtitle: false/);
  assert.match(coverJs, /showBackEdition: false/);
  assert.match(coverJs, /state\.cleanBackVisibility = captureBackVisibility\(\)/);
  assert.match(coverJs, /applyBackVisibility\(state\.cleanBackVisibility\)/);
  assert.match(coverJs, /surface === 'back' && state\.design\.backTemplate === 'Clean'\) return 'Solid'/);
  assert.match(policy, /backTemplate == CompendiumBackCoverTemplate\.Clean[\s\S]*Solid/);
});

test('phase 37.7 normalizes template names and advances cover render identity', () => {
  for (const name of ['Institutional Hero', 'Full-Bleed Hero', 'Editorial Split', 'Portfolio Triptych', 'Portfolio Quartet', 'Minimal Institutional', 'Image Echo', 'Portfolio Strip', 'Typography Only', 'Clean Back']) {
    assert.match(coverView, new RegExp(name.replace(/[.*+?^${}()|[\\]\\]/g, '\\$&')));
  }
  assert.match(fingerprint, /compendium-review-v19-cover-identity/);
  assert.match(readService, /CompendiumPdf_2026-08-16_cover-identity-v26/);
  assert.match(presetService, /CurrentSchemaVersion\s*=\s*12/);
});

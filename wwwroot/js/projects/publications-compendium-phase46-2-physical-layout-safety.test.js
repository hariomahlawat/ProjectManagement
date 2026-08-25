const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = relativePath => fs.readFileSync(path.join(root, relativePath), 'utf8');

const metrics = read('Utilities/Reporting/CompendiumLayoutMetrics.cs');
const typography = read('Services/Compendiums/CompendiumNarrativeTypographyPolicy.cs');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const identity = read('Utilities/Reporting/CompendiumBuildIdentity.cs');
const csharpContract = read('ProjectManagement.Tests/Publications/CompendiumPhase46_2PhysicalLayoutSafetyTests.cs');

test('phase 46.2 derives the physical shaping reserve from the maximum body line plus native tolerance', () => {
  assert.match(metrics, /ProjectBodyMaximumNarrativeScale\s*=\s*1\.10f/);
  assert.match(metrics, /ProjectBodyLineHeightMultiplier\s*=\s*1\.25f/);
  assert.match(metrics, /MaximumProjectBodyLineHeightPoints\s*=\s*[\s\S]*ProjectBodyFontSize[\s\S]*ProjectBodyMaximumNarrativeScale[\s\S]*ProjectBodyLineHeightMultiplier/);
  assert.match(metrics, /PhysicalPaginationNativeShapingTolerancePoints\s*=\s*([0-9.]+)f/);
  const tolerance = Number(metrics.match(/PhysicalPaginationNativeShapingTolerancePoints\s*=\s*([0-9.]+)f/)[1]);
  assert.ok(tolerance >= 2, 'native shaping tolerance must be at least 2 points');
  assert.match(metrics, /PhysicalPaginationReservePoints\s*=\s*MaximumProjectBodyLineHeightPoints\s*\+\s*PhysicalPaginationNativeShapingTolerancePoints/);
});

test('phase 46.2 prevents typography and pagination metrics from drifting apart', () => {
  assert.match(typography, /using ProjectManagement\.Utilities\.Reporting;/);
  assert.match(typography, /MaximumScale\s*=\s*CompendiumLayoutMetrics\.ProjectBodyMaximumNarrativeScale/);
  assert.match(typography, /BodyFontSizePoints\s*=\s*CompendiumLayoutMetrics\.ProjectBodyFontSize/);
  assert.match(typography, /BodyLineHeightMultiplier\s*=\s*CompendiumLayoutMetrics\.ProjectBodyLineHeightMultiplier/);
});

test('phase 46.2 keeps the atomic QuestPDF guard and advances the physical PDF contract', () => {
  assert.match(builder, /ShowEntire\(\)/);
  assert.match(identity, /Phase\s*=\s*"46\.2"/);
  assert.match(identity, /PdfContract\s*=\s*"physical-a4-v46\.2"/);
  assert.match(identity, /phase46\.2-physical-layout-safety/);
});

test('phase 46.2 C# regression contract asserts the derived maximum-line safety invariant', () => {
  assert.match(csharpContract, /MaximumProjectBodyLineHeightPoints/);
  assert.match(csharpContract, /PhysicalPaginationNativeShapingTolerancePoints/);
  assert.match(csharpContract, /physical-a4-v46\.2/);
});

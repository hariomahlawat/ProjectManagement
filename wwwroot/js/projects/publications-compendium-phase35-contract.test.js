const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

const readService = read('Services/Compendiums/CompendiumReadService.cs');
const resolver = read('Services/Compendiums/CompendiumProgrammeInformation.cs');
const dtos = read('Services/Compendiums/CompendiumDtos.cs');
const readiness = read('Services/Compendiums/CompendiumReadinessPolicy.cs');
const fingerprint = read('Services/Compendiums/CompendiumReviewFingerprint.cs');
const exportService = read('Services/Compendiums/CompendiumExportService.cs');
const builder = read('Utilities/Reporting/CompendiumPdfReportBuilder.cs');
const indexView = read('Pages/Projects/Publications/Compendium/Index.cshtml');
const indexPage = read('Pages/Projects/Publications/Compendium/Index.cshtml.cs');
const structurePage = read('Pages/Projects/Publications/Compendium/Structure.cshtml.cs');
const mainJs = read('wwwroot/js/pages/projects-compendium.js');
const css = read('wwwroot/css/pages/projects-publications.css');
const programmeTests = read('ProjectManagement.Tests/Publications/CompendiumProgrammeInformationTests.cs');

const ruleBody = selector => {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = css.match(new RegExp(`${escaped}\\{([^}]*)\\}`));
  assert.ok(match, `missing CSS rule: ${selector}`);
  return match[1];
};

test('phase 35 maps Arms / Services strictly from the authoritative Sponsoring Line Directorate', () => {
  assert.match(readService, /project\.SponsoringLineDirectorate != null \? project\.SponsoringLineDirectorate\.Name : null/);
  assert.match(readService, /var sponsoringLineDirectorate = NormalizeOptional\(project\.SponsoringLineDirectorate\) \?\? string\.Empty;/);
  assert.match(readService, /CompendiumProgrammeInformation\.Resolve\(\s*sponsoringLineDirectorate,/s);
  assert.match(readService, /SponsoringLineDirectorateDisplay = NormalizeDisplay\(project\.SponsoringLineDirectorate, "Not recorded"\)/);
  assert.doesNotMatch(readService, /project\.ArmService\b/);
  assert.doesNotMatch(readService, /\bArmServiceDisplay\b/);

  assert.match(resolver, /string\? sponsoringLineDirectorate/);
  assert.match(resolver, /"Arms \/ Services",\s*cleanSponsoringLineDirectorate,/s);
  assert.doesNotMatch(resolver, /\barmService\b/i);
  assert.match(programmeTests, /Assert\.Equal\("Infantry Directorate", modules\[0\]\.Value\)/);
});

test('phase 35 carries the source meaning through DTOs, review JSON and PDF without legacy aliases', () => {
  for (const source of [dtos, readiness, fingerprint, exportService, builder, indexView, indexPage, structurePage]) {
    assert.doesNotMatch(source, /\bArmService(?:Display)?\b/);
  }

  assert.match(dtos, /bool HasSponsoringLineDirectorate/);
  assert.match(dtos, /string SponsoringLineDirectorateDisplay/);
  assert.match(indexView, /project\.HasSponsoringLineDirectorate/);
  assert.match(indexView, /project\.SponsoringLineDirectorateDisplay/);
  assert.match(indexPage, /review\.SponsoringLineDirectorateDisplay/);
  assert.match(structurePage, /sponsoringLineDirectorate = candidate\.SponsoringLineDirectorateDisplay/);
  assert.match(builder, /project\.SponsoringLineDirectorateDisplay/);
});

test('phase 35 evaluates readiness and review identity against the same authoritative fact', () => {
  assert.match(readiness, /string\? SponsoringLineDirectorate/);
  assert.match(readiness, /context\.SponsoringLineDirectorate/);
  assert.match(readiness, /MissingSponsoringLineDirectorate/);
  assert.match(readiness, /"missingSponsoringLineDirectorate"/);
  assert.match(fingerprint, /string\? SponsoringLineDirectorate/);
  assert.match(fingerprint, /Clean\(input\.SponsoringLineDirectorate\)/);
  assert.match(fingerprint, /compendium-review-v(?:10-sponsoring-line-directorate|11-balanced-text-flow)/);
  assert.match(readService, /CompendiumPdf_2026-08-15_(?:programme-particulars-v17|final-composition-v18)/);
});

test('phase 35 removes nested icon tiles while retaining a stable alignment column', () => {
  const iconRule = ruleBody('.compendium-live-page__programme-icon');
  const imageRule = ruleBody('.compendium-live-page__programme-icon img');
  assert.match(iconRule, /width:22px/);
  assert.match(iconRule, /height:22px/);
  assert.doesNotMatch(iconRule, /border|background|border-radius/);
  assert.match(imageRule, /width:18px/);
  assert.match(imageRule, /height:18px/);

  const composeIcon = builder.slice(
    builder.indexOf('private static void ComposeProgrammeIcon'),
    builder.indexOf('private static void ComposeTechnicalSpecifications'));
  assert.match(composeIcon, /container\.Padding\(2\)\.Element/);
  assert.doesNotMatch(composeIcon, /\.Background\(|\.Border\(|\.BorderColor\(/);
  assert.match(builder, /cell\.ConstantItem\(22\)\.Height\(22\)/);
  assert.match(mainJs, /const programmeIconVersion = "v16"/);
});

test('phase 35 retains coloured local vectors and the programme panel as the sole container', () => {
  assert.match(mainJs, /compendium-live-page__programme-heading">PROJECT PARTICULARS/);
  assert.doesNotMatch(mainJs, /compendium-live-page__programme-heading">PROGRAMME INFORMATION/);
  assert.match(builder, /Text\("PROJECT PARTICULARS"\)/);
  assert.doesNotMatch(builder, /Text\("PROGRAMME INFORMATION"\)/);
  assert.match(css, /\.compendium-live-page__programme\{[^}]*border:1px solid #d8e5df[^}]*border-top:2px solid #205244/s);
  assert.match(builder, /container\.Background\(Forest50\)\.Border\(1\)\.BorderColor\("#D8E5DF"\)/);
  assert.match(builder, /"maroon" => "#8B3A3A"/);
  assert.match(builder, /"green" => "#27825B"/);
  assert.match(builder, /"blue" => "#3275C7"/);
  assert.match(builder, /_ => "#A97712"/);
});

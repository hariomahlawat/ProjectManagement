const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

// Confirm dialogs PR 4 (docs/ux/confirm-dialogs.md): the Office records pages ask in the shared
// PRISM dialog. Visits, Social media, Industry partners and IPR forms confirm through
// data-confirm; scripts with their own questions use askConfirm, whose browser-box fallback is
// the only confirm() allowed.
const jsRoot = path.join(__dirname, '..');
const repoRoot = path.join(jsRoot, '..', '..');
const fallback = "Promise.resolve(window.confirm([options.title, options.message].filter(Boolean).join(' ')))";
const nativeConfirm = /\bwindow\.confirm\s*\(|(^|[^\w.])confirm\s*\(/m;

const scripts = [
  'project-office-reports/ipr/index.js',
  'pages/project-office-reports/visits.js',
  'pages/project-office-reports/social-media.js',
  'pages/project-office-reports/arpp/arpp-manage.js',
  'pages/proliferation-manage.js',
  'pages/industry-partners.js'
];

for (const file of scripts) {
  test(`${file} does not call the browser confirm() directly`, () => {
    const source = fs.readFileSync(path.join(jsRoot, file), 'utf8');
    assert.equal(nativeConfirm.test(source.split(fallback).join('')), false, `${file} still calls confirm() directly`);
  });
}

test('Office records forms confirm through data-confirm, not page-specific attributes', () => {
  const views = [
    'Areas/ProjectOfficeReports/Pages/Ipr/_RecordFormPartial.cshtml',
    'Pages/IndustryPartners/Organisation.cshtml',
    'Pages/IndustryPartners/_PartnerDrawer.cshtml',
    'Pages/IndustryPartners/_Attachments.cshtml',
    'Pages/IndustryPartners/_Contacts.cshtml',
    'Pages/IndustryPartners/_LinkedProjects.cshtml',
    'Areas/ProjectOfficeReports/Pages/Visits/Details.cshtml',
    'Areas/ProjectOfficeReports/Pages/Visits/_PhotosPartial.cshtml',
    'Areas/ProjectOfficeReports/Pages/SocialMedia/_PhotoGallery.cshtml'
  ];
  for (const view of views) {
    const markup = fs.readFileSync(path.join(repoRoot, view), 'utf8');
    assert.match(markup, /data-confirm="/, `${view} asks through data-confirm`);
    assert.doesNotMatch(markup, /data-ipr-confirm|data-confirm-form=|data-page-handled data-confirm/, `${view} still uses a page-specific confirmation`);
  }
});

const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const script = fs.readFileSync(path.resolve(__dirname, 'photos-person-profile.js'), 'utf8');
const page = fs.readFileSync(path.resolve(__dirname, '../../../Pages/Photos/Index.cshtml'), 'utf8');
const card = fs.readFileSync(path.resolve(__dirname, '../../../Pages/Photos/_PersonPhotoCandidateCard.cshtml'), 'utf8');

test('person discovery exposes direct evidence bands and identity-group evidence', () => {
    assert.match(page, /Strong possibilities/);
    assert.match(page, /Moderate possibilities/);
    assert.match(page, /Possible identity groups/);
    assert.match(page, /lower-confidence/);
    assert.match(page, /data-select-person-group/);
    assert.doesNotMatch(card, /checked[^>]*data-person-candidate-select/);
});

test('candidate actions stay explicit and support linked-user wording', () => {
    assert.match(card, /data-person-candidate-select/);
    assert.match(card, /@Model\.ConfirmLabel/);
    assert.match(card, /@Model\.RejectLabel/);
    assert.match(script, /data-select-person-group/);
    assert.match(script, /window\.confirm/);
    assert.match(script, /selfReview/);
    assert.match(script, /X-Requested-With/);
});

test('processing-only discovery remains compact and never claims the queue is clear', () => {
    assert.match(page, /person-photo-discovery__processing/);
    assert.match(page, /Checking for more photos/);
    assert.match(page, /still being compared/);
});

test('single-person profile supports linked PRISM user context', () => {
    assert.match(page, /Linked to your PRISM account/);
    assert.match(page, /PersonDiscoveryPrimaryLabel/);
    assert.match(page, /photos-control--select/);
});

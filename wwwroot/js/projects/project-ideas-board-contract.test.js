const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..', '..', '..');
const read = (...segments) => fs.readFileSync(path.join(root, ...segments), 'utf8');

const boardView = read('Pages', 'ProjectIdeas', 'Index.cshtml');
const boardModel = read('Pages', 'ProjectIdeas', 'Index.cshtml.cs');
const boardCss = read('wwwroot', 'css', 'project-ideas-board.css');
const commonIdeasCss = read('wwwroot', 'css', 'project-ideas.css');

test('ideation table uses natural page scrolling with horizontal overflow only', () => {
    assert.match(boardView, /class="pi-table-scroll"/);
    assert.doesNotMatch(boardView, /pi-table-scrollable/);
    assert.match(
        boardCss,
        /\.pi-index-page \.pi-table-scroll \{[\s\S]*overflow-x:\s*auto !important;[\s\S]*overflow-y:\s*hidden !important;/
    );
    assert.doesNotMatch(commonIdeasCss, /\.pi-table-scrollable/);
});

test('stale state belongs to Last Updated instead of the Idea signal stack', () => {
    const dateCell = boardView.match(/<td class="pi-table-date-cell">([\s\S]*?)<\/td>/)?.[1] ?? '';
    const ideaCell = boardView.match(/<td class="pi-table-idea-cell">([\s\S]*?)<\/td>/)?.[1] ?? '';

    assert.match(dateCell, /pi-table-stale/);
    assert.match(dateCell, /30\+ days idle/);
    assert.doesNotMatch(ideaCell, /No update for 30\+ days/);
});

test('table suppresses duplicate placeholder summary when Needs details is shown', () => {
    assert.match(boardView, /@if \(!needsDetails\)[\s\S]*<p>@IndexModel\.DisplayDescription\(idea\)<\/p>/);
    assert.match(boardView, /pi-signal-warning[\s\S]*Needs details/);
});

test('table activity formatting separates recent relative time from older absolute dates', () => {
    assert.match(boardModel, /DisplayTableActivityPrimary\(DateTime value\)/);
    assert.match(boardModel, /return utcValue\.ToLocalTime\(\)\.ToString\("dd MMM yyyy"\);/);
    assert.match(boardModel, /DisplayTableActivitySecondary\(DateTime value\)/);
    assert.match(boardModel, /\? localValue\.ToString\("dd MMM yyyy, hh:mm tt"\)[\s\S]*: localValue\.ToString\("hh:mm tt"\)/);
});

test('search submit uses an explicit search affordance', () => {
    assert.match(boardView, /class="pi-search-submit"[\s\S]*aria-label="Search ideas"[\s\S]*bi-search/);
    assert.doesNotMatch(boardView, /pi-search-submit[\s\S]{0,220}bi-arrow-right/);
});


test('card view suppresses redundant status and missing-summary placeholder', () => {
    const cardSection = boardView.match(/<section class="pi-card-grid"([\s\S]*?)<\/section>/)?.[1] ?? '';

    assert.doesNotMatch(cardSection, /pi-status-/);
    assert.match(cardSection, /@if \(!needsDetails\)[\s\S]*pi-card-description/);
    assert.match(cardSection, /pi-signal-warning[\s\S]*Needs details/);
});

test('card stale state is integrated into updated metadata', () => {
    const cardSection = boardView.match(/<section class="pi-card-grid"([\s\S]*?)<\/section>/)?.[1] ?? '';

    assert.match(cardSection, /pi-card-updated-row/);
    assert.match(cardSection, /DisplayCardActivity\(lastActivity\)/);
    assert.match(cardSection, /pi-card-stale[\s\S]*30\+ days idle/);
    assert.doesNotMatch(cardSection, /pi-signal-danger[\s\S]*30\+ days idle/);
});

test('card activity uses full date for older records via shared activity rule', () => {
    assert.match(boardModel, /DisplayCardActivity\(DateTime value\)/);
    assert.match(boardModel, /return DisplayTableActivityPrimary\(value\);/);
});

test('card density is compact without reserving empty description height', () => {
    assert.match(boardCss, /\.pi-index-page \.pi-idea-card \{[\s\S]*min-height:\s*220px;/);
    const descriptionRule = boardCss.match(/\.pi-index-page \.pi-card-description \{([\s\S]*?)\}/)?.[1] ?? '';
    assert.doesNotMatch(descriptionRule, /min-height/);
});

// Review people (Pages/Photos/People/Review.cshtml): the page looks like Photos and speaks plainly.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const read = (relative) => fs.readFileSync(path.resolve(__dirname, '../../..', relative), 'utf8');
const page = read('Pages/Photos/People/Review.cshtml');
const model = read('Pages/Photos/People/Review.cshtml.cs');
const script = read('wwwroot/js/pages/photos-people-review.js');

test('the review page has the Photos bar, one plain heading and three queues', () => {
    assert.match(page, /class="photos-bar" data-photos-bar/);
    assert.match(page, /<h1>Who is in these pictures\?<\/h1>/);
    assert.match(page, /ViewData\["Title"\] = "Review people";/);
    const queues = page.slice(page.indexOf('class="photos-chips people-queues"'), page.indexOf('data-workload-strip'));
    ['Same person\?', 'Is this …\?', 'Unnamed faces'].forEach(name => assert.match(queues, new RegExp(name)));
    // The same three addresses as before, and the counts the status script keeps up to date.
    ['"groups", 1, "detail"', '"matches", 1, "detail"', '"unidentified", 1, "triage"'].forEach(url => assert.ok(queues.includes(url), url));
    ['data-workload-groups', 'data-workload-known', 'data-workload-individual'].forEach(hook => assert.ok(queues.includes(hook), hook));
    assert.doesNotMatch(page, /review-summary--people/, 'no tiles repeating the counts');
});

test('the review page, its messages and its live status use no system terms', () => {
    const terms = /identity intelligence|identity-group|identity groups|snapshot|known-match|known-person|trusted references|unresolved appearances|individual-review queue|matching worker/i;
    // What a person reads: text between tags in the page, and string literals shown to them.
    const visible = page.replace(/@\*[\s\S]*?\*@/g, '').match(/>[^<>@{}]+</g).join('\n');
    assert.doesNotMatch(visible, terms);
    const messages = model.match(/"[A-Z][^"]{14,}"/g).filter(text => !/could not be (refreshed|loaded)\.$|operation failed\.$/.test(text));
    assert.doesNotMatch(messages.join('\n'), terms);
    // Only the words: string and template literals of some length, not the names of variables.
    assert.doesNotMatch(script.match(/"[^"\n]{12,}"|`[^`\n]{12,}`/g).join('\n'), terms);
});

test('the grouping notice is shown only while groups are being rebuilt or could not be', () => {
    assert.match(page, /data-grouping-refresh-notice\s+hidden="@\(!Model\.GroupingRefreshing && !Model\.GroupingFailed \? "hidden" : null\)"/);
    // The Reload link sits inside the notice, so the notice stays while reloading is offered.
    assert.match(script, /groupingRefreshNotice\.hidden = !groupingRefreshFailed && !data\.groupingRefreshPending && !reloadOffered;/);
    assert.match(script, /groupingRefreshAction\.hidden = false;\s+\/\/.*\s+if \(groupingRefreshNotice instanceof HTMLElement\) \{\s+groupingRefreshNotice\.hidden = false;/);
});

test('a Same person? card: plain heading, sure faces start ticked, no scores on faces', () => {
    const card = page.slice(page.indexOf('class="identity-group-card"'), page.indexOf('</article>', page.indexOf('class="identity-group-card"')));
    assert.match(card, /<h2>These @group\.Members\.Count faces look like the same person<\/h2>/);
    assert.match(card, /var startsTicked = member\.FaceId == group\.RepresentativeFaceId \|\| similarityBand == "strong";/);
    assert.match(card, /checked="@startsTicked"/);
    assert.match(card, /@if \(!startsTicked\)\s+\{\s+<span class="identity-similarity is-@similarityBand"><em>Less sure<\/em><\/span>/);
    // The figures are in one place, behind a disclosure, and say what they are for.
    const figures = card.slice(card.indexOf('<details class="identity-group-figures">'));
    assert.match(figures, /<summary>How sure is PRISM\?<\/summary>/);
    assert.match(figures, /It never names anyone\./);
    const faces = card.slice(card.indexOf('class="identity-group-mosaic"'), card.indexOf('<details class="identity-group-figures">'));
    assert.doesNotMatch(faces, /ToString\("0\.0+"\)/, 'no likeness number outside the disclosure');
    assert.match(script, /`Confirm \$\{selected\} \$\{selected === 1 \? "face" : "faces"\}`/);
});

test('a face card asks "Is this …?", keeps the figures behind a disclosure and names through the one field', () => {
    const cards = page.slice(page.indexOf('class="face-review-card__face"'), page.indexOf('class="people-pagination"'));
    assert.match(cards, /<strong>Is this <a asp-page="\.\/Details" asp-route-id="@candidate\.PersonId">@candidate\.DisplayName<\/a>\?<\/strong>/);
    assert.match(cards, /<i class="bi bi-check-lg"><\/i> Yes<\/button>/);
    assert.match(cards, /aria-label="No, not @candidate\.DisplayName">No<\/button>/);
    assert.doesNotMatch(cards, /Quality @item\.QualityScore/, 'no quality score on the face');
    // Every number is inside the disclosure.
    const outside = cards.replace(/<details class="identity-group-figures">[\s\S]*?<\/details>/g, '');
    assert.doesNotMatch(outside, /ToString\("0\.0+"\)|:0\.00\}/);
    // One form for naming: the field on top, the two plain controls beneath, the two existing handlers.
    const form = cards.slice(cards.indexOf('<form method="post" class="face-name-form">'), cards.indexOf('class="face-review-card__secondary-actions"', cards.indexOf('<form method="post" class="face-name-form">')));
    assert.match(form, /data-assign-action="@Url\.Page\("\.\/Review", "AssignExisting"\)"\s+data-create-action="@Url\.Page\("\.\/Review", "Create"\)"/);
    ['name="faceId"', 'select id="existing-person-@item.FaceId" class="form-select" name="personId"', 'name="displayName"', 'asp-page-handler="AssignExisting"', 'asp-page-handler="Create"']
        .forEach(piece => assert.ok(form.includes(piece), piece));
    ['None of these', 'Leave unnamed', 'Not a face', 'Bring back to review'].forEach(label => assert.ok(cards.includes(label), label));
});

test('unnamed faces are one grid; ticked faces are named through the one field', () => {
    assert.doesNotMatch(page, /face-review-layout-toggle/, 'no Triage / Detailed switch');
    assert.match(page, /class="face-review-back" href="@\(Model\.BuildReviewUrl\(layout: "triage", pageNumber: 1\)\)"/, 'a face opened on its own offers the way back');
    const bar = page.slice(page.indexOf('id="batchAssignKnownPersonForm"'), page.indexOf('@if (Model.IsTriageLayout)'));
    assert.match(bar, /data-assign-action="@Url\.Page\("\.\/Review", "AssignSelected"\)"\s+data-create-action="@Url\.Page\("\.\/Review", "CreateSelected"\)"/);
    ['name="personId"', 'name="displayName"', 'asp-page-handler="AssignSelected"', 'asp-page-handler="CreateSelected"', 'asp-page-handler="IgnoreSelected"', 'asp-page-handler="SuppressSelected"', 'asp-page-handler="ReopenSelected"']
        .forEach(piece => assert.ok(bar.includes(piece), piece));
    // Naming several single faces as a new person is the group operation, kept on the reviewer's queue.
    const createSelected = model.slice(model.indexOf('OnPostCreateSelectedAsync('), model.indexOf('OnPostIgnoreSelectedAsync('));
    assert.ok(createSelected.includes('_review.CreatePersonAndAssignManyAsync(faceIds, displayName, UserId, cancellationToken)'));
    assert.match(createSelected, /"New person added and the selected faces named\.",\s+Mode,/, 'it stays on the queue the reviewer is in');
    const tiles = page.slice(page.indexOf('class="face-review-triage-grid"'), page.indexOf('class="face-review-list"'));
    assert.doesNotMatch(tiles, /QualityScore/);
});

test('a decision can be undone from the message that confirms it, and only from there', () => {
    assert.match(page, /@if \(Model\.CanUndo\)\s+\{\s+<form method="post" asp-page-handler="Undo">/);
    // The note is looked at while the message shows, dropped otherwise, and used up by Undo.
    assert.match(model, /CanUndo = !string\.IsNullOrWhiteSpace\(StatusMessage\)\s+&& PeopleReviewUndo\.Read\(UndoState, UserId, DateTimeOffset\.UtcNow\) is not null;\s+if \(!CanUndo\)\s+\{\s+UndoState = null;/);
    const undo = model.slice(model.indexOf('OnPostUndoAsync'), model.indexOf('private async Task<IActionResult> ExecuteAsync'));
    assert.match(undo, /PeopleReviewUndo\.Read\(UndoState, UserId, DateTimeOffset\.UtcNow\);\s+UndoState = null;/);
    assert.match(undo, /_review\.ReturnAssignmentsToReviewAsync\(undo\.PersonId!\.Value, undo\.FaceIds\.ToList\(\), UserId, PeopleReviewUndo\.UndoReason, cancellationToken\)/, 'a naming is undone by the recorded correction');
    assert.match(undo, /_review\.ReopenUnidentifiedManyAsync\(undo\.FaceIds\.ToList\(\), UserId, cancellationToken\)/);
    // Any new decision ends the chance to undo the one before.
    assert.match(model, /Func<string\?>\? undoState = null\)\s+\{\s+\/\/[^\n]*\n\s+UndoState = null;/);
});

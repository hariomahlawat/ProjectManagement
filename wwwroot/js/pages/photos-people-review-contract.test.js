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
    assert.match(script, /groupingRefreshNotice\.hidden = !groupingRefreshFailed && !data\.groupingRefreshPending;/);
});

const fs = require('node:fs');
const path = require('node:path');
const { test } = require('node:test');
const assert = require('node:assert/strict');

const source = fs.readFileSync(path.resolve(__dirname, 'officer-conference.js'), 'utf8');

test('conference editor supports keyboard save and cancel', () => {
    assert.match(source, /event\.key === 'Escape'/);
    assert.match(source, /event\.ctrlKey \|\| event\.metaKey/);
});

test('conference save uses antiforgery and same-origin credentials', () => {
    assert.match(source, /X-CSRF-TOKEN/);
    assert.match(source, /credentials: 'same-origin'/);
});

test('conference direction rendering uses textContent rather than user HTML', () => {
    assert.match(source, /body\.textContent = direction\.body/);
    assert.doesNotMatch(source, /direction\.body.*innerHTML/);
});

test('officer navigation uses server-generated route values', () => {
    assert.match(source, /window\.location\.assign\(destination\)/);
    assert.doesNotMatch(source, /`\/Workspace\/Conference\//);
});

test('conference editor restores focus and exposes busy and invalid states', () => {
    assert.match(source, /restoreFocus: true/);
    assert.match(source, /aria-busy/);
    assert.match(source, /aria-invalid/);
});

test('conference save surfaces server trace references and row-local feedback', () => {
    assert.match(source, /payload\.traceId/);
    assert.match(source, /setRowStatus\(item, 'Direction saved\.'/);
    assert.match(source, /setRowStatus\(item, 'Save failed\.', true\)/);
});

test('conference direction uses formal instruction icon and simplified metadata', () => {
    assert.match(source, /bi bi-file-earmark-check/);
    assert.match(source, /Latest conference direction/);
    assert.doesNotMatch(source, /direction\.authorRole/);
    assert.doesNotMatch(source, /direction\.snapshotLabel/);
    assert.doesNotMatch(source, /direction\.snapshotValue/);
});

test('conference direction metadata retains a subdued timestamp', () => {
    assert.match(source, /hour: '2-digit'/);
    assert.match(source, /minute: '2-digit'/);
    assert.match(source, /oc-direction__timestamp/);
});

test('conference directions expose accessible more and less controls', () => {
    assert.match(source, /data-oc-direction-toggle/);
    assert.match(source, /aria-expanded/);
    assert.match(source, /toggle\.textContent = expanded \? 'Less' : 'More'/);
});

test('structured progress renders project, idea and task activity safely', () => {
    assert.match(source, /payload\.progressEntries/);
    assert.match(source, /entry\.body/);
    assert.match(source, /body\.textContent = entry\.body/);
    assert.match(source, /payload\.emptyProgressText/);
    assert.doesNotMatch(source, /entry\.body.*innerHTML/);
});


test('task progress no longer renders generic status or due-date movement summaries', () => {
    assert.doesNotMatch(source, /structuredProgressKinds/);
    assert.doesNotMatch(source, /oc-task-progress-summary/);
    assert.doesNotMatch(source, /latestProgressText/);
});

test('saving a direction clears the neutral empty-direction state', () => {
    assert.match(source, /classList\.remove\('oc-item__direction--empty'\)/);
});

test('structured progress supports accessible more and less controls', () => {
    assert.match(source, /data-oc-progress-toggle/);
    assert.match(source, /setProgressExpanded/);
    assert.match(source, /configureProgressToggle/);
});

test('conference save uses only row-local success feedback', () => {
    assert.doesNotMatch(source, /setPageFeedback/);
    assert.match(source, /setRowStatus\(item, 'Direction saved\.'/);
});

test('conference page creates Action Tracker tasks without navigation', () => {
    assert.match(source, /data-oc-task-add/);
    assert.match(source, /const saveTask = async/);
    assert.match(source, /fetch\(editor\.action/);
    assert.match(source, /appendCreatedTask\(payload\.task\)/);
    assert.doesNotMatch(source, /window\.location.*ActionTasks/);
});

test('conference task creation uses antiforgery, same-origin credentials and double-submit protection', () => {
    assert.match(source, /data\.append\('__RequestVerificationToken'/);
    assert.match(source, /credentials: 'same-origin'/);
    assert.match(source, /editor\.classList\.contains\('is-saving'\)/);
    assert.match(source, /aria-busy/);
});

test('conference task form supports keyboard create, cancel and field-level validation', () => {
    assert.match(source, /const applyTaskErrors/);
    assert.match(source, /data-oc-task-error/);
    assert.match(source, /event\.key === 'Escape'/);
    assert.match(source, /void saveTask\(taskEditor\)/);
});

test('new task rendering uses textContent for server-returned values', () => {
    assert.match(source, /title\.textContent = task\.title/);
    assert.match(source, /context\.textContent = task\.currentContext/);
    assert.doesNotMatch(source, /task\.title.*innerHTML/);
});

test('conference page creates Project Ideas without leaving the officer review', () => {
    assert.match(source, /data-oc-idea-add/);
    assert.match(source, /const saveIdea = async/);
    assert.match(source, /appendCreatedIdea\(payload\.idea\)/);
    assert.doesNotMatch(source, /window\.location.*ProjectIdeas/);
});

test('conference idea creation uses antiforgery and same-origin credentials', () => {
    assert.match(source, /data\.append\('__RequestVerificationToken'/);
    assert.match(source, /credentials: 'same-origin'/);
    assert.match(source, /editor\.classList\.contains\('is-saving'\)/);
});

test('conference idea form supports keyboard create, cancel and field-level validation', () => {
    assert.match(source, /const applyIdeaErrors/);
    assert.match(source, /data-oc-idea-error/);
    assert.match(source, /void saveIdea\(ideaEditor\)/);
    assert.match(source, /closeIdeaEditor\(ideaEditor, \{ restoreFocus: true \}\)/);
});

test('new idea rendering uses textContent and updates idea counts', () => {
    assert.match(source, /title\.textContent = idea\.title/);
    assert.match(source, /context\.textContent = idea\.currentContext/);
    assert.match(source, /data-oc-header-idea-count/);
    assert.doesNotMatch(source, /idea\.title.*innerHTML/);
});

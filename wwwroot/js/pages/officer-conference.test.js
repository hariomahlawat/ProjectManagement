const fs = require('node:fs');
const path = require('node:path');
const { test } = require('node:test');
const assert = require('node:assert/strict');

const source = fs.readFileSync(path.resolve(__dirname, 'officer-conference.js'), 'utf8');
const conferenceCss = fs.readFileSync(path.resolve(__dirname, '../../css/officer-conference.css'), 'utf8');

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

test('conference direction uses a semantic label and simplified metadata', () => {
    assert.match(source, /aria-label', 'Latest conference direction/);
    assert.doesNotMatch(source, /oc-direction__label/);
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


test('first direction transitions from issue action to issue-further-direction action', () => {
    assert.match(source, /label\.textContent = 'Issue further direction'/);
    assert.match(source, /actions\.prepend\(addButton\)/);
    assert.match(source, /saveLabel\.textContent = 'Issue further direction'/);
});

test('conference sticky toolbar measures the actual application header and avoids fixed offsets', () => {
    assert.match(source, /document\.querySelector\('\.pm-topbar'\)/);
    assert.match(source, /const syncStickyHeaderHeight/);
    assert.match(source, /--oc-topbar-height/);
    assert.match(source, /ResizeObserver/);
    assert.match(source, /window\.addEventListener\('scroll', scheduleStickySync/);
});


test('conference sticky toolbar uses hysteresis and avoids threshold oscillation', () => {
    assert.match(source, /const stickyEnterOffset = 1/);
    assert.match(source, /const stickyReleaseOffset = 8/);
    assert.match(source, /stickyState\s*\?\s*shellTop <= topbarHeight \+ stickyReleaseOffset/);
    assert.match(source, /shellTop <= topbarHeight \+ stickyEnterOffset/);
    assert.match(source, /nextStickyState !== stickyState/);
    assert.match(source, /window\.getComputedStyle\(stickyShell\)\.position === 'sticky'/);
});

test('conference sticky state preserves header geometry', () => {
    const stuckRule = conferenceCss.match(/\.oc-sticky-shell\.is-stuck \.oc-header\s*\{([\s\S]*?)\}/)?.[1] ?? '';

    assert.match(stuckRule, /min-height:\s*64px/);
    assert.match(stuckRule, /padding:\s*8px 12px/);
    assert.doesNotMatch(conferenceCss, /transition:[^;]*(?:min-height|padding)/);
    assert.doesNotMatch(conferenceCss, /\.oc-sticky-shell\.is-stuck \.oc-eyebrow\s*\{[^}]*display:\s*none/);
    assert.doesNotMatch(conferenceCss, /\.oc-sticky-shell\.is-stuck \.oc-avatar\s*\{/);
    assert.doesNotMatch(conferenceCss, /\.oc-sticky-shell\.is-stuck \.oc-header__metric--tasks\.is-zero\s*\{/);
});

test('conference rows create column headings when the first idea or task is added', () => {
    assert.match(source, /const ensureColumnHeadings/);
    assert.match(source, /ensureColumnHeadings\(section, 'Idea'\)/);
    assert.match(source, /ensureColumnHeadings\(section, 'Task'\)/);
});

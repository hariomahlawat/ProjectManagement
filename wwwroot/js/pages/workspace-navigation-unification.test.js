const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../../..');
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');

test('workspace rail exposes a stable authorised navigation model', () => {
    const rail = read('Pages/Workspace/_CommandWorkspaceRail.cshtml');

    assert.match(rail, /My Workspace/);
    assert.match(rail, /cw-workspace-lens/);
    assert.match(rail, />Personal</);
    assert.match(rail, />Command</);
    assert.match(rail, /Model\.HasProjectOfficerAccess/);
    assert.match(rail, /Model\.HasCommandAccess/);
    assert.match(rail, />My work</);
    assert.match(rail, />Command oversight</);
    assert.match(rail, />System adoption</);
    assert.match(rail, />My resources</);
    assert.match(rail, /My conference review/);
    assert.match(rail, /Briefing decks/);
    assert.doesNotMatch(rail, /Workspace mode/);
    assert.doesNotMatch(rail, /is-context-active/);
});

test('personal and command workspace shells render the same navigation rail', () => {
    const personal = read('Pages/Workspace/_ProjectOfficerWorkspace.cshtml');
    const command = read('Pages/Workspace/_CommandWorkspace.cshtml');

    assert.match(personal, /PartialAsync\("_CommandWorkspaceRail", Model\.NavigationRail\)/);
    assert.match(command, /PartialAsync\("_CommandWorkspaceRail", Model\.NavigationRail\)/);
});

test('personal and command scripts persist one navigation expansion preference', () => {
    const personalScript = read('wwwroot/js/pages/workspace-index.js');
    const commandScript = read('wwwroot/js/pages/command-workspace.js');
    const key = 'prism.workspace.navigationExpanded';

    assert.match(personalScript, new RegExp(key.replaceAll('.', '\\.')));
    assert.match(commandScript, new RegExp(key.replaceAll('.', '\\.')));
    assert.doesNotMatch(personalScript, /projectOfficerWorkspace\.navExpanded/);
    assert.doesNotMatch(commandScript, /commandWorkspace\.navigationExpanded/);
});

test('workspace page model loads the opposite navigation shell for dual-role users', () => {
    const pageModel = read('Pages/Workspace/Index.cshtml.cs');

    assert.match(pageModel, /else if \(hasProjectOfficerRole\)/);
    assert.match(pageModel, /GetNavigationShellAsync\(/);
    assert.match(pageModel, /NavigationRail = BuildNavigationRail/);
    assert.match(pageModel, /HasCommandAccess = hasCommandAccess/);
    assert.match(pageModel, /HasProjectOfficerAccess = hasProjectOfficerAccess/);
});

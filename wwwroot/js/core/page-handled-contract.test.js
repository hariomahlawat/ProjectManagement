// View contract: every element marked data-page-handled must be handled by a page script that
// actually loads where the element renders. Otherwise declarative-actions.js skips it and
// nothing confirms or submits (as happened on Activities Details).
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const repoRoot = path.resolve(__dirname, '..', '..', '..');
const read = (relative) => fs.readFileSync(path.join(repoRoot, relative), 'utf8');

// view -> { script: handler file, selector: text the handler must contain, hosts: pages that load the handler }
const registry = {
  'Areas/ProjectOfficeReports/Pages/SocialMedia/_PhotoGallery.cshtml': {
    script: 'wwwroot/js/pages/project-office-reports/social-media.js',
    selector: "form[data-confirm]",
    hosts: ['Areas/ProjectOfficeReports/Pages/SocialMedia/Details.cshtml', 'Areas/ProjectOfficeReports/Pages/SocialMedia/Edit.cshtml'],
  },
  'Areas/ProjectOfficeReports/Pages/Visits/_PhotosPartial.cshtml': {
    script: 'wwwroot/js/pages/project-office-reports/visits.js',
    selector: "form[data-confirm]",
    hosts: ['Areas/ProjectOfficeReports/Pages/Visits/Details.cshtml', 'Areas/ProjectOfficeReports/Pages/Visits/Edit.cshtml', 'Areas/ProjectOfficeReports/Pages/Visits/New.cshtml'],
  },
  'Areas/ProjectOfficeReports/Pages/Visits/Details.cshtml': {
    script: 'wwwroot/js/pages/project-office-reports/visits.js',
    selector: "form[data-confirm]",
    hosts: ['Areas/ProjectOfficeReports/Pages/Visits/Details.cshtml'],
  },
  'Pages/Tasks/_TaskList.cshtml': {
    script: 'wwwroot/js/todo.js',
    selector: '.js-confirm-delete',
    hosts: ['Pages/Shared/_Layout.cshtml'], // init.js loads todo.js wherever a .todo-list renders
  },
  'Pages/Tasks/Index.cshtml': {
    script: 'wwwroot/js/todo.js',
    selector: '.js-confirm-delete',
    hosts: ['Pages/Shared/_Layout.cshtml'],
  },
  'Pages/Shared/_TodoWidget.cshtml': {
    script: 'wwwroot/js/todo.js',
    selector: '.js-confirm-delete',
    hosts: ['Pages/Shared/_Layout.cshtml'],
  },
  'Pages/Analytics/Partials/_ProjectManagementInsights.cshtml': {
    script: 'wwwroot/js/analytics-projects.js',
    selector: '[data-auto-submit="change"]',
    hosts: ['Pages/Analytics/Index.cshtml'],
  },
  'Pages/Analytics/Partials/_ArppYearFilter.cshtml': {
    script: 'wwwroot/js/analytics-projects.js',
    selector: '[data-auto-submit="change"]',
    hosts: ['Pages/Analytics/Index.cshtml'],
  },
  'Pages/ActionTasks/_TaskUpdateTimeline.cshtml': {
    script: 'wwwroot/js/pages/action-tasks/task-interaction.js',
    selector: '[data-at-update-delete-form]',
    hosts: ['Pages/ActionTasks/Details.cshtml', 'Pages/ActionTasks/Index.cshtml'],
  },
  'Pages/Approvals/Pending/Index.cshtml': {
    script: 'wwwroot/js/pages/approvals-pending.js',
    selector: '[data-auto-submit]',
    hosts: ['Pages/Approvals/Pending/Index.cshtml'],
  },
};

function viewsUsingPageHandled(directory, found = []) {
  for (const entry of fs.readdirSync(path.join(repoRoot, directory), { withFileTypes: true })) {
    const relative = path.posix.join(directory, entry.name);
    if (entry.isDirectory()) {
      viewsUsingPageHandled(relative, found);
    } else if (entry.name.endsWith('.cshtml') && read(relative).includes('data-page-handled')) {
      found.push(relative);
    }
  }
  return found;
}

test('every view using data-page-handled is registered with its handler', () => {
  const views = [...viewsUsingPageHandled('Pages'), ...viewsUsingPageHandled('Areas')].sort();
  const unregistered = views.filter((view) => !registry[view]);
  assert.deepEqual(unregistered, [], 'Register these views (and their handler script) in page-handled-contract.test.js, or drop data-page-handled so declarative-actions.js handles them.');
});

for (const [view, { script, selector, hosts }] of Object.entries(registry)) {
  test(`${view} is handled by ${path.basename(script)}`, () => {
    assert.ok(read(view).includes('data-page-handled'), `${view} no longer uses data-page-handled; remove it from the registry`);
    assert.ok(read(script).includes(selector), `${script} no longer contains ${selector}`);
    const loader = path.basename(script);
    for (const host of hosts) {
      assert.ok(read(host).includes(loader), `${host} does not load ${loader}`);
    }
  });
}

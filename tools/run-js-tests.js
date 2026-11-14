#!/usr/bin/env node

const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');

// -----------------------------------------------------------------------------
// Test Discovery
// -----------------------------------------------------------------------------
const projectTestsRoot = path.join('wwwroot', 'js', 'projects');
const notebookTestsRoot = path.join('wwwroot', 'js', 'notebook');
const explicitTests = [
  path.join('wwwroot', 'js', 'pages', 'action-tasks', 'index.test.js'),
  path.join('wwwroot', 'js', 'pages', 'workspace-index.test.js'),
  path.join('wwwroot', 'js', 'pages', 'workspace-briefing-desk.test.js'),
  path.join('wwwroot', 'js', 'pages', 'workspace-navigation-unification.test.js'),
  path.join('wwwroot', 'js', 'pages', 'activities-build-hotfix.test.js'),
  path.join('wwwroot', 'js', 'pages', 'activities-hardening.test.js'),
  path.join('wwwroot', 'js', 'pages', 'project-portfolio-latest-update.test.js'),
  path.join('wwwroot', 'js', 'pages', 'officer-conference.test.js'),
  path.join('wwwroot', 'js', 'pages', 'it-projects-details.test.js'),
  path.join('wwwroot', 'js', 'pages', 'it-projects-create.test.js'),
  path.join('wwwroot', 'js', 'pages', 'photos-people-directory.test.js'),
  path.join('wwwroot', 'js', 'pages', 'photos-curation-contract.test.js'),
  path.join('wwwroot', 'js', 'pages', 'photos-library-contract.test.js'),
  path.join('wwwroot', 'js', 'pages', 'photos-library-scroll.test.js'),
  path.join('wwwroot', 'js', 'pages', 'photos-library-selection.test.js'),
  path.join('wwwroot', 'js', 'pages', 'photos-people-review-contract.test.js'),
  path.join('wwwroot', 'js', 'pages', 'photos-people-name-field.test.js'),
  path.join('wwwroot', 'js', 'pages', 'photos-people-keys.test.js'),
  path.join('wwwroot', 'js', 'pages', 'officer-conference-landing.test.js'),
  path.join('wwwroot', 'js', 'pages', 'photos-search-suggest.test.js'),
  path.join('wwwroot', 'js', 'pages', 'photos-person-linkage-contract.test.js'),
  path.join('wwwroot', 'js', 'pages', 'photos-person-profile-contract.test.js'),
  path.join('wwwroot', 'js', 'calendar.test.js'),
  path.join('wwwroot', 'js', 'chat.test.js'),
  path.join('wwwroot', 'js', 'notifications.test.js'),
  path.join('wwwroot', 'js', 'process-route.test.js'),
  path.join('wwwroot', 'js', 'project-office-reports', 'ipr', 'index.test.js'),
  path.join('wwwroot', 'js', 'project-office-reports', 'ipr', 'register-view.test.js'),
  path.join('wwwroot', 'js', 'project-office-reports', 'ipr', 'editor-focus.test.js'),
  path.join('wwwroot', 'js', 'pages', 'project-office-reports', 'training', 'training-export.test.js'),
  path.join('wwwroot', 'js', 'pages', 'project-office-reports', 'ffc', 'ffc-record-workspace.test.js'),
  path.join('wwwroot', 'js', 'pages', 'project-office-reports', 'ffc', 'ffc-footprint.test.js'),
  path.join('wwwroot', 'js', 'pages', 'project-office-reports', 'ffc', 'ffc-powerpoint-export.test.js'),
  path.join('wwwroot', 'js', 'pages', 'project-office-reports', 'visits-photo-viewer.test.js'),
  path.join('wwwroot', 'js', 'components', 'prism-confirm-dialog.test.js'),
  path.join('wwwroot', 'js', 'components', 'prism-toast.test.js'),
  path.join('wwwroot', 'js', 'widgets', 'media-viewer.test.js'),
  path.join('wwwroot', 'js', 'core', 'declarative-actions.test.js'),
  path.join('wwwroot', 'js', 'admin', 'admin-shell.test.js'),
  path.join('wwwroot', 'js', 'core', 'page-handled-contract.test.js'),
  path.join('wwwroot', 'js', 'core', 'theme-preference.test.js'),
  path.join('wwwroot', 'js', 'core', 'whats-new.test.js'),
  path.join('wwwroot', 'js', 'navigation', 'search-combobox.test.js'),
  path.join('wwwroot', 'js', 'pages', 'search.test.js'),
  path.join('wwwroot', 'js', 'navigation', 'drawer.test.js'),
  path.join('wwwroot', 'js', 'pdf-tools', 'pdf-tools-core.test.js'),
  path.join('wwwroot', 'js', 'pdf-tools', 'pdf-tools-reduce.test.js'),
  path.join('wwwroot', 'js', 'photo-tools', 'photo-tools-core.test.js'),
  path.join('wwwroot', 'js', 'photo-tools', 'photo-tools-canvas.test.js'),
  path.join('tools', 'check-views.test.js'),
  path.join('tools', 'theme-coverage.test.js'),
  path.join('tools', 'theme-dark-codemod.test.js'),
  path.join('tools', 'css-dead-declarations.test.js'),
];

function findTestFiles(directory) {
  if (!fs.existsSync(directory)) {
    return [];
  }

  const entries = fs.readdirSync(directory, { withFileTypes: true });
  const testFiles = [];

  for (const entry of entries) {
    const entryPath = path.join(directory, entry.name);

    if (entry.isDirectory()) {
      testFiles.push(...findTestFiles(entryPath));
      continue;
    }

    if (entry.isFile() && entry.name.endsWith('.test.js')) {
      testFiles.push(entryPath);
    }
  }

  return testFiles.sort();
}

// -----------------------------------------------------------------------------
// Test Execution
// -----------------------------------------------------------------------------
const testFiles = [...findTestFiles(projectTestsRoot), ...findTestFiles(notebookTestsRoot), ...explicitTests];
const result = spawnSync(process.execPath, ['--test', ...testFiles], {
  stdio: 'inherit',
  shell: false,
});

process.exit(result.status ?? 1);

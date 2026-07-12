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
  path.join('wwwroot', 'js', 'pages', 'officer-conference.test.js'),
  path.join('wwwroot', 'js', 'calendar.test.js'),
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

'use strict';

// Browser tests for Collage Studio (docs/ux/collage-review-fixes.md, PR 10). Development and CI
// only: Playwright and its browser are never part of the PRISM deployment.

const { defineConfig, devices } = require('@playwright/test');

const port = Number(process.env.COLLAGE_TEST_PORT || 7399);

module.exports = defineConfig({
  testDir: __dirname,
  testMatch: /.*\.spec\.js$/,
  timeout: 60000,
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  // A test that passes only on its retry fails the run, so a flaky test is seen, not hidden.
  failOnFlakyTests: Boolean(process.env.CI),
  reporter: process.env.CI ? [['list'], ['html', { open: 'never', outputFolder: '../../playwright-report' }]] : 'list',
  // Each port has its own results folder, so two runs at once (each on its own COLLAGE_TEST_PORT)
  // never write over each other's traces and screenshots.
  outputDir: `../../test-results/collage-${port}`,
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    trace: 'retain-on-failure',
    acceptDownloads: true
  },
  projects: [
    { name: 'desktop', testIgnore: /phone.spec.js$/, use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 800 } } },
    { name: 'phone', testMatch: /phone.spec.js$/, use: { ...devices['Pixel 7'] } }
  ],
  webServer: {
    command: 'node tests/browser/serve.js',
    cwd: require('node:path').resolve(__dirname, '..', '..'),
    url: `http://127.0.0.1:${port}/Tools/Collage`,
    reuseExistingServer: !process.env.CI,
    timeout: 30000
  }
});

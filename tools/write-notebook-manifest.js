const fs = require('node:fs');
const { execSync } = require('node:child_process');

// SECTION: Notebook bundle build manifest for runtime diagnostics.
function sourceCommit() {
  try {
    return execSync('git rev-parse --short HEAD', { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
  } catch {
    return null;
  }
}

const manifest = {
  entry: 'notebook-index.bundle.js',
  builtAtUtc: new Date().toISOString(),
  sourceCommit: sourceCommit()
};

fs.mkdirSync('wwwroot/dist', { recursive: true });
fs.writeFileSync('wwwroot/dist/notebook-manifest.json', `${JSON.stringify(manifest, null, 2)}\n`);

const fs = require('node:fs');
const { execFileSync } = require('node:child_process');

const manifestPath = 'wwwroot/dist/notebook-manifest.json';

// SECTION: Stable source identity for committed Notebook assets
function existingSourceCommit() {
  try {
    const existing = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));

    if (typeof existing.sourceCommit === 'string' && existing.sourceCommit.trim()) {
      return existing.sourceCommit;
    }
  } catch {
    // Existing manifest is absent or unreadable.
  }

  return null;
}

function sourceCommit() {
  const suppliedCommit = process.env.SOURCE_COMMIT?.trim();

  if (suppliedCommit) {
    return suppliedCommit;
  }

  const previousCommit = existingSourceCommit();

  if (previousCommit) {
    return previousCommit;
  }

  try {
    return execFileSync(
      'git',
      [
        'rev-parse',
        '--short=12',
        'HEAD'
      ],
      {
        encoding: 'utf8',
        stdio: [
          'ignore',
          'pipe',
          'ignore'
        ]
      }
    ).trim();
  } catch {
    return 'unknown';
  }
}

// SECTION: Content-stable manifest file writes
function writeIfChanged(filePath, content) {
  let existing = null;

  try {
    existing = fs.readFileSync(filePath, 'utf8');
  } catch {
    // Output does not exist.
  }

  if (existing === content) {
    return false;
  }

  fs.writeFileSync(filePath, content, 'utf8');
  return true;
}

// SECTION: Notebook bundle build manifest for runtime diagnostics
const manifest = {
  entry: 'notebook-index.bundle.js',
  sourceCommit: sourceCommit()
};

fs.mkdirSync('wwwroot/dist', { recursive: true });
writeIfChanged(
  manifestPath,
  `${JSON.stringify(manifest, null, 2)}\n`
);

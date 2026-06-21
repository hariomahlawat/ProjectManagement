const fs = require('node:fs');
const { createHash } = require('node:crypto');

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

  return existingSourceCommit();
}

function calculateSha256(filePath) {
  const bytes = fs.readFileSync(filePath);

  return createHash('sha256')
    .update(bytes)
    .digest('hex');
}

// SECTION: Content-stable manifest file writes
function writeOrTouch(filePath, content) {
  let existing = null;

  try {
    existing = fs.readFileSync(filePath, 'utf8');
  } catch (error) {
    if (error?.code !== 'ENOENT') {
      throw error;
    }
  }

  if (existing === content) {
    const now = new Date();
    fs.utimesSync(filePath, now, now);

    return {
      contentChanged: false,
      timestampUpdated: true
    };
  }

  fs.writeFileSync(filePath, content, 'utf8');

  return {
    contentChanged: true,
    timestampUpdated: true
  };
}

// SECTION: Notebook bundle build manifest for runtime diagnostics
const manifest = {
  entry: 'notebook-index.bundle.js',
  bundleSha256: calculateSha256('wwwroot/dist/notebook-index.bundle.js')
};
const commit = sourceCommit();

if (commit) {
  manifest.sourceCommit = commit;
}

fs.mkdirSync('wwwroot/dist', { recursive: true });
const result = writeOrTouch(
  manifestPath,
  `${JSON.stringify(manifest, null, 2)}\n`
);

if (result.contentChanged) {
  console.log('Notebook manifest updated.');
} else {
  console.log('Notebook manifest unchanged; output timestamp refreshed.');
}

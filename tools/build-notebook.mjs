import { spawnSync } from 'node:child_process';
import { build } from 'esbuild';

// SECTION: Deterministic Notebook bundle settings
const minify = process.argv.includes('--minify');

await build({
  entryPoints: ['wwwroot/js/pages/notebook-index.js'],
  outfile: 'wwwroot/dist/notebook-index.bundle.js',
  bundle: true,
  format: 'esm',
  platform: 'browser',
  target: ['es2022'],
  sourcemap: true,
  sourcesContent: true,
  charset: 'utf8',
  legalComments: 'none',
  minify,
  logLevel: 'info'
});

// SECTION: Deterministic Notebook runtime manifest
const manifestResult = spawnSync(
  process.execPath,
  ['tools/write-notebook-manifest.js'],
  {
    stdio: 'inherit'
  }
);

if (manifestResult.status !== 0) {
  process.exit(manifestResult.status ?? 1);
}

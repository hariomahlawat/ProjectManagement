import { spawnSync } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import { build } from 'esbuild';

// SECTION: Deterministic Notebook bundle settings
const minify = process.argv.includes('--minify');

// Sources are read with LF line endings so the source map (which embeds them) is the same
// on a Windows checkout with CRLF files as in CI; otherwise check:notebook-assets fails.
const normaliseLineEndings = {
  name: 'normalise-line-endings',
  setup(context) {
    context.onLoad({ filter: /\.m?js$/ }, async (args) => ({
      contents: (await readFile(args.path, 'utf8')).replace(/\r\n/g, '\n'),
      loader: 'js'
    }));
  }
};

await build({
  plugins: [normaliseLineEndings],
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

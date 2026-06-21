import { spawnSync } from 'node:child_process';

// SECTION: Rebuild committed Notebook assets before stale-output verification
const buildResult = spawnSync(
  process.platform === 'win32' ? 'npm.cmd' : 'npm',
  ['run', 'build:notebook'],
  {
    stdio: 'inherit'
  }
);

if (buildResult.status !== 0) {
  process.exit(buildResult.status ?? 1);
}

// SECTION: Fail clearly when committed generated assets are stale
const diffResult = spawnSync(
  'git',
  ['diff', '--quiet', '--', 'wwwroot/dist'],
  {
    stdio: 'inherit'
  }
);

if (diffResult.status !== 0) {
  console.error('Notebook generated assets are stale.');
  console.error("Run 'npm run build:notebook' and commit the updated wwwroot/dist files.");
  spawnSync('git', ['diff', '--', 'wwwroot/dist'], { stdio: 'inherit' });
  process.exit(diffResult.status ?? 1);
}

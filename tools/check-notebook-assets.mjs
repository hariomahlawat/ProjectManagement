import { spawnSync } from 'node:child_process';

// SECTION: Fail loudly when a step cannot start
// spawnSync reports launch failures (missing binary, EINVAL) through `error` with a null
// status; without this the check exited 1 silently and looked like it had run.
function run(command, args) {
  const result = spawnSync(command, args, { stdio: 'inherit' });
  if (result.error) {
    console.error(`Could not run ${command} ${args.join(' ')}: ${result.error.message}`);
  }
  return result;
}

// SECTION: Rebuild committed Notebook assets before stale-output verification
// Runs the build script with this Node directly (what `npm run build:notebook` does) so no
// npm shim or shell is needed; Node on Windows refuses to spawn npm.cmd without a shell.
const buildResult = run(process.execPath, ['tools/build-notebook.mjs']);

if (buildResult.status !== 0) {
  process.exit(buildResult.status ?? 1);
}

// SECTION: Fail clearly when committed generated assets are stale
const diffResult = run('git', ['diff', '--quiet', '--', 'wwwroot/dist']);

if (diffResult.status !== 0) {
  console.error('Notebook generated assets are stale.');
  console.error("Run 'npm run build:notebook' and commit the updated wwwroot/dist files.");
  spawnSync('git', ['diff', '--', 'wwwroot/dist'], { stdio: 'inherit' });
  process.exit(diffResult.status ?? 1);
}

console.log('Notebook generated assets are up to date.');

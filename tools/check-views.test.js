const test = require('node:test');
const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const script = path.join(__dirname, 'check-views.sh');
// Skipped (not failed) where bash or a PCRE2-enabled ripgrep is unavailable, e.g. plain Windows.
const hasTools = spawnSync('bash', ['--version']).status === 0
  && spawnSync('rg', ['--pcre2-version']).status === 0;

function runAgainst(files, env = process.env) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'check-views-'));
  try {
    for (const [name, content] of Object.entries(files)) {
      fs.writeFileSync(path.join(root, name), content);
    }
    return spawnSync('bash', [script, root], { encoding: 'utf8', env });
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
}

test('accepts external scripts, JSON data blocks, data-* behaviours and Razor identifiers', { skip: !hasTools }, () => {
  const result = runAgainst({
    'Valid.cshtml': [
      '@{ var onTimePercent = (Model?.OnTimePercent ?? 0d).ToString("0"); }',
      '<script src="~/js/site.js" asp-append-version="true"></script>',
      '<script type="application/json" id="data">@Html.Raw(json)</script>',
      '<form method="post" data-confirm="Delete this?"><button type="submit">Delete</button></form>',
      '<select data-auto-submit></select>',
      '<button type="button" data-print>Print</button>',
      '<i style="width:@(percent)%"></i>',
    ].join('\n'),
  });

  assert.equal(result.status, 0, result.stderr);
});

test('rejects inline handlers, including ones on their own line', { skip: !hasTools }, () => {
  const result = runAgainst({
    'Handlers.cshtml': [
      '<form method="post"',
      '      onsubmit="return confirm(\'Sure?\');">',
      '<select onchange="this.form.submit()"></select>',
      '<button ONCLICK=\'window.print()\'>Print</button>',
    ].join('\n'),
  });

  assert.equal(result.status, 1);
  assert.match(result.stderr, /Inline event handler/);
  assert.match(result.stderr, /:2:/);
  assert.match(result.stderr, /:3:/);
  assert.match(result.stderr, /:4:/);
});

test('rejects inline script blocks', { skip: !hasTools }, () => {
  const result = runAgainst({
    'Script.html': '<script>\nalert(1);\n</script>\n<script type="module">import "./x.js";</script>\n',
  });

  assert.equal(result.status, 1);
  assert.match(result.stderr, /Inline <script>/);
  assert.match(result.stderr, /:1:/);
  assert.match(result.stderr, /:4:/);
});

test('fails instead of passing when the paths are missing', { skip: !hasTools }, () => {
  const result = spawnSync('bash', [script, path.join(os.tmpdir(), 'check-views-missing-path')], { encoding: 'utf8' });

  assert.equal(result.status, 2);
});

test('rejects inline scripts and handlers that span lines or hide behind data-src', { skip: !hasTools }, () => {
  const result = runAgainst({
    'Spread.cshtml': [
      '<script',
      '   nonce="x">alert(1)</script>',
      '<script data-src="x.js">alert(2)</script>',
      '<select name="Sort"',
      '        onchange="this.form.submit()"></select>',
      '<a href="javascript:void(0)">Open</a>',
      '<noscript><style>.x { display: none; }</style></noscript>',
    ].join('\n'),
  });

  assert.equal(result.status, 1);
  assert.match(result.stderr, /Inline <style>/);
  assert.match(result.stderr, /Inline <script>/);
  assert.match(result.stderr, /:5:\s+onchange=/);
  assert.match(result.stderr, /javascript: URLs/);
});

test('accepts C# that looks like a handler and non-executable script types', { skip: !hasTools }, () => {
  const result = runAgainst({
    'Razor.cshtml': [
      '@{ var d = new { online = "y" }; var onTrackLabel = "On track"; }',
      '<script type="text/template" id="row"><tr><td></td></tr></script>',
      '<script asp-src-include="~/js/pages/*.js"></script>',
      '<script',
      '   src="~/js/site.js"></script>',
    ].join('\n'),
  });

  assert.equal(result.status, 0, result.stderr);
});

function withFakeRg(body) {
  const bin = fs.mkdtempSync(path.join(os.tmpdir(), 'fake-rg-'));
  try {
    fs.writeFileSync(path.join(bin, 'rg'), `#!/bin/sh\n${body}\n`, { mode: 0o755 });
    return runAgainst({ 'Any.cshtml': '<p>ok</p>' }, { ...process.env, PATH: `${bin}${path.delimiter}${process.env.PATH}` });
  } finally {
    fs.rmSync(bin, { recursive: true, force: true });
  }
}

test('fails instead of passing when ripgrep errors', { skip: !hasTools }, () => {
  const result = withFakeRg('case "$1" in --pcre2-version) exit 0;; esac; echo "boom" >&2; exit 2');

  assert.equal(result.status, 2);
  assert.match(result.stderr, /ripgrep failed/);
});

test('fails instead of passing when ripgrep lacks PCRE2', { skip: !hasTools }, () => {
  const result = withFakeRg('exit 2');

  assert.equal(result.status, 2);
  assert.match(result.stderr, /without PCRE2/);
});

test('fails instead of passing when ripgrep is missing', { skip: !hasTools }, () => {
  const bashDir = path.dirname(spawnSync('bash', ['-c', 'command -v bash'], { encoding: 'utf8' }).stdout.trim());
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'check-views-'));
  try {
    const result = spawnSync(path.join(bashDir, 'bash'), [script, root], { encoding: 'utf8', env: { PATH: '/nonexistent' } });
    assert.equal(result.status, 2);
    assert.match(result.stderr, /ripgrep \(rg\) is required/);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});


const test = require('node:test');
const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const script = path.join(__dirname, 'check-views.sh');
const hasTools = ['bash', 'rg'].every((tool) => spawnSync(tool, ['--version']).status === 0);

function runAgainst(files) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'check-views-'));
  try {
    for (const [name, content] of Object.entries(files)) {
      fs.writeFileSync(path.join(root, name), content);
    }
    return spawnSync('bash', [script, root], { encoding: 'utf8' });
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

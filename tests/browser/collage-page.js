'use strict';

// Builds Collage Studio's page as plain HTML from the real Razor page, for browser tests that
// need no database, sign-in or ASP.NET host (docs/ux/collage-review-fixes.md, PR 10).
//
// It understands only the few Razor features the page uses today. Anything else left in the
// output fails the build, so the test page can never quietly drift from the real one.

const fs = require('node:fs');
const path = require('node:path');

const repo = path.resolve(__dirname, '..', '..');
const PAGE = path.join(repo, 'Pages', 'Tools', 'Collage', 'Index.cshtml');
const PARTIALS = { _PrismConfirmDialog: path.join(repo, 'Pages', 'Shared', '_PrismConfirmDialog.cshtml') };

function withoutComments(text) {
  return text.replace(/@\*[\s\S]*?\*@/g, '');
}

/** Removes "@{ ... }" code blocks, matching braces. */
function withoutCodeBlocks(text) {
  let out = text;
  for (let start = out.indexOf('@{'); start >= 0; start = out.indexOf('@{')) {
    let depth = 0;
    let end = start + 1;
    for (; end < out.length; end++) {
      if (out[end] === '{') depth++;
      else if (out[end] === '}' && --depth === 0) break;
    }
    out = out.slice(0, start) + out.slice(end + 1);
  }
  return out;
}

/** Keeps or drops "@if (Model.LibraryOn) { ... }" blocks. */
function resolveLibraryBlocks(text, libraryOn) {
  const marker = /@if \(Model\.LibraryOn\)\s*\{/;
  let out = text;
  for (let match = out.match(marker); match; match = out.match(marker)) {
    const open = match.index + match[0].length - 1;
    let depth = 0;
    let close = open;
    for (; close < out.length; close++) {
      if (out[close] === '{') depth++;
      else if (out[close] === '}' && --depth === 0) break;
    }
    const inner = out.slice(open + 1, close);
    out = out.slice(0, match.index) + (libraryOn ? inner : '') + out.slice(close + 1);
  }
  return out;
}

function buildCollagePage({ libraryOn = true } = {}) {
  let html = fs.readFileSync(PAGE, 'utf8').replace(/\r\n/g, '\n');
  html = html.replace(/^@page\s*$/m, '').replace(/^@model .*$/m, '');
  html = withoutComments(html);
  html = withoutCodeBlocks(html);
  html = resolveLibraryBlocks(html, libraryOn);
  html = html.replace(/<partial name="(\w+)" \/>/g, (all, name) => {
    if (!PARTIALS[name]) throw new Error(`Unknown partial ${name} in the Collage page`);
    return withoutComments(fs.readFileSync(PARTIALS[name], 'utf8').replace(/\r\n/g, '\n'));
  });
  html = html
    .replace(/@Url\.Content\("~\/([^"]*)"\)/g, '/$1')
    .replace(/@\(Model\.LibraryOn \? "on" : "off"\)/g, libraryOn ? 'on' : 'off')
    .replace(/ asp-page="\/Dashboard\/Index"/g, ' href="/Dashboard"')
    .replace(/ asp-append-version="true"/g, '')
    .replace(/(src|href)="~\//g, '$1="/');

  // Anything Razor left over means the page grew a feature this builder does not know.
  const leftover = html.split('\n').map((line, index) => [index + 1, line])
    .filter(([, line]) => /@[\w({*]/.test(line.replace(/[\w.+-]+@[\w-]+\.[\w.]+/g, '')) || /\basp-[a-z-]+=/.test(line) || /="~\//.test(line));
  if (leftover.length) {
    throw new Error(`The Collage page uses Razor the browser tests do not understand:\n${leftover.map(([n, l]) => `  ${n}: ${l.trim()}`).join('\n')}`);
  }
  return html;
}

module.exports = { buildCollagePage };

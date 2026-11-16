'use strict';

// A small static server for the Collage Studio browser tests: the page built from the real
// Razor page, PRISM's own files from wwwroot, and the collage API's answers, which a test can
// replace with page.route(). Every answer carries PRISM's production security headers, and the
// API answers are PRISM's own; both are read from tests/browser/fixtures, which a .NET test keeps
// equal to PRISM (ProjectManagement.Tests/Tools/CollageBrowserFixtureTests.cs). Nothing here
// talks to the Internet.

const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const { buildCollagePage } = require('./collage-page');

const port = Number(process.env.COLLAGE_TEST_PORT || 7399);
const wwwroot = path.resolve(__dirname, '..', '..', 'wwwroot');
const TYPES = {
  '.js': 'text/javascript', '.css': 'text/css', '.html': 'text/html', '.svg': 'image/svg+xml',
  '.png': 'image/png', '.jpg': 'image/jpeg', '.ico': 'image/x-icon', '.woff2': 'font/woff2',
  '.woff': 'font/woff', '.json': 'application/json', '.map': 'application/json', '.txt': 'text/plain'
};

const pages = {
  '/Tools/Collage': () => buildCollagePage({ libraryOn: true }),
  '/Tools/Collage/no-library': () => buildCollagePage({ libraryOn: false }),
  // Where PRISM's cookie sign-in sends a request after the session has ended: a page, not an
  // answer. Tests reach it by redirecting a mocked answer here (collage.session.spec.js).
  '/Identity/Account/Login': () => '<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Sign in</title></head><body><form method="post"></form></body></html>',
  // Where it sends a signed-in user who may not see a page (a photo no longer theirs to see).
  '/Identity/Account/AccessDenied': () => '<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Access denied</title></head><body><p>Access denied</p></body></html>'
};

const fixtures = path.join(__dirname, 'fixtures');
const productionHeaders = JSON.parse(fs.readFileSync(path.join(fixtures, 'production-headers.json'), 'utf8'));
const samples = JSON.parse(fs.readFileSync(path.join(fixtures, 'collage-api-samples.json'), 'utf8'));

const api = {
  '/api/collage/stickers': samples.stickers,
  '/api/collage/library-photos': samples.libraryOff
};

// The crest in the samples, served as PRISM serves a sticker's picture: its stored PNG
// (fixtures/crest.png) with the API's own headers and the production security headers.
const crest = fs.readFileSync(path.join(fixtures, 'crest.png'));
const stickerPictures = new Map(samples.stickers.stickers.map((sticker) => [`/api/collage/stickers/${sticker.id}/image`, crest]));

/** The headers of one answer: its own, and PRISM's production security headers. */
const headers = (own) => ({ ...productionHeaders, ...own });

http.createServer((request, response) => {
  const url = new URL(request.url, `http://127.0.0.1:${port}`);
  if (pages[url.pathname]) {
    response.writeHead(200, headers({ 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' }));
    response.end(pages[url.pathname]());
    return;
  }
  if (stickerPictures.has(url.pathname)) {
    response.writeHead(200, headers({ ...samples.stickerPicture }));
    response.end(stickerPictures.get(url.pathname));
    return;
  }
  if (api[url.pathname]) {
    response.writeHead(200, headers({ 'Content-Type': 'application/json', 'Cache-Control': 'no-store' }));
    response.end(JSON.stringify(api[url.pathname]));
    return;
  }
  const file = path.normalize(path.join(wwwroot, decodeURIComponent(url.pathname)));
  if (!file.startsWith(wwwroot + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
    response.writeHead(404, headers({}));
    response.end();
    return;
  }
  response.writeHead(200, headers({ 'Content-Type': TYPES[path.extname(file).toLowerCase()] || 'application/octet-stream' }));
  fs.createReadStream(file).pipe(response);
}).listen(port, '127.0.0.1', () => {
  console.log(`Collage test server on http://127.0.0.1:${port}/Tools/Collage`);
});

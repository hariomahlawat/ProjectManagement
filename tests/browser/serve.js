'use strict';

// A small static server for the Collage Studio browser tests: the page built from the real
// Razor page, PRISM's own files from wwwroot, and plain answers for the collage API, which a
// test can replace with page.route(). Nothing here talks to the Internet.

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
  '/Tools/Collage/no-library': () => buildCollagePage({ libraryOn: false })
};

const api = {
  '/api/collage/stickers': { stickers: [] },
  '/api/collage/library-photos': { available: false, message: 'PRISM Photos is not switched on. Add photos from your computer.', photos: [], page: 1, hasMore: false }
};

http.createServer((request, response) => {
  const url = new URL(request.url, `http://127.0.0.1:${port}`);
  if (pages[url.pathname]) {
    response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' });
    response.end(pages[url.pathname]());
    return;
  }
  if (api[url.pathname]) {
    response.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
    response.end(JSON.stringify(api[url.pathname]));
    return;
  }
  const file = path.normalize(path.join(wwwroot, decodeURIComponent(url.pathname)));
  if (!file.startsWith(wwwroot + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
    response.writeHead(404);
    response.end();
    return;
  }
  response.writeHead(200, { 'Content-Type': TYPES[path.extname(file).toLowerCase()] || 'application/octet-stream' });
  fs.createReadStream(file).pipe(response);
}).listen(port, '127.0.0.1', () => {
  console.log(`Collage test server on http://127.0.0.1:${port}/Tools/Collage`);
});

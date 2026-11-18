// PRISM Collage Studio — printing cards: print sheets with cut marks, and guest lists
// (docs/ux/invitations-posters.md, PRs 9 and 10).
//
// Pure rules, tested in Node: how many cards of a size fit an A4 or A3 sheet and where each goes
// (fronts and their backs), the cut marks, where each card lands on a PDF page, and reading the
// guest list a user pastes or gives as a file. Nothing here touches the page or the network.
(function (root, factory) {
  'use strict';
  const api = factory((root && root.PrismCollage) || (typeof require === 'function' ? require('./collage-core.js') : null));
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.PrismCollagePrint = api;
})(typeof self !== 'undefined' ? self : typeof globalThis !== 'undefined' ? globalThis : this, function (core) {
  'use strict';

  // A table looked up by a name from a draft or a file: no prototype, so "constructor" or
  // "toString" is never a sheet.
  const lookupTable = (entries) => Object.freeze(Object.assign(Object.create(null), entries));

  // ---------------------------------------------------------------------------
  // Print sheets
  // ---------------------------------------------------------------------------

  // Sheets for printing several cards. "Edge to edge" packs cards touching, with no margin, gaps
  // or cut marks: more to a sheet (4 A6 or 2 A5 on A4), cut on the card edges, where a printer
  // that cannot print to the paper's edge leaves a margin or shrinks the page, so it is for
  // printers that print to the edge.
  const SHEETS = lookupTable({
    a4: Object.freeze({ label: 'A4', mm: [210, 297] }),
    a3: Object.freeze({ label: 'A3', mm: [297, 420] }),
    a4edge: Object.freeze({ label: 'A4, edge to edge', mm: [210, 297], margin: 0, gap: 0, edge: true })
  });
  const SHEET_SPACE = Object.freeze({ margin: 8, gap: 4, mark: 2.5 });

  /**
   * Print sheets (docs/ux/invitations-posters.md, PR 9): as many cards of a size as fit on an A4
   * or A3 sheet, with room round them for cutting. The sheet is turned whichever way holds more
   * (upright on a tie). Everything is in mm from the sheet's top-left; `cut` marks sit in the
   * gaps, never on a card. A back sheet is mirrored so each back lands behind its front when the
   * printer flips on the long edge: across for an upright sheet, top to bottom for a turned one.
   */
  function sheetLayout(sizeKey, sheetKey, { back = false } = {}) {
    const size = core.sizeOf(sizeKey);
    const sheet = SHEETS[sheetKey];
    if (!size.mm || !sheet) return null;
    const [cw, ch] = size.mm;
    const margin = sheet.margin ?? SHEET_SPACE.margin;
    const gap = sheet.gap ?? SHEET_SPACE.gap;
    const fit = ([sw, sh]) => {
      const cols = Math.max(0, Math.floor((sw - 2 * margin + gap) / (cw + gap)));
      const rows = Math.max(0, Math.floor((sh - 2 * margin + gap) / (ch + gap)));
      return { sw, sh, cols, rows, count: cols * rows };
    };
    const upright = fit(sheet.mm);
    const turned = fit([sheet.mm[1], sheet.mm[0]]);
    const best = turned.count > upright.count ? turned : upright;
    if (!best.count) return { sheet: sheet.mm, upright: true, cols: 0, rows: 0, count: 0, cards: [] };
    const blockW = best.cols * cw + (best.cols - 1) * gap;
    const blockH = best.rows * ch + (best.rows - 1) * gap;
    const left = (best.sw - blockW) / 2;
    const top = (best.sh - blockH) / 2;
    const isUpright = best === upright;
    const cards = [];
    for (let row = 0; row < best.rows; row += 1) {
      for (let col = 0; col < best.cols; col += 1) {
        // Behind its front after a long-edge flip.
        const c = back && isUpright ? best.cols - 1 - col : col;
        const r = back && !isUpright ? best.rows - 1 - row : row;
        cards.push({ x: left + c * (cw + gap), y: top + r * (ch + gap), w: cw, h: ch });
      }
    }
    return { sheet: [best.sw, best.sh], upright: isUpright, cols: best.cols, rows: best.rows, count: best.count, cards, ...(sheet.edge ? { edge: true } : {}) };
  }

  /** PDF points in a mm. */
  const PT_PER_MM = 72 / 25.4;

  /**
   * Where each card of a sheet layout goes on a PDF page, in points from the bottom left, and
   * whether it is turned. A sheet's back lines up with its front when the printer flips on the
   * long edge. On a turned (landscape) sheet that flip turns the page over its top edge, so each
   * back is drawn turned 180°: then a cut card turned over like a book page reads upright, as on
   * an upright sheet. `rotate` is pdf-lib's: about (x, y), so a turned card's (x, y) is the
   * slot's top right corner.
   */
  function sheetPlacements(layout, back = false) {
    const [, sh] = layout.sheet;
    const turn = back && !layout.upright;
    return layout.cards.map((slot) => (turn
      ? { x: (slot.x + slot.w) * PT_PER_MM, y: (sh - slot.y) * PT_PER_MM, width: slot.w * PT_PER_MM, height: slot.h * PT_PER_MM, rotate: 180 }
      : { x: slot.x * PT_PER_MM, y: (sh - slot.y - slot.h) * PT_PER_MM, width: slot.w * PT_PER_MM, height: slot.h * PT_PER_MM, rotate: 0 }));
  }

  /** Cut marks round each card of a sheet: short lines in the gaps at each corner, in mm. */
  function cutMarks(layout) {
    // Cards edge to edge have no gaps to mark in: they are cut on their edges.
    if (layout?.edge) return [];
    const { mark } = SHEET_SPACE;
    const lines = [];
    for (const card of layout?.cards || []) {
      for (const [x, y] of [[card.x, card.y], [card.x + card.w, card.y], [card.x, card.y + card.h], [card.x + card.w, card.y + card.h]]) {
        const sx = x === card.x ? -1 : 1;
        const sy = y === card.y ? -1 : 1;
        lines.push([x, y + sy * 1, x, y + sy * (1 + mark)]);
        lines.push([x + sx * 1, y, x + sx * (1 + mark), y]);
      }
    }
    return lines;
  }

  // ---------------------------------------------------------------------------
  // Guest lists
  // ---------------------------------------------------------------------------

  /**
   * Guest names from pasted text or a .txt file (one a whole line, commas and all), or a .csv file
   * (`csv`: the rank and name columns when the first row heads them, else the first column);
   * blanks and a heading line left out, each trimmed to core.GUEST_LIMITS.length, at most
   * core.GUEST_LIMITS.names. See readGuests.
   */
  function guestNames(text, opts) {
    return readGuests(text, opts).names;
  }

  /**
   * The rows of a CSV, each a list of cells: a quoted cell may hold the separator, a line break
   * (an address) or a doubled quote for a quote.
   */
  function csvRows(text, separator = ',') {
    const rows = [];
    let row = [];
    let cell = '';
    let quoted = false;
    const source = String(text || '');
    for (let i = 0; i < source.length; i += 1) {
      const ch = source[i];
      if (quoted) {
        if (ch === '"' && source[i + 1] === '"') { cell += '"'; i += 1; }
        else if (ch === '"') quoted = false;
        else cell += ch;
      } else if (ch === '"' && !cell.trim()) { cell = ''; quoted = true; }
      else if (ch === separator) { row.push(cell); cell = ''; }
      else if (ch === '\n' || ch === '\r') {
        if (ch === '\r' && source[i + 1] === '\n') i += 1;
        row.push(cell);
        rows.push(row);
        row = [];
        cell = '';
      } else cell += ch;
    }
    row.push(cell);
    rows.push(row);
    // A quote opened and never closed (a stray " in a name) would swallow every later line: read
    // the lines again with each one's own cells, quotes kept as they are.
    if (quoted) return source.split(/\r?\n/).map((line) => line.split(separator));
    return rows;
  }

  // Headings in a guest list's first row: the columns that make up a guest's name, in order.
  // A rank column joins the name; a column whose heading names a person ("Name", "Rank & Name",
  // "Name of Guest", "Surname"), not a unit or a place, is a name column.
  // An appointment ("Cdr 50 Bde") is not part of a name on the card; a spouse's column joins the
  // name with "and" ("Brig P. Singh and Mrs Singh").
  const RANK_HEADING = /^\s*(rank|title)\s*$/i;
  const NAME_WORDS = /\bnames?\b|\bsurname\b|^\s*guests?\s*$|^\s*initials?\s*$/i;
  const NOT_A_PERSON = /\b(unit|regiment|formation|organi[sz]ation|station|place|venue|file|company|establishment|appointment)\b/i;
  const SPOUSE_HEADING = /\b(spouse|wife|husband|partner)\b/i;
  // Honours and awards follow the name after a comma ("Lt Gen A. Kumar, PVSM, AVSM").
  const HONOURS_HEADING = /\b(honours|honors|awards|decorations|post-?nominals)\b/i;
  const NAME_HEADING = { test: (cell) => NAME_WORDS.test(cell) && !NOT_A_PERSON.test(cell) && !SPOUSE_HEADING.test(cell) };

  /**
   * The guest list read from text, one name a line, with how many names did not fit (past
   * core.GUEST_LIMITS.names). In a CSV whose first row has headings, a guest's name is made from the
   * rank and name columns in their order ("Rank, Name, Unit" gives "Brig P. Singh, SM"; "First
   * name, Surname" gives both); with no headings, it is the first column. A first line that is a
   * heading ("Name", "Guests") is left out. `heading: false` reads every line as a name (a list
   * kept in a draft, where a guest may be called anything).
   */
  function readGuests(text, { csv = false, heading = true } = {}) {
    const source = String(text || '');
    const head = source.split(/\r?\n/).find((line) => line.trim()) || '';
    // Columns pasted from a spreadsheet come with tabs between them; a CSV saved with semicolons
    // (as some spreadsheets do) has them when its first row has no comma.
    const pasted = !csv && heading && source.includes('\t');
    // Pasted text whose first line heads columns with commas ("Name, Unit") is read as a CSV.
    const headedText = !csv && !pasted && heading && head.includes(',') && head.split(',').some((cell) => NAME_HEADING.test(cell));
    const asCsv = csv || headedText;
    const separator = pasted ? '\t' : asCsv && head.includes(';') && !head.includes(',') ? ';' : ',';
    const rows = (asCsv || pasted ? csvRows(source, separator) : source.split(/\r?\n/).map((line) => [line]))
      .filter((cells) => cells.some((cell) => cell.trim()));
    let columns = [0];
    let spouse = -1;
    let honours = -1;
    let headed = false;
    let width = 1;
    if (heading && rows.length) {
      const named = rows[0].map((cell, i) => (NAME_HEADING.test(cell) || RANK_HEADING.test(cell) ? i : -1)).filter((i) => i >= 0);
      if (named.some((i) => NAME_HEADING.test(rows[0][i]))) {
        columns = named;
        spouse = rows[0].findIndex((cell) => SPOUSE_HEADING.test(cell));
        honours = rows[0].findIndex((cell) => HONOURS_HEADING.test(cell));
        width = rows[0].length;
        headed = true;
        rows.shift();
      }
    }
    // With no headings, only the first column is a name; the list says when others were there.
    const firstColumnOnly = !headed && rows.some((cells) => cells.slice(1).some((cell) => cell.trim()));
    // A row with more cells than headings had a comma inside a name without quotes ("Col X, VSM"):
    // the list says so, as the part after the comma is not read as the name.
    const extraCells = headed && rows.some((cells) => cells.length > width);
    const names = [];
    let over = 0;
    for (const cells of rows) {
      const person = columns.map((i) => cells[i] ?? '').join(' ').replace(/\s+/g, ' ').trim();
      const awards = honours >= 0 ? String(cells[honours] ?? '').replace(/\s+/g, ' ').trim() : '';
      const main = person && awards ? `${person}, ${awards}` : person;
      const partner = spouse >= 0 ? String(cells[spouse] ?? '').replace(/\s+/g, ' ').trim() : '';
      const name = (main && partner ? `${main} and ${partner}` : main).slice(0, core.GUEST_LIMITS.length);
      if (!name) continue;
      if (names.length >= core.GUEST_LIMITS.names) { over += 1; continue; }
      names.push(name);
    }
    return { names, over, firstColumnOnly, extraCells };
  }

  return Object.freeze({ SHEETS, SHEET_SPACE, sheetLayout, sheetPlacements, PT_PER_MM, cutMarks, csvRows, readGuests, guestNames });
});

// Printing cards: print sheets with cut marks, and guest lists (collage-print.js;
// docs/ux/invitations-posters.md, PRs 9 and 10).
const test = require('node:test');
const assert = require('node:assert/strict');
const collage = require('./collage-core.js');
const print = require('./collage-print.js');

// Print sheets (docs/ux/invitations-posters.md, PR 9).
test('cards go as many to an A4 or A3 sheet as fit, turned whichever way holds more, with cut marks in the gaps', () => {
  const count = (size, sheet) => print.sheetLayout(size, sheet).count;
  assert.deepEqual([count('a6p', 'a4'), count('a7p', 'a4'), count('passl', 'a4'), count('a5p', 'a4'), count('a5p', 'a3')], [2, 4, 10, 1, 2]);
  assert.equal(print.sheetLayout('a4p', 'a4').count, 0, 'an A4 page does not fit inside an A4 sheet with margins');
  assert.equal(print.sheetLayout('slide', 'a4'), null, 'a screen size has no paper size');
  // A6 upright holds two side by side only on a turned A4.
  const a6 = print.sheetLayout('a6p', 'a4');
  assert.deepEqual([a6.upright, a6.sheet, a6.cols, a6.rows], [false, [297, 210], 2, 1]);
  // Every card on the sheet, inside the margins, none overlapping.
  for (const [size, sheet] of [['passl', 'a4'], ['a7l', 'a3'], ['sq5', 'a4']]) {
    const layout = print.sheetLayout(size, sheet);
    for (const c of layout.cards) {
      assert.ok(c.x >= print.SHEET_SPACE.margin - 1e-9 && c.y >= print.SHEET_SPACE.margin - 1e-9, size);
      assert.ok(c.x + c.w <= layout.sheet[0] - print.SHEET_SPACE.margin + 1e-9 && c.y + c.h <= layout.sheet[1] - print.SHEET_SPACE.margin + 1e-9, size);
    }
    for (let i = 0; i < layout.cards.length; i += 1) for (let j = i + 1; j < layout.cards.length; j += 1) {
      const a = layout.cards[i];
      const b = layout.cards[j];
      assert.ok(a.x + a.w <= b.x + 1e-9 || b.x + b.w <= a.x + 1e-9 || a.y + a.h <= b.y + 1e-9 || b.y + b.h <= a.y + 1e-9, `${size}: cards overlap`);
    }
    // Cut marks never cross a card.
    for (const [x1, y1, x2, y2] of print.cutMarks(layout)) {
      for (const c of layout.cards) {
        const inside = (x, y) => x > c.x + 1e-9 && x < c.x + c.w - 1e-9 && y > c.y + 1e-9 && y < c.y + c.h - 1e-9;
        assert.ok(!inside(x1, y1) && !inside(x2, y2) && !inside((x1 + x2) / 2, (y1 + y2) / 2), `${size}: a cut mark crosses a card`);
      }
    }
  }
  // The back sheet lines up after a long-edge flip: mirrored across an upright sheet, top to bottom on a turned one.
  const front = print.sheetLayout('passl', 'a4');
  const back = print.sheetLayout('passl', 'a4', { back: true });
  assert.equal(front.upright, true);
  assert.equal(back.cards[0].x, front.cards[front.cols - 1].x);
  assert.equal(back.cards[0].y, front.cards[0].y);
  const turnedFront = print.sheetLayout('a6p', 'a4');
  const turnedBack = print.sheetLayout('a6p', 'a4', { back: true });
  assert.deepEqual(turnedBack.cards.map((c) => c.x), turnedFront.cards.map((c) => c.x), 'one row: a top-to-bottom flip keeps the columns');
});

// The guest list (docs/ux/invitations-posters.md, PR 10).
test('guest names come one a line from pasted text, or the first column of a CSV, within limits', () => {
  assert.deepEqual(print.guestNames('Name\nLt Gen R. K. Mehta, AVSM\n\n  Maj A and   Mrs B  '), ['Lt Gen R. K. Mehta, AVSM', 'Maj A and Mrs B']);
  assert.deepEqual(print.guestNames('Name,Email\n"Brig P. Singh, SM",x@y\nCol D. Nair,col@x\r\n"Say ""Hi""",z', { csv: true }), ['Brig P. Singh, SM', 'Col D. Nair', 'Say "Hi"']);
  const many = Array.from({ length: 400 }, (_, i) => `Guest ${i}`).join('\n');
  assert.equal(print.guestNames(many).length, collage.GUEST_LIMITS.names);
  assert.equal(print.guestNames('x'.repeat(500))[0].length, collage.GUEST_LIMITS.length);
  // Names past the limit are counted, so the list can say how many were left out.
  assert.deepEqual(print.readGuests(many).over, 400 - collage.GUEST_LIMITS.names);
  assert.equal(print.readGuests('Maj A').over, 0);
  // A CSV whose first row heads a Name column takes that column, not the rank before it.
  assert.deepEqual(print.guestNames('Rank,Name,Unit\nBrig,"P. Singh, SM",5 Sikh\nCol,D. Nair,', { csv: true }), ['Brig P. Singh, SM', 'Col D. Nair']);
  assert.deepEqual(print.guestNames('Rank;Guest name' + String.fromCharCode(10) + 'Brig;P. Singh, SM', { csv: true }), ['Brig P. Singh, SM']);
  assert.deepEqual(print.guestNames('Guests' + String.fromCharCode(10) + 'Maj A' + String.fromCharCode(10) + 'Capt B'), ['Maj A', 'Capt B']);
  // First name and surname are joined; a quoted address with a line break stays in its cell.
  assert.deepEqual(print.guestNames('First name,Surname,Address'+String.fromCharCode(10)+'Ravi,Kumar,"12 Mall Road'+String.fromCharCode(10)+'Delhi"'+String.fromCharCode(10)+'Asha,Rao,x', { csv: true }), ['Ravi Kumar', 'Asha Rao']);
  // A draft keeps a guest called anything, even "Name".
  const kept = collage.readDraftModel({ version: 1, size: 'a5p', items: [], card: { design: 'classic', details: {}, guests: ['Name', 'Guests', 'Maj B'] } }, []);
  assert.deepEqual(kept.card.guests, ['Name', 'Guests', 'Maj B']);
  assert.deepEqual(print.guestNames('Full name\nMaj A', { csv: true }), ['Maj A']);
  // A draft keeps the list, cleaned the same way.
  const read = collage.readDraftModel({ version: 1, size: 'a5p', items: [], card: { design: 'classic', details: {}, guests: ['Col D. Nair', '', 7, 'Maj B'] } }, []);
  assert.deepEqual(read.card.guests, ['Col D. Nair', 'Maj B']);
  assert.equal(collage.readDraftModel({ version: 1, size: 'a5p', items: [], card: { design: 'classic', details: {} } }, []).card.guests, undefined);
});

// Backs on print sheets (cold review 2): on a turned sheet each back is turned 180°, so a card
// turned over like a book page reads upright; on an upright sheet nothing is turned.
test('sheet placements turn the backs on a turned sheet, and each card still fills its slot', () => {
  const pt = print.PT_PER_MM;
  const turned = print.sheetLayout('a6p', 'a4', { back: true });
  assert.equal(turned.upright, false, 'A6 portrait goes two to a turned A4');
  const backs = print.sheetPlacements(turned, true);
  const [, sh] = turned.sheet;
  backs.forEach((place, i) => {
    const slot = turned.cards[i];
    assert.equal(place.rotate, 180);
    // Turned about (x, y), the card covers x - width … x and y - height … y.
    assert.ok(Math.abs(place.x - place.width - slot.x * pt) < 0.01);
    assert.ok(Math.abs(place.y - place.height - (sh - slot.y - slot.h) * pt) < 0.01);
  });
  assert.ok(print.sheetPlacements(print.sheetLayout('a6p', 'a4'), false).every((place) => place.rotate === 0), 'fronts are never turned');
  const upright = print.sheetLayout('a7p', 'a4', { back: true });
  assert.equal(upright.upright, true);
  assert.ok(print.sheetPlacements(upright, true).every((place) => place.rotate === 0), 'an upright sheet keeps its backs upright');
});

// Print sheet geometry, exactly (cold review 2): margins and gaps, the back's rows mirrored on a
// turned sheet of several rows, and cut marks at the card corners in the gaps.
test('sheet layout: 8 mm margins, 4 mm gaps, mirrored rows on a turned sheet, cut marks at the corners', () => {
  const front = print.sheetLayout('a7l', 'a4');
  const back = print.sheetLayout('a7l', 'a4', { back: true });
  assert.equal(front.upright, false);
  assert.equal(front.rows, 2);
  assert.equal(front.cols, 2);
  // The gap between neighbours is 4 mm, and the cards sit centred inside 8 mm margins.
  const [a, b] = [front.cards[0], front.cards[1]];
  assert.ok(Math.abs(b.x - (a.x + a.w) - 4) < 1e-6, 'a 4 mm gap across');
  assert.ok(a.x >= 8 - 1e-6 && a.y >= 8 - 1e-6, 'inside the margins');
  const rowGap = front.cards[2].y - (front.cards[0].y + front.cards[0].h);
  assert.ok(Math.abs(rowGap - 4) < 1e-6, 'a 4 mm gap down');
  // On a turned sheet the back's rows swap (top to bottom), the columns stay.
  back.cards.forEach((slot, i) => {
    const mirrored = front.cards[(1 - Math.floor(i / 2)) * 2 + (i % 2)];
    assert.deepEqual([slot.x, slot.y], [mirrored.x, mirrored.y], `back slot ${i}`);
  });
  // Every cut mark lies outside every card, and each card corner has its marks.
  const marks = print.cutMarks(front);
  const inside = (x, y) => front.cards.some((c) => x > c.x + 1e-6 && x < c.x + c.w - 1e-6 && y > c.y + 1e-6 && y < c.y + c.h - 1e-6);
  for (const [x1, y1, x2, y2] of marks) assert.ok(!inside((x1 + x2) / 2, (y1 + y2) / 2));
  for (const c of front.cards) {
    for (const [cx, cy] of [[c.x, c.y], [c.x + c.w, c.y], [c.x, c.y + c.h], [c.x + c.w, c.y + c.h]]) {
      assert.ok(marks.some(([x1, y1, x2, y2]) => (Math.abs(x1 - cx) < 1e-6 && Math.abs(x2 - cx) < 1e-6) || (Math.abs(y1 - cy) < 1e-6 && Math.abs(y2 - cy) < 1e-6)), 'a mark lines up with each corner');
    }
  }
});

// Columns pasted from a spreadsheet (cold review 2): with a heading row, rank and name are joined;
// without one, only the first column is used, and the list says so.
test('names pasted from a spreadsheet: columns read by their headings, else the first, and said', () => {
  const tab = String.fromCharCode(9);
  const nl = String.fromCharCode(10);
  const headed = print.readGuests(['Rank' + tab + 'Name' + tab + 'Unit', 'Brig' + tab + 'P. Singh, SM' + tab + 'HQ'].join(nl));
  assert.deepEqual(headed.names, ['Brig P. Singh, SM']);
  assert.equal(headed.firstColumnOnly, false);
  const plain = print.readGuests(['Brig P. Singh, SM and Mrs Singh' + tab + 'HQ', 'Col D. Nair' + tab + '5 Sikh'].join(nl));
  assert.deepEqual(plain.names, ['Brig P. Singh, SM and Mrs Singh', 'Col D. Nair']);
  assert.equal(plain.firstColumnOnly, true);
  // One name a line, commas and all, stays as it was.
  assert.deepEqual(print.readGuests('Brig P. Singh, SM' + nl + 'Col D. Nair').names, ['Brig P. Singh, SM', 'Col D. Nair']);
  assert.equal(print.readGuests('Brig P. Singh, SM').firstColumnOnly, false);
});

// Edge to edge (cold review 2): cards touching, no margin or gaps, so more fit on A4; no cut marks.
test('edge to edge on A4 fits 4 A6 and 2 A5, touching, with no cut marks', () => {
  const a6 = print.sheetLayout('a6p', 'a4edge');
  assert.equal(a6.count, 4);
  assert.equal(a6.edge, true);
  assert.deepEqual(print.cutMarks(a6), []);
  const [a, b] = a6.cards;
  assert.ok(Math.abs(b.x - (a.x + a.w)) < 1e-6, 'touching');
  assert.equal(print.sheetLayout('a5p', 'a4edge').count, 2);
  assert.equal(print.sheetLayout('a7p', 'a4edge').count, 8);
  // Backs still line up with their fronts.
  const back = print.sheetLayout('a6p', 'a4edge', { back: true });
  assert.equal(back.cards.length, 4);
  assert.equal(print.sheetLayout('a6p', 'a4').count, 2, 'with room for cutting, 2');
});

// Guest list headings as units write them (cold review 3): "Ser No, Rank & Name, Unit" and
// "S.No, Rank, Name of Guest" give names, never serial numbers; a stray quote spoils one line only.
test('guest list headings with serial numbers, "Rank & Name" and "Name of Guest"; a stray quote', () => {
  const nl = String.fromCharCode(10);
  assert.deepEqual(print.guestNames(['Ser No,Rank & Name,Unit', '1,Brig P. Singh,HQ', '2,Col D. Nair,5 Sikh'].join(nl), { csv: true }), ['Brig P. Singh', 'Col D. Nair']);
  assert.deepEqual(print.guestNames(['S.No,Rank,Name of Guest', '1,Maj,A. Rao'].join(nl), { csv: true }), ['Maj A. Rao']);
  // A unit's name is not a guest's.
  assert.deepEqual(print.guestNames(['Name,Unit Name', 'Col D. Nair,5 Sikh'].join(nl), { csv: true }), ['Col D. Nair']);
  // An unclosed quote: each line still a guest of its own.
  assert.deepEqual(print.guestNames(['Name', '"Maj A', 'Capt B', 'Lt C'].join(nl), { csv: true }), ['"Maj A', 'Capt B', 'Lt C']);
});

// Cut marks exactly (cold review 3): both marks at every card corner, 1 mm out from it and
// SHEET_SPACE.mark long; the cards centred on the sheet.
test('each card corner has both cut marks, 1 mm out, and the cards are centred', () => {
  const layout = print.sheetLayout('a6p', 'a4');
  const marks = print.cutMarks(layout);
  assert.equal(marks.length, layout.cards.length * 8);
  const { mark } = print.SHEET_SPACE;
  for (const card of layout.cards) {
    for (const [cx, cy] of [[card.x, card.y], [card.x + card.w, card.y], [card.x, card.y + card.h], [card.x + card.w, card.y + card.h]]) {
      const vertical = marks.find(([x1, y1, x2, y2]) => Math.abs(x1 - cx) < 1e-9 && Math.abs(x2 - cx) < 1e-9 && Math.abs(Math.min(Math.abs(y1 - cy), Math.abs(y2 - cy)) - 1) < 1e-9);
      const horizontal = marks.find(([x1, y1, x2, y2]) => Math.abs(y1 - cy) < 1e-9 && Math.abs(y2 - cy) < 1e-9 && Math.abs(Math.min(Math.abs(x1 - cx), Math.abs(x2 - cx)) - 1) < 1e-9);
      assert.ok(vertical && Math.abs(Math.abs(vertical[3] - vertical[1]) - mark) < 1e-9, 'a vertical mark at the corner');
      assert.ok(horizontal && Math.abs(Math.abs(horizontal[2] - horizontal[0]) - mark) < 1e-9, 'a horizontal mark at the corner');
    }
  }
  const left = Math.min(...layout.cards.map((c) => c.x));
  const right = Math.max(...layout.cards.map((c) => c.x + c.w));
  const top = Math.min(...layout.cards.map((c) => c.y));
  const bottom = Math.max(...layout.cards.map((c) => c.y + c.h));
  assert.ok(Math.abs(left - (layout.sheet[0] - right)) < 1e-9, 'centred across');
  assert.ok(Math.abs(top - (layout.sheet[1] - bottom)) < 1e-9, 'centred down');
});

// Guest list columns as units keep them (cold review 4): an appointment is not part of the name;
// a spouse joins with "and"; pasted "Name, Unit" text is columns; unquoted commas are reported.
test('guest list: appointments left out, spouses joined, pasted headings read, stray commas said', () => {
  const nl = String.fromCharCode(10);
  assert.deepEqual(print.guestNames(['S No,Rank & Name,Appointment', '1,Brig P. Singh,Cdr 50 Bde'].join(nl), { csv: true }), ['Brig P. Singh']);
  assert.deepEqual(print.guestNames(['Name,Spouse name', 'Brig P Singh,Mrs Singh', 'Col D Nair,'].join(nl), { csv: true }), ['Brig P Singh and Mrs Singh', 'Col D Nair']);
  assert.deepEqual(print.guestNames(['Name, Unit', 'Col D. Nair, 5 Sikh'].join(nl)), ['Col D. Nair']);
  const stray = print.readGuests(['Name,Unit', 'Col X, VSM,5 Sikh'].join(nl), { csv: true });
  assert.equal(stray.extraCells, true);
  assert.equal(print.readGuests(['Name,Unit', '"Col X, VSM",5 Sikh'].join(nl), { csv: true }).extraCells, false);
});

test('an Honours column follows the name after a comma', () => {
  const nl = String.fromCharCode(10);
  assert.deepEqual(print.guestNames(['Rank,Name,Honours', 'Lt Gen,Arvind Kumar,"PVSM, AVSM"', 'Col,D Nair,'].join(nl), { csv: true }), ['Lt Gen Arvind Kumar, PVSM, AVSM', 'Col D Nair']);
});

// Sheet rules pinned exactly (cold review 4): the 8 mm margin decides how many fit, and a tie
// between an upright and a turned sheet stays upright.
test('the 8 mm margin decides the count, and a tie stays upright', () => {
  // 96 mm wide: two across an upright A4 only with 7 mm margins, so 8 mm gives 6 on a turned sheet.
  const tight = print.sheetLayout('mm:96x60', 'a4');
  assert.equal(tight.count, 6);
  assert.equal(tight.upright, false);
  const tie = print.sheetLayout('in45p', 'a4');
  assert.equal(tie.count, 2);
  assert.equal(tie.upright, true, 'upright on a tie');
});

test('a draft keeps at most 300 guests', () => {
  const guests = Array.from({ length: 1000 }, (_, i) => `Guest ${i + 1}`);
  const read = collage.readDraftModel({ version: 1, size: 'a5p', items: [], card: { design: 'classic', details: {}, guests } }, []);
  assert.equal(read.card.guests.length, 300);
  assert.equal(read.card.guests[299], 'Guest 300');
});

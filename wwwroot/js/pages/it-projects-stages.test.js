const assert = require('assert');
const Chain = require('./it-projects-stages.js');

function test(name, fn) {
  try {
    fn();
    console.log(`PASS ${name}`);
  } catch (error) {
    console.error(`FAIL ${name}`);
    console.error(error.message);
    process.exitCode = 1;
  }
}

const codes = ['PPP', 'AON', 'BID', 'TEC', 'COB', 'BM', 'PNC', 'SO', 'DLV'];
const labels = { PPP: 'IT PPP / IPA', AON: 'AoN', BID: 'Bid', TEC: 'TEC', COB: 'COB', BM: 'Benchmarking', PNC: 'PNC', SO: 'SO', DLV: 'Delivery and ATP' };

function fresh(overrides) {
  return codes.map(code => Object.assign(
    { code, label: labels[code], completed: false, date: null, skipped: code === 'BM' || code === 'PNC' },
    (overrides && overrides[code]) || {}));
}

const done = states => states.filter(s => s.completed).map(s => s.code);

test('ticking a later stage completes the earlier non-skipped ones', () => {
  const next = Chain.tick(fresh(), 3, true); // TEC
  assert.deepStrictEqual(done(next), ['PPP', 'AON', 'BID', 'TEC']);
  assert.strictEqual(next[5].completed, false); // BM stays skipped
});

test('unticking a stage reopens every later stage and clears their dates', () => {
  const start = Chain.chain(fresh({ SO: { completed: true, date: '2026-03-04' }, BID: { date: '2025-09-29' } }));
  assert.deepStrictEqual(done(start), ['PPP', 'AON', 'BID', 'TEC', 'COB', 'SO']);
  const next = Chain.tick(start, 4, false); // COB: TEC and everything before it stay done
  assert.deepStrictEqual(done(next), ['PPP', 'AON', 'BID', 'TEC']);
  assert.strictEqual(next[7].date, null);
  assert.strictEqual(next[2].date, '2025-09-29');

  const further = Chain.tick(next, 3, false); // TEC
  assert.deepStrictEqual(done(further), ['PPP', 'AON', 'BID']);
});

test('a date means done', () => {
  const next = Chain.setDate(fresh(), 1, '2026-06-19'); // AoN
  assert.deepStrictEqual(done(next), ['PPP', 'AON']);
  assert.strictEqual(next[1].date, '2026-06-19');
});

test('including an optional stage puts it into the chain; skipping clears it', () => {
  const included = Chain.setSkipped(Chain.tick(fresh(), 7, true), 6, false); // SO done, include PNC
  assert.strictEqual(included[6].skipped, false);
  assert.strictEqual(included[6].completed, true);
  assert.strictEqual(included[6].date, null);

  const skipped = Chain.setSkipped(included, 6, true);
  assert.strictEqual(skipped[6].skipped, true);
  assert.strictEqual(skipped[6].completed, false);
  assert.deepStrictEqual(done(skipped), ['PPP', 'AON', 'BID', 'TEC', 'COB', 'SO']);
});

test('cascade names only the stages the user did not touch', () => {
  const saved = Chain.chain(fresh({ AON: { completed: true } }));
  const current = Chain.tick(saved, 3, true); // user ticks TEC
  const cascade = Chain.cascade(saved, current, { TEC: true });
  assert.deepStrictEqual(cascade, { completed: ['BID'], reopened: [] });
  assert.strictEqual(Chain.summary(cascade, current), 'Saving will also complete Bid.');

  const back = Chain.tick(current, 1, false); // user unticks AoN
  const cascadeBack = Chain.cascade(saved, back, { AON: true });
  assert.deepStrictEqual(cascadeBack, { completed: [], reopened: [] }); // BID/TEC were never saved as done
  assert.strictEqual(Chain.summary(cascadeBack, back), null);
});

test('summary joins several stages with commas and "and"', () => {
  const saved = Chain.chain(fresh());
  const current = Chain.tick(saved, 8, true); // DLV
  const cascade = Chain.cascade(saved, current, { DLV: true });
  assert.strictEqual(
    Chain.summary(cascade, current),
    'Saving will also complete IT PPP / IPA, AoN, Bid, TEC, COB and SO.');
});

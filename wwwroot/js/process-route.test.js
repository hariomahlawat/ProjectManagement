const test = require('node:test');
const assert = require('node:assert/strict');
const Route = require('./process-route.js');

// Mirrors the V2 template in Data/StageFlowSeeder.cs: TEC and BM in parallel before COB,
// PNC as a conditional detour between COB and EAS, ToT as a conditional ending.
const STAGES = [
  ['FS', 10], ['SOW', 20], ['IPA', 30], ['AON', 40], ['BID', 50],
  ['TEC', 60, false, 'PRE_COB'], ['BM', 70, false, 'PRE_COB'], ['COB', 80], ['PNC', 90, true],
  ['EAS', 100], ['SO', 110], ['DEVP', 115], ['ATP', 120], ['PAYMENT', 130], ['TOT', 140, true]
];
const DEPENDENCIES = [
  ['SOW', 'FS'], ['IPA', 'SOW'], ['AON', 'IPA'], ['BID', 'AON'], ['TEC', 'BID'], ['BM', 'BID'],
  ['COB', 'TEC'], ['COB', 'BM'], ['PNC', 'COB'], ['EAS', 'COB'], ['SO', 'EAS'], ['DEVP', 'SO'],
  ['ATP', 'DEVP'], ['PAYMENT', 'ATP'], ['TOT', 'PAYMENT']
];

function v2Route() {
  const dto = {
    version: 'SDD-2.0',
    nodes: STAGES.map(([code, sequence, optional = false, parallelGroup = null]) => ({
      code: code.toLowerCase(), name: `${code} name`, sequence, optional, parallelGroup
    })),
    edges: DEPENDENCIES.map(([from, on]) => ({ source: on, target: from }))
  };
  return Route.buildRoute(Route.normalizeFlow(dto));
}

test('normalizeFlow orders stages by sequence and upper-cases codes', () => {
  const flow = Route.normalizeFlow({
    nodes: [{ code: 'b', name: 'Second', sequence: 20 }, { code: 'a', name: 'First', sequence: 10 }, { code: '' }],
    edges: [{ source: 'a', target: 'b' }, { source: '', target: 'b' }]
  }, 'fallback');
  assert.deepEqual(flow.nodes.map(node => [node.code, node.displayIndex]), [['A', 1], ['B', 2]]);
  assert.equal(flow.edges.length, 1);
  assert.equal(flow.version, 'fallback');
});

test('every stage from the API is placed on the route', () => {
  const route = v2Route();
  assert.equal(route.nodes.length, STAGES.length);
  route.nodes.forEach(node => {
    assert.ok(Number.isFinite(node.column), `${node.code} has a column`);
    assert.ok(Number.isFinite(node.lane), `${node.code} has a lane`);
  });
});

test('parallel stages share a column on opposite lanes', () => {
  const route = v2Route();
  const tec = route.byCode.get('TEC');
  const bm = route.byCode.get('BM');
  assert.equal(tec.column, bm.column);
  assert.equal(tec.lane, -1);
  assert.equal(bm.lane, 1);
  assert.deepEqual(tec.parallelWith, ['BM']);
  assert.ok(route.byCode.get('BID').column < tec.column);
  assert.ok(route.byCode.get('COB').column > tec.column);
});

test('a conditional detour gets its own column and rejoins the main route', () => {
  const route = v2Route();
  const cob = route.byCode.get('COB');
  const pnc = route.byCode.get('PNC');
  const eas = route.byCode.get('EAS');
  assert.equal(pnc.column, cob.column + 1);
  assert.equal(eas.column, pnc.column + 1);
  assert.ok(pnc.lane > 1);
  const kinds = route.links.filter(link => [link.source, link.target].includes('PNC')).map(link => link.kind).sort();
  assert.deepEqual(kinds, ['conditional-entry', 'conditional-return']);
  assert.ok(route.links.some(link => link.source === 'COB' && link.target === 'EAS' && !link.conditional));
});

test('a terminal conditional stage continues the route without a return path', () => {
  const route = v2Route();
  const tot = route.byCode.get('TOT');
  assert.equal(tot.lane, 0);
  assert.equal(tot.column, route.byCode.get('PAYMENT').column + 1);
  assert.equal(route.links.filter(link => link.source === 'TOT').length, 0);
  assert.equal(route.columns, tot.column + 1);
});

test('selecting one parallel stage never marks its partner as complete', () => {
  const route = v2Route();
  assert.equal(Route.stationState(route, 'BM', 'TEC'), 'ahead');
  assert.equal(Route.stationState(route, 'BID', 'TEC'), 'done');
  assert.equal(Route.stationState(route, 'TEC', 'TEC'), 'active');
  const link = (source, target) => route.links.find(item => item.source === source && item.target === target);
  assert.equal(Route.linkState(route, link('BID', 'TEC'), 'TEC'), 'done');
  assert.equal(Route.linkState(route, link('BID', 'BM'), 'TEC'), 'idle');
  assert.equal(Route.linkState(route, link('TEC', 'COB'), 'COB'), 'done');
  assert.equal(Route.linkState(route, link('COB', 'PNC'), 'EAS'), 'idle');
  assert.equal(Route.linkState(route, link('COB', 'PNC'), 'PNC'), 'lit');
});

test('describe explains forks, joins and conditional stages in plain words', () => {
  const route = v2Route();
  assert.match(Route.describe(route, 'FS'), /^Every case starts here\./);
  assert.match(Route.describe(route, 'BID'), /route splits: TEC name and BM name run in parallel/);
  assert.match(Route.describe(route, 'TEC'), /same time as BM name\. Both must be complete before COB name/);
  assert.match(Route.describe(route, 'PNC'), /only when it is required.*straight from COB name to EAS name/);
  assert.match(Route.describe(route, 'PAYMENT'), /last mandatory stage.*TOT name may follow/);
  assert.match(Route.describe(route, 'TOT'), /route ends at PAYMENT name/);
});

test('a dependency cycle falls back to sequence order instead of failing', () => {
  const route = Route.buildRoute(Route.normalizeFlow({
    nodes: [{ code: 'A', sequence: 10 }, { code: 'B', sequence: 20 }],
    edges: [{ source: 'A', target: 'B' }, { source: 'B', target: 'A' }]
  }));
  assert.deepEqual(route.nodes.map(node => node.column), [0, 1]);
});

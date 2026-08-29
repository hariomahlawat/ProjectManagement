import fs from 'node:fs';

const [datasetPath, resultsPath] = process.argv.slice(2);
if (!datasetPath || !resultsPath) {
  console.error('Usage: node tools/search-v2-relevance-evaluator.mjs <dataset.json> <results.json>');
  process.exit(2);
}

const dataset = JSON.parse(fs.readFileSync(datasetPath, 'utf8'));
const run = JSON.parse(fs.readFileSync(resultsPath, 'utf8'));
if (dataset?.version !== 1 || !Array.isArray(dataset?.queries)) throw new Error('Unsupported or invalid benchmark dataset.');
if (!Array.isArray(run?.queries)) throw new Error('Results file must contain a queries array.');

const byId = new Map(run.queries.map(item => [item.id, item]));
const keyOf = item => `${item.entityType}:${item.entityKey}`;
const atK = (items, k) => items.slice(0, k);
const dcg = values => values.reduce((sum, relevance, index) => sum + ((2 ** relevance) - 1) / Math.log2(index + 2), 0);

let mrr = 0;
let ndcg = 0;
let recall = 0;
let rank1Total = 0;
let rank1Hits = 0;
let evaluated = 0;
const failures = [];

for (const query of dataset.queries) {
  const actual = byId.get(query.id);
  if (!actual || !Array.isArray(actual.results)) {
    failures.push(`${query.id}: no captured result set`);
    continue;
  }

  evaluated += 1;
  const relevance = new Map(query.expected.map(item => [keyOf(item), item.relevance]));
  const results = actual.results;
  const firstRelevant = results.findIndex(item => relevance.has(keyOf(item)));
  if (firstRelevant >= 0 && firstRelevant < 10) mrr += 1 / (firstRelevant + 1);

  const actualGrades = atK(results, 10).map(item => relevance.get(keyOf(item)) ?? 0);
  const idealGrades = query.expected.map(item => item.relevance).sort((a, b) => b - a).slice(0, 10);
  const ideal = dcg(idealGrades);
  ndcg += ideal > 0 ? dcg(actualGrades) / ideal : 0;

  const expectedKeys = new Set(query.expected.map(keyOf));
  const found = new Set(atK(results, 20).map(keyOf).filter(key => expectedKeys.has(key)));
  recall += expectedKeys.size > 0 ? found.size / expectedKeys.size : 0;

  if (query.rank1) {
    rank1Total += 1;
    if (results.length > 0 && keyOf(results[0]) === keyOf(query.rank1)) rank1Hits += 1;
  }
}

if (failures.length) {
  console.error('Benchmark capture is incomplete:');
  failures.forEach(item => console.error(` - ${item}`));
  process.exit(1);
}
if (evaluated === 0) throw new Error('No benchmark queries were evaluated.');

const metrics = {
  queries: evaluated,
  mrrAt10: mrr / evaluated,
  nDcgAt10: ndcg / evaluated,
  recallAt20: recall / evaluated,
  exactNavigationRank1: rank1Total === 0 ? null : rank1Hits / rank1Total,
  exactNavigationQueries: rank1Total
};

console.log(JSON.stringify(metrics, null, 2));

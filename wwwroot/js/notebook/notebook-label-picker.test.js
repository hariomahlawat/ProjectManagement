const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
function load(){ let s=fs.readFileSync(path.join(__dirname,'notebook-label-picker.js'),'utf8'); s=s.replace(/export function /g,'function '); s+='\nmodule.exports={normaliseLabelName,normaliseLabels};'; const c={module:{exports:{}},exports:{}}; vm.runInNewContext(s,c); return c.module.exports; }
test('label names are trimmed and hashes removed',()=>{const {normaliseLabelName}=load();assert.equal(normaliseLabelName(' ## Procurement '),'Procurement');});
test('labels are deduplicated case-insensitively',()=>{const {normaliseLabels}=load();assert.deepEqual(Array.from(normaliseLabels(['Docs',' docs ','#OPS','ops'])),['Docs','OPS']);});

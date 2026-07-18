const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

function loadModule() {
  const file = path.join(__dirname, 'notebook-reminder-scheduler.js');
  let source = fs.readFileSync(file, 'utf8');
  source = source
    .replace(/export function /g, 'function ')
    .concat('\nmodule.exports = { toIstIsoFromParts, istScheduleInstant, isFutureIstSchedule, getIstTodayValue, getReminderPreset, getDefaultReminderSchedule, formatReminderSummary };');
  const context = { module: { exports: {} }, exports: {}, Date, Intl, Math, Number, String, Boolean };
  vm.createContext(context);
  vm.runInContext(source, context);
  return context.module.exports;
}

test('IST schedule serialisation is explicit and host-time-zone independent', () => {
  const { toIstIsoFromParts, istScheduleInstant } = loadModule();
  assert.equal(toIstIsoFromParts('2026-07-18', '09:15'), '2026-07-18T09:15:00+05:30');
  assert.equal(istScheduleInstant('2026-07-18', '09:15').toISOString(), '2026-07-18T03:45:00.000Z');
  assert.equal(toIstIsoFromParts('invalid', '09:15'), null);
});

test('later today rounds to the next 30 minute IST slot', () => {
  const { getReminderPreset } = loadModule();
  const now = new Date('2026-07-18T02:31:10.000Z'); // 08:01:10 IST
  assert.deepEqual({ ...getReminderPreset('later-today', now) }, { date: '2026-07-18', time: '08:30' });
});


test('later today is always strictly after the current time on an exact slot boundary', () => {
  const { getReminderPreset } = loadModule();
  const now = new Date('2026-07-18T02:30:00.000Z'); // 08:00 IST
  assert.deepEqual({ ...getReminderPreset('later-today', now) }, { date: '2026-07-18', time: '08:30' });
});

test('default schedule moves to tomorrow morning when the working day is over', () => {
  const { getReminderPreset, getDefaultReminderSchedule } = loadModule();
  const now = new Date('2026-07-18T15:10:00.000Z'); // 20:40 IST
  assert.equal(getReminderPreset('later-today', now), null);
  assert.deepEqual({ ...getDefaultReminderSchedule(now) }, { date: '2026-07-19', time: '09:00' });
});

test('next Monday always means a future Monday', () => {
  const { getReminderPreset } = loadModule();
  const monday = new Date('2026-07-20T03:30:00.000Z'); // Monday 09:00 IST
  assert.deepEqual({ ...getReminderPreset('next-monday', monday) }, { date: '2026-07-27', time: '09:00' });
});

test('future validation and readable summary use IST', () => {
  const { isFutureIstSchedule, formatReminderSummary } = loadModule();
  const now = new Date('2026-07-18T02:30:00.000Z'); // 08:00 IST
  assert.equal(isFutureIstSchedule('2026-07-18', '08:30', now), true);
  assert.equal(isFutureIstSchedule('2026-07-18', '07:30', now), false);
  assert.equal(formatReminderSummary('2026-07-19', '09:00', now), 'Tomorrow at 09:00 IST');
});

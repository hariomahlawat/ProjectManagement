const IST_OFFSET_MINUTES = 330;
const MINUTE_MS = 60_000;
const DAY_MS = 24 * 60 * MINUTE_MS;

function pad2(value) {
  return String(value).padStart(2, '0');
}

function parseDateParts(dateValue) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(dateValue || '').trim());
  if (!match) return null;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const probe = new Date(Date.UTC(year, month - 1, day));
  if (probe.getUTCFullYear() !== year || probe.getUTCMonth() !== month - 1 || probe.getUTCDate() !== day) return null;
  return { year, month, day };
}

function parseTimeParts(timeValue) {
  const match = /^(\d{2}):(\d{2})$/.exec(String(timeValue || '').trim());
  if (!match) return null;
  const hour = Number(match[1]);
  const minute = Number(match[2]);
  if (hour > 23 || minute > 59) return null;
  return { hour, minute };
}

function istCalendarDate(date = new Date()) {
  return new Date(date.getTime() + IST_OFFSET_MINUTES * MINUTE_MS);
}

function toDateValue(date) {
  return `${date.getUTCFullYear()}-${pad2(date.getUTCMonth() + 1)}-${pad2(date.getUTCDate())}`;
}

function toTimeValue(date) {
  return `${pad2(date.getUTCHours())}:${pad2(date.getUTCMinutes())}`;
}

function addIstDays(parts, days) {
  const date = new Date(Date.UTC(parts.year, parts.month - 1, parts.day) + days * DAY_MS);
  return { year: date.getUTCFullYear(), month: date.getUTCMonth() + 1, day: date.getUTCDate() };
}

function partsToDateValue(parts) {
  return `${parts.year}-${pad2(parts.month)}-${pad2(parts.day)}`;
}

function nextRoundedIstDate(now = new Date(), intervalMinutes = 30) {
  const current = istCalendarDate(now);
  const minuteOfDay = current.getUTCHours() * 60 + current.getUTCMinutes();
  const roundedMinutes = (Math.floor(minuteOfDay / intervalMinutes) + 1) * intervalMinutes;
  const dayOffset = Math.floor(roundedMinutes / (24 * 60));
  const minuteWithinDay = roundedMinutes % (24 * 60);
  const dateOnly = addIstDays({
    year: current.getUTCFullYear(),
    month: current.getUTCMonth() + 1,
    day: current.getUTCDate()
  }, dayOffset);
  return {
    date: partsToDateValue(dateOnly),
    time: `${pad2(Math.floor(minuteWithinDay / 60))}:${pad2(minuteWithinDay % 60)}`
  };
}

export function toIstIsoFromParts(dateValue, timeValue) {
  const date = parseDateParts(dateValue);
  const time = parseTimeParts(timeValue);
  if (!date || !time) return null;
  return `${partsToDateValue(date)}T${pad2(time.hour)}:${pad2(time.minute)}:00+05:30`;
}

export function istScheduleInstant(dateValue, timeValue) {
  const date = parseDateParts(dateValue);
  const time = parseTimeParts(timeValue);
  if (!date || !time) return null;
  return new Date(Date.UTC(date.year, date.month - 1, date.day, time.hour, time.minute) - IST_OFFSET_MINUTES * MINUTE_MS);
}

export function isFutureIstSchedule(dateValue, timeValue, now = new Date(), minimumLeadMinutes = 1) {
  const instant = istScheduleInstant(dateValue, timeValue);
  return Boolean(instant && instant.getTime() > now.getTime() + minimumLeadMinutes * MINUTE_MS);
}

export function getIstTodayValue(now = new Date()) {
  return toDateValue(istCalendarDate(now));
}

export function getReminderPreset(preset, now = new Date()) {
  const currentIst = istCalendarDate(now);
  const today = {
    year: currentIst.getUTCFullYear(),
    month: currentIst.getUTCMonth() + 1,
    day: currentIst.getUTCDate()
  };

  if (preset === 'later-today') {
    const rounded = nextRoundedIstDate(now, 30);
    const sameDay = rounded.date === partsToDateValue(today);
    const hour = Number(rounded.time.slice(0, 2));
    return sameDay && hour < 20 ? rounded : null;
  }

  if (preset === 'tomorrow-morning') {
    return { date: partsToDateValue(addIstDays(today, 1)), time: '09:00' };
  }

  if (preset === 'next-monday') {
    const weekday = currentIst.getUTCDay();
    const daysUntilMonday = weekday === 1 ? 7 : (8 - weekday) % 7;
    return { date: partsToDateValue(addIstDays(today, daysUntilMonday || 7)), time: '09:00' };
  }

  return null;
}

export function getDefaultReminderSchedule(now = new Date()) {
  return getReminderPreset('later-today', now) || getReminderPreset('tomorrow-morning', now);
}

export function formatReminderSummary(dateValue, timeValue, now = new Date()) {
  const instant = istScheduleInstant(dateValue, timeValue);
  if (!instant) return '';

  const targetIst = istCalendarDate(instant);
  const nowIst = istCalendarDate(now);
  const targetDay = Date.UTC(targetIst.getUTCFullYear(), targetIst.getUTCMonth(), targetIst.getUTCDate());
  const today = Date.UTC(nowIst.getUTCFullYear(), nowIst.getUTCMonth(), nowIst.getUTCDate());
  const dayDifference = Math.round((targetDay - today) / DAY_MS);
  const prefix = dayDifference === 0 ? 'Today' : dayDifference === 1 ? 'Tomorrow' : new Intl.DateTimeFormat('en-IN', {
    weekday: 'short',
    day: 'numeric',
    month: 'long',
    year: targetIst.getUTCFullYear() !== nowIst.getUTCFullYear() ? 'numeric' : undefined,
    timeZone: 'Asia/Kolkata'
  }).format(instant);
  const time = new Intl.DateTimeFormat('en-IN', {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
    timeZone: 'Asia/Kolkata'
  }).format(instant);
  return `${prefix} at ${time} IST`;
}

export function createReminderScheduler(root, options = {}) {
  if (!root) throw new Error('Reminder scheduler root is required.');
  const nowProvider = options.nowProvider || (() => new Date());
  const dateInput = root.querySelector('[data-reminder-date]');
  const timeInput = root.querySelector('[data-reminder-time]');
  const summary = root.querySelector('[data-reminder-summary]');
  const error = root.querySelector('[data-reminder-error]');
  const presetButtons = [...root.querySelectorAll('[data-reminder-preset]')];
  if (!dateInput || !timeInput || !summary || !error) throw new Error('Reminder scheduler markup is incomplete.');

  let touched = false;

  function setError(message = '') {
    error.textContent = message;
    error.hidden = !message;
    dateInput.setAttribute('aria-invalid', message ? 'true' : 'false');
    timeInput.setAttribute('aria-invalid', message ? 'true' : 'false');
  }

  function updatePresentation({ validate = false } = {}) {
    const now = nowProvider();
    dateInput.min = getIstTodayValue(now);
    const laterToday = getReminderPreset('later-today', now);
    const laterButton = presetButtons.find((button) => button.dataset.reminderPreset === 'later-today');
    if (laterButton) {
      laterButton.disabled = !laterToday;
      laterButton.title = laterToday ? '' : 'No suitable time remains today.';
    }
    summary.textContent = formatReminderSummary(dateInput.value, timeInput.value, now);
    summary.hidden = !summary.textContent;
    if (validate && (dateInput.value || timeInput.value)) validateSchedule();
    else setError('');
  }

  function setValue(value = {}, { markTouched = false, validate = false } = {}) {
    dateInput.value = value.date || '';
    timeInput.value = value.time || '';
    touched = Boolean(markTouched);
    updatePresentation({ validate });
  }

  function setDefault() {
    setValue(getDefaultReminderSchedule(nowProvider()), { markTouched: false });
  }

  function clear() {
    setValue({}, { markTouched: false });
  }

  function getValue() {
    return {
      date: dateInput.value,
      time: timeInput.value,
      iso: toIstIsoFromParts(dateInput.value, timeInput.value),
      touched
    };
  }

  function validateSchedule({ focus = false } = {}) {
    let message = '';
    if (!dateInput.value) message = 'Select a reminder date.';
    else if (!timeInput.value) message = 'Select a reminder time.';
    else if (!isFutureIstSchedule(dateInput.value, timeInput.value, nowProvider())) message = 'Choose a future date and time.';
    setError(message);
    if (message && focus) (!dateInput.value ? dateInput : timeInput).focus();
    return { valid: !message, message };
  }

  presetButtons.forEach((button) => button.addEventListener('click', () => {
    const value = getReminderPreset(button.dataset.reminderPreset, nowProvider());
    if (!value) return;
    setValue(value, { markTouched: true, validate: true });
    options.onChange?.(getValue());
  }));

  [dateInput, timeInput].forEach((input) => {
    input.addEventListener('input', () => {
      touched = true;
      updatePresentation();
      options.onChange?.(getValue());
    });
    input.addEventListener('change', () => {
      touched = true;
      updatePresentation({ validate: true });
      options.onChange?.(getValue());
    });
    input.addEventListener('blur', () => {
      if (dateInput.value || timeInput.value) validateSchedule();
    });
  });

  updatePresentation();
  return {
    clear,
    focus: () => dateInput.focus(),
    getValue,
    setDefault,
    setValue,
    validate: validateSchedule,
    updatePresentation
  };
}

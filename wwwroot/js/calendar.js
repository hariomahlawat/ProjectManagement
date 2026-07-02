(function () {
  // FullCalendar globals from index.global.min.js (bundle or individual files)
  const FC = window.FullCalendar;
  const Calendar = FC && FC.Calendar;
  if (!Calendar) { console.error('FullCalendar global bundle missing'); return; }

  // Build a plugin list that only includes plugins that actually exist
  const pluginList = [];
  // Prefer FullCalendar 6 global bundle plugin names; fall back to legacy globals
  const dg = (FC && (FC.dayGridPlugin || FC.dayGrid)) || window.FullCalendarDayGrid;
  const tg = (FC && (FC.timeGridPlugin || FC.timeGrid)) || window.FullCalendarTimeGrid;
  const ls = (FC && (FC.listPlugin || FC.list)) || window.FullCalendarList;
  const ia = (FC && (FC.interactionPlugin || FC.interaction)) || window.FullCalendarInteraction;
  if (dg) pluginList.push(dg);
  if (tg) pluginList.push(tg);
  if (ls) pluginList.push(ls);
  if (ia) pluginList.push(ia);

  const calendarEl = document.getElementById('calendar');
  if (!calendarEl) return;

  const canEdit = (calendarEl.dataset.canEdit || '').toLowerCase() === 'true';
  let showCelebrations = (calendarEl.dataset.showCelebrations || 'true').toLowerCase() === 'true';

  const preferencesForm = document.getElementById('calendarPreferences');
  const showCelebrationsToggle = document.getElementById('showCelebrationsToggle');
  const antiforgeryInput = preferencesForm?.querySelector('input[name="__RequestVerificationToken"]');
  const preferenceEndpoint = preferencesForm?.dataset.preferenceEndpoint || '/calendar/events/preferences/show-celebrations';

  const canonMap = {
    visit: 'Visit',
    insp: 'Insp',
    inspection: 'Insp',
    conference: 'Conference',
    other: 'Other',
    celebration: 'Celebration',
    birthday: 'Birthday',
    anniversary: 'Anniversary'
  };
  const canon = (raw) => {
    const str = (raw || '').toString();
    return canonMap[str.toLowerCase()] || (str || 'Other');
  };

  const getCelebrationPresentation = (event) => {
    const title = (event?.title || '').trim();
    const explicitType = event?.extendedProps?.celebrationType;
    const titlePrefix = title.match(/^([^:]+):/i)?.[1];
    const type = canon(explicitType || titlePrefix || 'Celebration');
    const name = title
      .replace(/^(Birthday|Anniversary|Celebration)\s*:\s*/i, '')
      .trim() || title || 'Celebration';

    return {
      type,
      name,
      iconClass: type === 'Anniversary' ? 'bi-hearts' : 'bi-gift'
    };
  };

  let activeCategory = "";

  const holidayMap = new Map();
  let holidayRangeKey = '';
  let holidayFetchController = null;
  let holidayErrorShown = false;
  let calendar = null;

  const getIsoDate = (value) => {
    if (!value) return '';
    if (typeof value === 'string') {
      return value.slice(0, 10);
    }
    const d = value instanceof Date ? value : new Date(value);
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
  };

  // helpers
  const pad = (n) => String(n).padStart(2,'0');
  const toLocalDateInputValue = (d) => {
    const dt = new Date(d);
    return `${dt.getFullYear()}-${pad(dt.getMonth()+1)}-${pad(dt.getDate())}`;
  };

  const toLocalTimeInputValue = (d) => {
    const dt = new Date(d);
    return `${pad(dt.getHours())}:${pad(dt.getMinutes())}`;
  };

  const combineLocalDateTime = (datePart, timePart) => {
    if (!datePart || !timePart) return null;
    return new Date(`${datePart}T${timePart}`);
  };

  const roundUpToInterval = (date, minutes = 15) => {
    const dt = new Date(date);
    dt.setSeconds(0, 0);
    const ms = minutes * 60 * 1000;
    const rounded = new Date(Math.ceil(dt.getTime() / ms) * ms);
    if (rounded.getTime() === dt.getTime()) {
      return rounded;
    }
    rounded.setSeconds(0, 0);
    return rounded;
  };

  const isValidDate = (value) => value instanceof Date && !Number.isNaN(value.getTime());

  const isSameLocalDate = (left, right) =>
    left.getFullYear() === right.getFullYear()
    && left.getMonth() === right.getMonth()
    && left.getDate() === right.getDate();

  const atLocalTime = (date, hours, minutes = 0) => {
    const value = new Date(date);
    value.setHours(hours, minutes, 0, 0);
    return value;
  };

  const getDefaultTimedStart = (selectedDate = null) => {
    const now = new Date();
    const selected = selectedDate ? new Date(selectedDate) : null;

    if (selected && !isSameLocalDate(selected, now)) {
      return atLocalTime(selected, 9, 0);
    }

    let candidate = roundUpToInterval(now, 15);
    if (candidate.getHours() < 8) {
      candidate = atLocalTime(candidate, 8, 0);
    } else if (candidate.getHours() >= 18) {
      candidate.setDate(candidate.getDate() + 1);
      candidate = atLocalTime(candidate, 9, 0);
    }

    if (selected) {
      candidate.setFullYear(selected.getFullYear(), selected.getMonth(), selected.getDate());
    }

    return candidate;
  };

  const monthFormatter = new Intl.DateTimeFormat('en-US', { month: 'short' });
  const formatDisplayDate = (date) => {
    const d = date instanceof Date ? date : new Date(date);
    return `${pad(d.getDate())} ${monthFormatter.format(d)} ${d.getFullYear()}`;
  };
  const formatDisplayDateTime = (date) => {
    const d = date instanceof Date ? date : new Date(date);
    return `${formatDisplayDate(d)} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
  };

  const buildHolidayTooltip = (meta) => {
    const name = meta?.name || meta?.Name || '';
    return name ? `Holiday: ${name}` : 'Holiday';
  };

  const buildHolidayAria = (iso, meta) => {
    const labelDate = formatDisplayDate(`${iso}T00:00:00`);
    const tooltip = buildHolidayTooltip(meta);
    return `${labelDate} — ${tooltip}`;
  };

  const decorateHolidayCell = (el, iso) => {
    if (!el) return;
    const meta = iso ? holidayMap.get(iso) : null;
    if (meta) {
      el.classList.add('pm-holiday');
      el.setAttribute('data-pm-holiday-active', '1');
    } else if (el.getAttribute('data-pm-holiday-active')) {
      el.classList.remove('pm-holiday');
      el.removeAttribute('data-pm-holiday-active');
    } else {
      el.classList.remove('pm-holiday');
    }
  };

  const decorateHolidayLabelElement = (el, iso) => {
    if (!el) return;
    const meta = iso ? holidayMap.get(iso) : null;
    if (meta) {
      if (!el.hasAttribute('data-pm-holiday-orig-title') && el.hasAttribute('title')) {
        el.setAttribute('data-pm-holiday-orig-title', el.getAttribute('title'));
      }
      if (!el.hasAttribute('data-pm-holiday-orig-aria') && el.hasAttribute('aria-label')) {
        el.setAttribute('data-pm-holiday-orig-aria', el.getAttribute('aria-label'));
      }
      const tooltip = buildHolidayTooltip(meta);
      const baseAria = el.getAttribute('data-pm-holiday-orig-aria');
      const aria = baseAria ? `${baseAria}. ${tooltip}` : buildHolidayAria(iso, meta);
      el.setAttribute('title', tooltip);
      el.setAttribute('aria-label', aria);
      el.setAttribute('data-pm-holiday-label', '1');
    } else if (el.getAttribute('data-pm-holiday-label')) {
      const origTitle = el.getAttribute('data-pm-holiday-orig-title');
      if (origTitle !== null) el.setAttribute('title', origTitle);
      else el.removeAttribute('title');
      const origAria = el.getAttribute('data-pm-holiday-orig-aria');
      if (origAria !== null) el.setAttribute('aria-label', origAria);
      else el.removeAttribute('aria-label');
      el.removeAttribute('data-pm-holiday-label');
    }
  };

  const syncHolidayBadge = (targetEl, iso, options = {}) => {
    if (!targetEl) return;
    const meta = iso ? holidayMap.get(iso) : null;
    const predicate = typeof options.shouldDisplay === 'function' ? options.shouldDisplay : null;
    const shouldShow = !!(meta && (!predicate || predicate(meta, iso)));
    const badge = targetEl.querySelector('.pm-holiday-badge');
    if (shouldShow) {
      const label = buildHolidayTooltip(meta);
      let node = badge;
      if (!node) {
        node = document.createElement('span');
        node.className = 'pm-holiday-badge';
        targetEl.appendChild(node);
      }
      node.textContent = label;
      decorateHolidayLabelElement(node, iso);
    } else if (badge) {
      decorateHolidayLabelElement(badge, null);
      badge.remove();
    }
  };

  const decorateDayCellElement = (el, iso) => {
    if (!el || !iso) return;
    decorateHolidayCell(el, iso);
    decorateHolidayLabelElement(el, iso);
    const numberEl = el.querySelector?.('.fc-daygrid-day-number');
    if (numberEl) decorateHolidayLabelElement(numberEl, iso);
    const frameEl = el.querySelector?.('.fc-daygrid-day-frame');
    if (frameEl) decorateHolidayCell(frameEl, iso);
    const topEl = frameEl?.querySelector?.('.fc-daygrid-day-top') || el.querySelector?.('.fc-daygrid-day-top');
    const badgeTarget = topEl || frameEl || el;
    if (badgeTarget) syncHolidayBadge(badgeTarget, iso);
  };

  const decorateHeaderCellElement = (el, iso) => {
    if (!el || !iso) return;
    decorateHolidayCell(el, iso);
    const cushion = el.querySelector?.('.fc-col-header-cell-cushion');
    if (cushion) decorateHolidayLabelElement(cushion, iso);
    else decorateHolidayLabelElement(el, iso);
  };

  const renderHolidayListBadges = () => {
    const isListView = (calendar?.view?.type || '').startsWith('list');
    calendarEl.querySelectorAll('.fc-list-day').forEach(row => {
      const iso = getIsoDate(row.getAttribute('data-date'));
      const meta = iso ? holidayMap.get(iso) : null;
      decorateHolidayCell(row, iso);
      const cushion = row.querySelector('.fc-list-day-cushion');
      if (cushion) {
        decorateHolidayCell(cushion, iso);
        decorateHolidayLabelElement(cushion, iso);
        const textEl = cushion.querySelector('.fc-list-day-text');
        if (textEl) decorateHolidayLabelElement(textEl, iso);
        syncHolidayBadge(cushion, iso, {
          shouldDisplay: () => isListView && !!meta
        });
      }
    });
  };

  const refreshHolidayHighlights = () => {
    calendarEl.querySelectorAll('.fc-daygrid-day').forEach(cell => {
      const iso = getIsoDate(cell.getAttribute('data-date'));
      decorateDayCellElement(cell, iso);
    });
    calendarEl.querySelectorAll('.fc-timegrid-col[data-date]').forEach(col => {
      const iso = getIsoDate(col.getAttribute('data-date'));
      decorateHolidayCell(col, iso);
      const frame = col.querySelector('.fc-timegrid-col-frame');
      if (frame) decorateHolidayCell(frame, iso);
      const badgeTarget = frame?.querySelector?.('.fc-timegrid-col-top') || frame || col;
      syncHolidayBadge(badgeTarget, iso);
    });
    calendarEl.querySelectorAll('.fc-col-header-cell[data-date]').forEach(cell => {
      const iso = getIsoDate(cell.getAttribute('data-date'));
      decorateHeaderCellElement(cell, iso);
    });
    renderHolidayListBadges();
  };

  // Cache event-editor elements. These are absent for read-only users.
  const form = document.getElementById('eventForm');
  const titleBox = form ? form.querySelector('[name="title"]') : null;
  const idBox = form ? form.querySelector('[name="id"]') : null;
  const catBox = form ? form.querySelector('[name="category"]') : null;
  const locBox = form ? form.querySelector('[name="location"]') : null;
  const descBox = form ? form.querySelector('[name="description"]') : null;
  const isAllDayBox = document.getElementById('toggleAllDay');
  const timedDateTimeFields = document.getElementById('timedDateTimeFields');
  const allDayDateFields = document.getElementById('allDayDateFields');
  const startDateTimedBox = document.getElementById('eventStartDateTimed');
  const startTimeTimedBox = document.getElementById('eventStartTimeTimed');
  const endDateTimedBox = document.getElementById('eventEndDateTimed');
  const endTimeTimedBox = document.getElementById('eventEndTimeTimed');
  const startDateAllDayBox = document.getElementById('eventStartDateAllDay');
  const endDateAllDayBox = document.getElementById('eventEndDateAllDay');
  const advancedOptions = document.getElementById('eventAdvancedOptions');
  const btnDelete = document.getElementById('btnDeleteEvent');
  const btnSave = document.getElementById('btnSaveEvent');
  const saveSpinner = document.getElementById('eventSaveSpinner');
  const saveIcon = document.getElementById('eventSaveIcon');
  const saveText = document.getElementById('eventSaveText');
  const formHint = document.getElementById('eventFormHint');
  const editorFormBody = form?.querySelector('.calendar-event-form-body');
  const formAlert = document.getElementById('eventFormAlert');
  const dateRangeError = document.getElementById('eventDateRangeError');
  const durationHint = document.getElementById('eventDurationHint');

  // ----- repeat UI wiring -----
  const repeatFreq = document.getElementById('repeatFreq');
  const repeatOptions = document.getElementById('repeatOptions');
  const repeatWeekly = document.getElementById('repeatWeekly');
  const repeatMonthly = document.getElementById('repeatMonthly');
  const repeatMonthDay = document.getElementById('repeatMonthDay');
  const repeatUntil = document.getElementById('repeatUntil');
  const repeatUntilFeedback = repeatUntil?.parentElement?.querySelector('.invalid-feedback');
  const repeatEndNever = document.getElementById('repeatEndNever');
  const repeatEndOn = document.getElementById('repeatEndOn');

  let linkedDurationMinutes = 60;
  let endWasManuallyEdited = false;
  let suppressEndDirtyTracking = false;

  function getTimedStart() {
    return combineLocalDateTime(startDateTimedBox?.value, startTimeTimedBox?.value);
  }

  function getTimedEnd() {
    return combineLocalDateTime(endDateTimedBox?.value, endTimeTimedBox?.value);
  }

  function setTimedValues(start, end, preserveDuration = true) {
    if (!isValidDate(start) || !isValidDate(end)) return;
    suppressEndDirtyTracking = true;
    if (startDateTimedBox) startDateTimedBox.value = toLocalDateInputValue(start);
    if (startTimeTimedBox) startTimeTimedBox.value = toLocalTimeInputValue(start);
    if (endDateTimedBox) endDateTimedBox.value = toLocalDateInputValue(end);
    if (endTimeTimedBox) endTimeTimedBox.value = toLocalTimeInputValue(end);
    suppressEndDirtyTracking = false;

    if (preserveDuration) {
      const minutes = Math.round((end - start) / 60000);
      linkedDurationMinutes = minutes > 0 ? minutes : 60;
      endWasManuallyEdited = false;
    }
  }

  function setAllDayValues(start, endInclusive) {
    if (!isValidDate(start) || !isValidDate(endInclusive)) return;
    if (startDateAllDayBox) startDateAllDayBox.value = toLocalDateInputValue(start);
    if (endDateAllDayBox) endDateAllDayBox.value = toLocalDateInputValue(endInclusive);
  }

  function syncEndToStartIfLinked() {
    if (endWasManuallyEdited) return;
    const start = getTimedStart();
    if (!isValidDate(start)) return;
    const duration = Math.max(1, linkedDurationMinutes || 60);
    const end = new Date(start.getTime() + duration * 60000);
    suppressEndDirtyTracking = true;
    if (endDateTimedBox) endDateTimedBox.value = toLocalDateInputValue(end);
    if (endTimeTimedBox) endTimeTimedBox.value = toLocalTimeInputValue(end);
    suppressEndDirtyTracking = false;
  }

  function syncModeValues(nextAllDay) {
    if (nextAllDay) {
      const start = getTimedStart();
      const end = getTimedEnd();
      if (isValidDate(start)) {
        const safeEnd = isValidDate(end) && end >= start ? end : start;
        setAllDayValues(start, safeEnd);
      }
      return;
    }

    const startDate = startDateAllDayBox?.value;
    const endDate = endDateAllDayBox?.value || startDate;
    if (!startDate) return;

    const existingStartTime = startTimeTimedBox?.value || '09:00';
    const existingEndTime = endTimeTimedBox?.value || '10:00';
    const start = combineLocalDateTime(startDate, existingStartTime);
    let end = combineLocalDateTime(endDate, existingEndTime);
    if (!isValidDate(start)) return;
    if (!isValidDate(end) || end <= start) {
      end = new Date(start.getTime() + Math.max(1, linkedDurationMinutes || 60) * 60000);
    }
    setTimedValues(start, end, true);
  }

  function activeSeriesStartDateValue() {
    return isAllDayBox?.checked
      ? startDateAllDayBox?.value || ''
      : startDateTimedBox?.value || '';
  }

  function validateRepeatEnd() {
    if (!repeatUntil) return true;
    const defaultMessage = 'Select the repeat end date.';
    repeatUntil.setCustomValidity('');
    if (repeatUntilFeedback) repeatUntilFeedback.textContent = defaultMessage;

    if (!repeatFreq?.value || !repeatEndOn?.checked) return true;
    if (!repeatUntil.value) {
      const message = 'Select when the recurring event ends.';
      repeatUntil.setCustomValidity(message);
      if (repeatUntilFeedback) repeatUntilFeedback.textContent = message;
      return false;
    }

    const startDate = activeSeriesStartDateValue();
    if (startDate && repeatUntil.value < startDate) {
      const message = 'The repeat end date cannot be before the event start date.';
      repeatUntil.setCustomValidity(message);
      if (repeatUntilFeedback) repeatUntilFeedback.textContent = message;
      return false;
    }

    return true;
  }

  function revealInvalidField(input) {
    if (!input) return;
    if (advancedOptions?.contains(input)) {
      advancedOptions.open = true;
    }
    window.requestAnimationFrame(() => {
      input.scrollIntoView({ block: 'center', behavior: 'smooth' });
      input.focus({ preventScroll: true });
    });
  }

  async function readErrorMessage(response) {
    const raw = await response.text();
    if (!raw) return `Error ${response.status}`;

    try {
      const problem = JSON.parse(raw);
      if (typeof problem === 'string') return problem;
      if (problem.detail) return problem.detail;
      if (problem.message) return problem.message;
      if (problem.errors && typeof problem.errors === 'object') {
        const messages = Object.values(problem.errors)
          .flatMap(value => Array.isArray(value) ? value : [value])
          .filter(Boolean);
        if (messages.length) return messages.join(' ');
      }
      if (problem.title) return problem.title;
    } catch { }

    return raw;
  }

  function clearFormAlert() {
    if (!formAlert) return;
    formAlert.textContent = '';
    formAlert.classList.add('d-none');
  }

  function showFormAlert(message) {
    if (!formAlert) {
      alert(message);
      return;
    }
    formAlert.textContent = message;
    formAlert.classList.remove('d-none');
    formAlert.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
  }

  function setSaving(isSaving) {
    if (!btnSave) return;
    btnSave.disabled = isSaving;
    btnSave.setAttribute('aria-busy', isSaving ? 'true' : 'false');
    saveSpinner?.classList.toggle('d-none', !isSaving);
    saveIcon?.classList.toggle('d-none', isSaving);
  }

  function resetEditorScroll() {
    if (editorFormBody) editorFormBody.scrollTop = 0;
  }

  function setEditorMode(isEditing) {
    const label = document.getElementById('eventFormLabel');
    if (label) label.textContent = isEditing ? 'Edit event' : 'New event';
    if (formHint) {
      formHint.textContent = isEditing
        ? 'Review the details and save only the changes that are required.'
        : 'Add the essential details now; optional fields can be completed later.';
    }
    if (saveText) saveText.textContent = isEditing ? 'Save changes' : 'Create event';
  }

  function clearDateRangeError() {
    endTimeTimedBox?.setCustomValidity('');
    endDateTimedBox?.setCustomValidity('');
    endDateAllDayBox?.setCustomValidity('');
    if (dateRangeError) {
      dateRangeError.textContent = '';
      dateRangeError.classList.add('d-none');
    }
  }

  function showDateRangeError(message, input) {
    clearDateRangeError();
    input?.setCustomValidity(message);
    if (dateRangeError) {
      dateRangeError.textContent = message;
      dateRangeError.classList.remove('d-none');
    }
  }

  function validateDateRange() {
    clearDateRangeError();
    if (!form || !isAllDayBox) return true;

    if (isAllDayBox.checked) {
      if (!startDateAllDayBox?.value || !endDateAllDayBox?.value) return true;
      const start = new Date(`${startDateAllDayBox.value}T00:00:00`);
      const end = new Date(`${endDateAllDayBox.value}T00:00:00`);
      if (!isValidDate(start) || !isValidDate(end)) return true;
      if (end < start) {
        showDateRangeError('End date cannot be before the start date.', endDateAllDayBox);
        return false;
      }
      return true;
    }

    if (!startDateTimedBox?.value || !startTimeTimedBox?.value || !endDateTimedBox?.value || !endTimeTimedBox?.value) return true;
    const start = combineLocalDateTime(startDateTimedBox.value, startTimeTimedBox.value);
    const end = combineLocalDateTime(endDateTimedBox.value, endTimeTimedBox.value);
    if (!isValidDate(start) || !isValidDate(end)) return true;
    if (end <= start) {
      showDateRangeError('End time must be after the start time.', endTimeTimedBox);
      return false;
    }
    return true;
  }

  function updateDurationHint() {
    if (!durationHint || !isAllDayBox) return;
    durationHint.textContent = '';

    if (isAllDayBox.checked) {
      if (!startDateAllDayBox?.value || !endDateAllDayBox?.value) return;
      const start = new Date(`${startDateAllDayBox.value}T00:00:00`);
      const end = new Date(`${endDateAllDayBox.value}T00:00:00`);
      if (!isValidDate(start) || !isValidDate(end) || end < start) return;
      const days = Math.round((end - start) / 86400000) + 1;
      durationHint.textContent = days === 1 ? '1 all-day event' : `${days} calendar days`;
      return;
    }

    if (!startDateTimedBox?.value || !startTimeTimedBox?.value || !endDateTimedBox?.value || !endTimeTimedBox?.value) return;
    const start = combineLocalDateTime(startDateTimedBox.value, startTimeTimedBox.value);
    const end = combineLocalDateTime(endDateTimedBox.value, endTimeTimedBox.value);
    if (!isValidDate(start) || !isValidDate(end)) return;
    const minutes = Math.round((end - start) / 60000);
    if (minutes <= 0) return;

    if (minutes < 60) {
      durationHint.textContent = `${minutes} minutes`;
    } else if (minutes % 60 === 0) {
      const hours = minutes / 60;
      durationHint.textContent = hours === 1 ? '1 hour' : `${hours} hours`;
    } else {
      const hours = Math.floor(minutes / 60);
      const remainder = minutes % 60;
      durationHint.textContent = `${hours} hr ${remainder} min`;
    }
  }

  function syncRepeatEndUI() {
    if (!repeatUntil) return;
    const enabled = !!repeatFreq?.value && !!repeatEndOn?.checked;
    repeatUntil.disabled = !enabled;
    repeatUntil.required = enabled;
    if (!enabled) repeatUntil.setCustomValidity('');
  }

  function syncRepeatUI() {
    if (!repeatFreq) return;
    const frequency = repeatFreq.value;
    const isWeekly = frequency === 'WEEKLY';
    const isMonthly = frequency === 'MONTHLY';

    repeatOptions?.classList.toggle('d-none', !frequency);
    repeatWeekly?.classList.toggle('d-none', !isWeekly);
    repeatMonthly?.classList.toggle('d-none', !isMonthly);
    repeatWeekly?.querySelectorAll('input').forEach(input => { input.disabled = !isWeekly; });
    if (repeatMonthDay) repeatMonthDay.disabled = !isMonthly;
    syncRepeatEndUI();
  }

  repeatFreq?.addEventListener('change', () => {
    syncRepeatUI();
    validateRepeatEnd();
    clearFormAlert();
  });
  repeatEndNever?.addEventListener('change', () => {
    syncRepeatEndUI();
    validateRepeatEnd();
  });
  repeatEndOn?.addEventListener('change', () => {
    syncRepeatEndUI();
    validateRepeatEnd();
    if (repeatEndOn.checked) repeatUntil?.focus();
  });
  titleBox?.addEventListener('input', () => {
    titleBox.setCustomValidity(titleBox.value.trim() ? '' : 'Enter a title for the event.');
    clearFormAlert();
  });
  repeatUntil?.addEventListener('input', () => {
    validateRepeatEnd();
    clearFormAlert();
  });

  function buildRRule(startLocalIso) {
    const f = repeatFreq?.value || '';
    if (!f) return null;
    const parts = [`FREQ=${f}`, 'INTERVAL=1'];

    if (f === 'WEEKLY') {
      const days = Array.from(repeatWeekly?.querySelectorAll('input:checked') || []).map(x => x.value);
      if (!days.length) {
        const map = ['SU','MO','TU','WE','TH','FR','SA'];
        days.push(map[new Date(startLocalIso).getDay()]);
      }
      parts.push(`BYDAY=${days.join(',')}`);
    }
    if (f === 'MONTHLY') {
      const d = parseInt(repeatMonthDay?.value, 10) || new Date(startLocalIso).getDate();
      parts.push(`BYMONTHDAY=${d}`);
    }
    // The end date is stored separately in RecurrenceUntilUtc. Keeping it out of
    // the RRULE avoids conflicting UTC/local interpretations of UNTIL.
    return parts.join(';');
  }

  function hydrateRepeatUI(rrule, recurrenceUntilUtc = null) {
    if (!repeatFreq) return;
    repeatFreq.value = '';
    repeatWeekly?.querySelectorAll('input').forEach(i => { i.checked = false; });
    if (repeatMonthDay) repeatMonthDay.value = '';
    if (repeatUntil) repeatUntil.value = '';
    if (repeatEndNever) repeatEndNever.checked = true;
    if (repeatEndOn) repeatEndOn.checked = false;

    let ruleUntil = null;
    if (rrule) {
      const m = Object.fromEntries(rrule.split(';').map(part => {
        const separator = part.indexOf('=');
        return separator > 0
          ? [part.slice(0, separator).toUpperCase(), part.slice(separator + 1)]
          : [part.toUpperCase(), ''];
      }));
      const frequency = (m.FREQ || '').toUpperCase();
      if (frequency === 'WEEKLY') {
        repeatFreq.value = 'WEEKLY';
        (m.BYDAY || '').toUpperCase().split(',').forEach(code => {
          const box = repeatWeekly?.querySelector(`input[value="${code}"]`);
          if (box) box.checked = true;
        });
      } else if (frequency === 'MONTHLY') {
        repeatFreq.value = 'MONTHLY';
        if (m.BYMONTHDAY && repeatMonthDay) repeatMonthDay.value = m.BYMONTHDAY;
      }
      if (m.UNTIL && /^\d{8}/.test(m.UNTIL)) {
        ruleUntil = `${m.UNTIL.slice(0, 4)}-${m.UNTIL.slice(4, 6)}-${m.UNTIL.slice(6, 8)}`;
      }
    }

    if (repeatUntil) {
      if (ruleUntil) {
        repeatUntil.value = ruleUntil;
      } else if (recurrenceUntilUtc) {
        const until = new Date(recurrenceUntilUtc);
        if (isValidDate(until)) repeatUntil.value = toLocalDateInputValue(until);
      }
    }

    if (repeatUntil?.value) {
      if (repeatEndOn) repeatEndOn.checked = true;
      if (repeatEndNever) repeatEndNever.checked = false;
    }

    syncRepeatUI();
    validateRepeatEnd();
  }

  function setAllDayUI(on) {
    if (!timedDateTimeFields || !allDayDateFields) return;
    timedDateTimeFields.classList.toggle('d-none', on);
    allDayDateFields.classList.toggle('d-none', !on);

    [startDateTimedBox, startTimeTimedBox, endDateTimedBox, endTimeTimedBox].forEach(input => {
      if (input) input.disabled = on;
    });
    [startDateAllDayBox, endDateAllDayBox].forEach(input => {
      if (input) input.disabled = !on;
    });

    clearDateRangeError();
    updateDurationHint();
  }

  function resetEditorValidation() {
    if (!form) return;
    form.classList.remove('was-validated');
    titleBox?.setCustomValidity('');
    repeatUntil?.setCustomValidity('');
    clearDateRangeError();
    clearFormAlert();
    setSaving(false);
    linkedDurationMinutes = 60;
    endWasManuallyEdited = false;
    suppressEndDirtyTracking = false;
  }

  function setAdvancedOptionsOpen(isOpen) {
    if (!advancedOptions) return;
    advancedOptions.open = !!isOpen;
  }

  isAllDayBox?.addEventListener('change', () => {
    syncModeValues(isAllDayBox.checked);
    setAllDayUI(isAllDayBox.checked);
    validateDateRange();
    validateRepeatEnd();
    updateDurationHint();
  });

  [startDateTimedBox, startTimeTimedBox].forEach(input => {
    input?.addEventListener('input', () => {
      syncEndToStartIfLinked();
      validateDateRange();
      validateRepeatEnd();
      updateDurationHint();
    });
    input?.addEventListener('change', () => {
      syncEndToStartIfLinked();
      validateDateRange();
      validateRepeatEnd();
      updateDurationHint();
    });
  });

  [endDateTimedBox, endTimeTimedBox].forEach(input => {
    input?.addEventListener('input', () => {
      if (!suppressEndDirtyTracking) endWasManuallyEdited = true;
      validateDateRange();
      updateDurationHint();
    });
    input?.addEventListener('change', () => {
      if (!suppressEndDirtyTracking) endWasManuallyEdited = true;
      validateDateRange();
      updateDurationHint();
    });
  });

  [startDateAllDayBox, endDateAllDayBox].forEach(input => {
    input?.addEventListener('input', () => {
      validateDateRange();
      validateRepeatEnd();
      updateDurationHint();
    });
    input?.addEventListener('change', () => {
      validateDateRange();
      validateRepeatEnd();
      updateDurationHint();
    });
  });

  if (form) {
    setAllDayUI(!!isAllDayBox?.checked);
    syncRepeatUI();

    const eventFormCanvasEl = document.getElementById('eventFormCanvas');
    eventFormCanvasEl?.addEventListener('hidden.bs.offcanvas', () => {
      form.reset();
      setAllDayUI(false);
      hydrateRepeatUI(null);
      setAdvancedOptionsOpen(false);
      resetEditorValidation();
      resetEditorScroll();
      btnDelete?.classList.add('d-none');
    });
  }

  // undo toast
  const undoToastEl = document.getElementById('undoToast');
  const undoMessageEl = document.getElementById('undoMessage');
  const undoBtn = document.getElementById('btnUndo');
  const undoToast = undoToastEl ? new bootstrap.Toast(undoToastEl, { autohide: true, delay: 5000 }) : null;
  let undoHandler = null;
  undoBtn && undoBtn.addEventListener('click', async () => {
    if (undoHandler) await undoHandler();
    undoToast && undoToast.hide();
    calendar && calendar.refetchEvents();
  });
  function showUndo(msg, handler) {
    if (!undoToast || !undoMessageEl) return;
    undoHandler = handler;
    undoMessageEl.textContent = msg;
    undoToast.show();
  }

  async function saveMoveResize(info) {
    const id = info.event.extendedProps.seriesId || info.event.id;
    const payload = {
      title: info.event.title,
      category: info.event.extendedProps.category,
      location: info.event.extendedProps.location || null,
      isAllDay: info.event.allDay,
      startUtc: info.event.start.toISOString(),
      endUtc: info.event.end.toISOString()
      // do not send description here to avoid wiping it
    };
    const prev = info.oldEvent;
    const undoPayload = {
      title: prev.title,
      category: prev.extendedProps.category,
      location: prev.extendedProps.location || null,
      isAllDay: prev.allDay,
      startUtc: prev.start.toISOString(),
      endUtc: prev.end.toISOString()
    };
    const res = await fetch(`/calendar/events/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (!res.ok) {
      const message = await readErrorMessage(res);
      info.revert();
      alert(`Update failed: ${message}`);
    } else {
      showUndo('Event updated.', async () => {
        await fetch(`/calendar/events/${id}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(undoPayload)
        });
      });
    }
  }

  const opts = {
    initialView: 'dayGridMonth',
    headerToolbar: false,
    firstDay: 1,
    height: 'auto',
    dayMaxEvents: 3,
    nowIndicator: true,
    navLinks: false,
    slotMinTime: '08:00:00',
    slotMaxTime: '18:00:00',
    scrollTime: '08:00:00',
    editable: canEdit,
    selectable: canEdit,
    businessHours: {
      daysOfWeek: [1, 2, 3, 4, 5, 6], // Mon–Sat
      startTime: '08:00',
      endTime: '18:00'
    },
    dayCellDidMount(info) {
      const iso = getIsoDate(info.dateStr || info.date);
      if (iso) decorateDayCellElement(info.el, iso);
    },
    dayHeaderDidMount(info) {
      const iso = getIsoDate(info.date);
      if (iso) decorateHeaderCellElement(info.el, iso);
    },
    eventSources: [{
      id: 'primary',
      url: '/calendar/events',
      method: 'GET',
      extraParams: () => ({ includeCelebrations: 'false' }),
      failure: (e) => { console.error('Events feed failed', e); alert('Couldn\u2019t load events. See console/Network.'); }
    }],
    eventDidMount(info) {
      const isCelebration = !!info.event.extendedProps.isCelebration;
      let categorySource = info.event.extendedProps.category;
      let celebrationPresentation = null;

      if (isCelebration) {
        celebrationPresentation = getCelebrationPresentation(info.event);
        categorySource = celebrationPresentation.type;
      }

      const key = canon(categorySource);
      info.event.setExtendedProp('category', key);
      info.el.classList.add('pm-cat-' + key.toLowerCase());

      if (celebrationPresentation) {
        info.el.classList.add('pm-celebration-event');
        const titleEl = info.el.querySelector('.fc-event-title');
        const titleContainer = titleEl?.closest('.fc-event-title-container') || titleEl?.parentElement;

        if (titleEl) {
          titleEl.textContent = celebrationPresentation.name;
          titleEl.setAttribute('data-full-title', info.event.title || celebrationPresentation.name);
        }

        if (titleContainer && !titleContainer.querySelector('.pm-celebration-event__icon')) {
          titleContainer.classList.add('pm-celebration-event__content');
          const icon = document.createElement('span');
          icon.className = 'pm-celebration-event__icon';
          icon.setAttribute('aria-hidden', 'true');
          const iconGlyph = document.createElement('i');
          iconGlyph.className = `bi ${celebrationPresentation.iconClass}`;
          icon.appendChild(iconGlyph);
          titleContainer.insertBefore(icon, titleEl || titleContainer.firstChild);
        }
      }

      const loc = info.event.extendedProps.location;
      const accessibleTitle = celebrationPresentation
        ? `${celebrationPresentation.type}: ${celebrationPresentation.name}`
        : info.event.title;
      info.el.setAttribute('title', accessibleTitle + (loc ? ' — ' + loc : ''));
      info.el.setAttribute('aria-label', info.el.title);

      if (activeCategory && key !== activeCategory) {
        info.el.style.display = 'none';
      }
      if (info.event.extendedProps.isRecurring) {
        // Birthdays and anniversaries are inherently annual; a recurrence glyph adds noise
        // and can wrap onto a second line in compact month cells.
        if (!isCelebration) {
          info.el.classList.add('pm-recurring');
        }
        info.el.querySelectorAll('.fc-event-resizer').forEach(r => r.style.display = 'none');
      }
    },
    eventAllow: (dropInfo, draggedEvent) => {
      return !draggedEvent.extendedProps?.isRecurring;
    }
  };

  if (canEdit) {
    opts.eventDrop = (info) => saveMoveResize(info);
    opts.eventResize = (info) => saveMoveResize(info);
  }

  // Only add `plugins` if we actually detected any.
  if (pluginList.length) opts.plugins = pluginList;

  calendar = new Calendar(calendarEl, opts);

  const HOLIDAYS_ENDPOINT = '/calendar/events/holidays';
  let celebrationSource = null;

  async function loadCelebrationEvents(info) {
    const params = new URLSearchParams({
      start: info.startStr,
      end: info.endStr,
      includeCelebrations: 'true'
    });

    const res = await fetch(`/calendar/events?${params}`);
    if (!res.ok) {
      const error = new Error(`Celebrations feed failed: ${res.status}`);
      error.status = res.status;
      throw error;
    }

    const data = await res.json().catch(() => []);
    return Array.isArray(data) ? data.filter(event => event?.isCelebration) : [];
  }

  async function loadHolidayEvents(info, signal) {
    const params = new URLSearchParams({
      start: info.startStr,
      end: info.endStr
    });
    const res = await fetch(`${HOLIDAYS_ENDPOINT}?${params}`, { signal });
    if (!res.ok) {
      const error = new Error(`Holidays feed failed: ${res.status}`);
      error.status = res.status;
      throw error;
    }
    const data = await res.json().catch(() => []);
    return Array.isArray(data) ? data : [];
  }

  function createCelebrationsSourceConfig() {
    return {
      id: 'celebrations',
      events(info, successCallback, failureCallback) {
        loadCelebrationEvents(info)
          .then(events => successCallback(events))
          .catch(err => {
            console.error('Celebrations feed failed', err);
            failureCallback?.(err);
            alert('Couldn\u2019t load celebrations. See console/Network.');
          });
      }
    };
  }

  function setCelebrationsStateLocal(value) {
    const bool = !!value;
    showCelebrations = bool;
    calendarEl.dataset.showCelebrations = bool ? 'true' : 'false';
    if (showCelebrationsToggle && showCelebrationsToggle.checked !== bool) {
      showCelebrationsToggle.checked = bool;
    }

    if (bool) {
      if (!celebrationSource) {
        celebrationSource = calendar.addEventSource(createCelebrationsSourceConfig());
      } else {
        celebrationSource.refetch();
      }
    } else if (celebrationSource) {
      celebrationSource.remove();
      celebrationSource = null;
    }
  }

  async function refreshHolidayEvents(info) {
    const key = `${info.startStr}|${info.endStr}`;
    if (key === holidayRangeKey) {
      refreshHolidayHighlights();
      return;
    }

    if (holidayFetchController) {
      holidayFetchController.abort();
    }
    const controller = new AbortController();
    holidayFetchController = controller;

    try {
      const items = await loadHolidayEvents(info, controller.signal);
      if (controller.signal.aborted) return;
      holidayMap.clear();
      items.forEach(item => {
        const iso = getIsoDate(item?.date || item?.Date);
        if (!iso) return;
        holidayMap.set(iso, {
          name: item?.name || item?.Name || '',
          skipWeekends: item?.skipWeekends ?? item?.SkipWeekends ?? null,
          startUtc: item?.startUtc || item?.StartUtc || null,
          endUtc: item?.endUtc || item?.EndUtc || null
        });
      });
      holidayRangeKey = key;
      refreshHolidayHighlights();
      updateCounts();
    } catch (err) {
      if (controller.signal.aborted) return;
      console.error('Holidays feed failed', err);
      if (!holidayErrorShown) {
        alert('Couldn\u2019t load holidays. See console/Network.');
        holidayErrorShown = true;
      }
      holidayRangeKey = '';
    } finally {
      if (holidayFetchController === controller) {
        holidayFetchController = null;
      }
    }
  }

  async function persistCelebrationPreference(value, previous) {
    if (!preferencesForm || !showCelebrationsToggle) return;
    showCelebrationsToggle.disabled = true;
    try {
      const response = await fetch(preferenceEndpoint, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(antiforgeryInput?.value ? { 'X-CSRF-TOKEN': antiforgeryInput.value } : {})
        },
        body: JSON.stringify({ showCelebrations: value })
      });

      if (!response.ok) {
        throw new Error(`Failed to update preference: ${response.status}`);
      }

      let payload = null;
      try { payload = await response.json(); } catch { payload = null; }
      if (payload && typeof payload.showCelebrations !== 'undefined') {
        setCelebrationsStateLocal(!!payload.showCelebrations);
      }
    } catch (err) {
      console.error(err);
      alert('Couldn\u2019t update preference. Please try again.');
      setCelebrationsStateLocal(previous);
    } finally {
      showCelebrationsToggle.disabled = false;
    }
  }

  if (showCelebrationsToggle) {
    showCelebrationsToggle.addEventListener('change', () => {
      const desired = !!showCelebrationsToggle.checked;
      const previous = showCelebrations;
      setCelebrationsStateLocal(desired);
      persistCelebrationPreference(desired, previous);
    });
  }

  setCelebrationsStateLocal(showCelebrations);

  // title handling
  const lblTitle = document.getElementById('calTitle');
  function updateTitle() {
    if (lblTitle) lblTitle.textContent = calendar.view?.title || '';
  }
  calendar.on('datesSet', updateTitle);
  calendar.on('datesSet', refreshHolidayEvents);

  // empty state handling
  const emptyEl = document.createElement('div');
  emptyEl.className = 'calendar-empty-state';
  emptyEl.style.display = 'none';
  emptyEl.textContent = canEdit
    ? 'No events scheduled in this period. Select a date or choose New event to add one.'
    : 'No events scheduled in this period.';
  calendarEl.appendChild(emptyEl);
  function updateEmptyState() {
    const has = calendar.getEvents().some(e => {
      const el = e.el; return el && el.offsetParent !== null;
    });
    emptyEl.style.display = has ? 'none' : '';
  }
  calendar.on('eventsSet', updateEmptyState);
  calendar.on('datesSet', () => setTimeout(updateEmptyState, 0));
  calendar.on('eventsSet', () => setTimeout(refreshHolidayHighlights, 0));

  // render calendar
  calendar.render();
  updateTitle();

  // prev/next
  const btnPrev = document.getElementById('btnPrev');
  const btnNext = document.getElementById('btnNext');
  btnPrev && btnPrev.addEventListener('click', () => calendar.prev(), { passive: true });
  btnNext && btnNext.addEventListener('click', () => calendar.next(), { passive: true });

  // view switches
  const viewButtons = document.querySelectorAll('[data-view]');
  viewButtons.forEach(b => {
    b.addEventListener('click', () => {
      calendar.changeView(b.getAttribute('data-view'));
    }, { passive: true });
  });
  const btnToday = document.getElementById('btnToday');
  btnToday && btnToday.addEventListener('click', () => calendar.today(), { passive: true });
  function markActiveView() {
    const v = calendar.view?.type;
    viewButtons.forEach(b => {
      const isActive = b.getAttribute('data-view') === v;
      b.classList.toggle('active', isActive);
      b.setAttribute('aria-pressed', isActive ? 'true' : 'false');
    });
  }
  calendar.on('datesSet', markActiveView);
  markActiveView();

  // responsive view switching
  const mq = window.matchMedia('(max-width: 576px)');
  function setResponsiveView(e) {
    const t = calendar.view?.type;
    if (e.matches && t !== 'listMonth') calendar.changeView('listMonth');
    else if (!e.matches && t === 'listMonth') calendar.changeView('dayGridMonth');
  }
  mq.addEventListener?.('change', setResponsiveView);
  setResponsiveView(mq);

  // category filter
  const catFilters = document.getElementById('categoryFilters');
  if (catFilters) {
    catFilters.addEventListener('click', (e) => {
      const btn = e.target.closest('button[data-cat]');
      if (!btn) return;
      catFilters.querySelectorAll('button').forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      activeCategory = btn.getAttribute('data-cat') || "";
      calendar.getEvents().forEach(ev => {
        const key = canon(ev.extendedProps.category);
        const el = ev.el; if (!el) return;
        el.style.display = (!activeCategory || key === activeCategory) ? '' : 'none';
      });
      updateCounts();
      updateEmptyState();
    });
  }

  function updateCounts() {
    const baseCategories = ['Visit', 'Insp', 'Conference', 'Other', 'Birthday', 'Anniversary'];
    const counts = Object.fromEntries(baseCategories.map(cat => [cat, 0]));
    calendar.getEvents().forEach(ev => {
      const key = canon(ev.extendedProps.category);
      counts[key] = (counts[key] || 0) + 1;
    });
    if (catFilters) {
      catFilters.querySelectorAll('button[data-cat]').forEach(btn => {
        const cat = btn.getAttribute('data-cat') || "";
        const count = cat ? counts[cat] || 0 : Object.values(counts).reduce((a,b)=>a+b,0);
        btn.textContent = `${cat || 'All'} (${count})`;
      });
    }
    const legend = document.getElementById('categoryLegend');
    if (legend) {
      const legendCats = [...baseCategories, ...Object.keys(counts).filter(cat => !baseCategories.includes(cat))];
      const visibleCats = legendCats.filter(cat => (counts[cat] || 0) > 0);
      let html = visibleCats.map(cat => {
        const n = counts[cat] || 0;
        const label = cat === 'Insp' ? 'Inspection' : cat;
        return `<span><span class="legend-dot pm-cat-${cat.toLowerCase()}"></span>${label} (${n})</span>`;
      }).join('');
      const holidayCount = holidayMap.size;
      if (holidayCount > 0) {
        html += `<span><span class="legend-dot pm-holiday"></span>Holiday (${holidayCount})</span>`;
      }
      legend.innerHTML = html;
    }
  }
  calendar.on('eventsSet', updateCounts);
  calendar.on('datesSet', () => setTimeout(updateCounts, 0));

  const viewCanvas = document.getElementById('eventDetailsCanvas');
  const viewTitle = document.getElementById('eventDetailsLabel');
  const viewTime = document.getElementById('eventDetailsTime');
  const viewCategory = document.getElementById('eventDetailsCategory');
  const viewLocation = document.getElementById('eventDetailsLocation');
  const viewDescription = document.getElementById('eventDetailsDescription');
  const btnAddToTasks = document.getElementById('btnAddToTasks');
  let currentTaskUrl = null;

  const toDate = (value) => {
    if (!value) return null;
    return value instanceof Date ? value : new Date(value);
  };

  function showEventDetails(payload) {
    if (!viewCanvas) return;
    const start = toDate(payload.start) || new Date();
    const endRaw = toDate(payload.end);
    const end = endRaw || start;

    if (viewTitle) viewTitle.textContent = payload.title || '';
    if (viewCategory) viewCategory.textContent = canon(payload.category);
    if (viewLocation) viewLocation.textContent = payload.location || '';
    if (viewDescription) {
      if (payload.descriptionHtml) {
        viewDescription.innerHTML = payload.descriptionHtml;
      } else {
        viewDescription.textContent = payload.description || '';
      }
    }

    if (viewTime) {
      if (payload.allDay) {
        const endInc = new Date(end);
        endInc.setDate(endInc.getDate() - 1);
        const endDisplay = payload.end ? endInc : start;
        viewTime.textContent = `${formatDisplayDate(start)} – ${formatDisplayDate(endDisplay)}`;
      } else {
        viewTime.textContent = `${formatDisplayDateTime(start)} – ${formatDisplayDateTime(end)}`;
      }
    }

    currentTaskUrl = payload.taskUrl || null;
    if (btnAddToTasks) {
      const hasTaskUrl = !!currentTaskUrl;
      btnAddToTasks.disabled = !hasTaskUrl;
      btnAddToTasks.classList.toggle('d-none', !hasTaskUrl);
    }

    bootstrap.Offcanvas.getOrCreateInstance(viewCanvas).show();
  }

  btnAddToTasks && btnAddToTasks.addEventListener('click', async () => {
    if (!currentTaskUrl) return;
    btnAddToTasks.disabled = true;
    try {
      const response = await fetch(currentTaskUrl, { method: 'POST' });
      if (!response.ok) {
        alert('Failed to add to tasks');
        return;
      }
      bootstrap.Offcanvas.getOrCreateInstance(viewCanvas).hide();
    } catch (err) {
      console.error(err);
      alert('Failed to add to tasks');
    } finally {
      btnAddToTasks.disabled = false;
    }
  });

  // Offcanvas form handling (create/edit) — only if editors
  if (canEdit) {
    let editingOriginal = null;

    calendar.setOption('eventClick', async (arg) => {
      if (!form) return;
      const ev = arg.event;

      if (ev.extendedProps.isCelebration) {
        showEventDetails({
          title: ev.title,
          start: ev.start,
          end: ev.end,
          allDay: ev.allDay,
          category: canon(ev.extendedProps.category),
          location: ev.extendedProps.location || '',
          description: ev.extendedProps.description || '',
          taskUrl: ev.extendedProps.taskUrl || null
        });
        return;
      }

      const seriesId = ev.extendedProps.seriesId || ev.id;
      const res = await fetch(`/calendar/events/${seriesId}`);
      if (!res.ok) {
        console.error('Failed to load event');
        alert('The event could not be loaded. Please try again.');
        return;
      }

      const data = await res.json();
      form.reset();
      resetEditorValidation();
      setEditorMode(true);
      editingOriginal = data;

      idBox.value = data.id;
      titleBox.value = data.title || '';
      catBox.value = data.category || 'Other';
      locBox.value = data.location || '';
      descBox.value = data.rawDescription || '';
      isAllDayBox.checked = !!data.allDay;
      setAllDayUI(isAllDayBox.checked);
      hydrateRepeatUI(data.recurrenceRule, data.recurrenceUntilUtc);
      setAdvancedOptionsOpen(!!(data.recurrenceRule || data.recurrenceUntilUtc || data.rawDescription));

      const start = new Date(data.start);
      const end = new Date(data.end);
      if (!isValidDate(start) || !isValidDate(end)) {
        alert('The stored event contains an invalid date range and cannot be edited safely.');
        return;
      }

      if (isAllDayBox.checked) {
        const endInclusive = new Date(end);
        endInclusive.setDate(endInclusive.getDate() - 1);
        setAllDayValues(start, endInclusive);
        const timedStart = atLocalTime(start, 9, 0);
        setTimedValues(timedStart, new Date(timedStart.getTime() + 60 * 60 * 1000));
      } else {
        setTimedValues(start, end);
        setAllDayValues(start, end);
      }

      validateRepeatEnd();
      updateDurationHint();
      resetEditorScroll();
      btnDelete?.classList.remove('d-none');
      bootstrap.Offcanvas.getOrCreateInstance(document.getElementById('eventFormCanvas')).show();
      window.setTimeout(() => titleBox?.focus(), 250);
    });

    const btnNew = document.getElementById('btnNewEvent');

    function openNewEvent(start = null, end = null, allDay = false) {
      if (!form) return;
      form.reset();
      resetEditorValidation();
      setEditorMode(false);
      editingOriginal = null;
      idBox.value = '';
      hydrateRepeatUI(null);
      setAdvancedOptionsOpen(false);
      isAllDayBox.checked = !!allDay;
      setAllDayUI(!!allDay);
      btnDelete?.classList.add('d-none');

      if (allDay) {
        const startValue = start ? new Date(start) : new Date();
        const endExclusive = end ? new Date(end) : new Date(startValue.getTime() + 24 * 60 * 60 * 1000);
        const inclusiveEnd = new Date(endExclusive);
        inclusiveEnd.setDate(inclusiveEnd.getDate() - 1);
        setAllDayValues(startValue, inclusiveEnd);
        const timedStart = atLocalTime(startValue, 9, 0);
        setTimedValues(timedStart, new Date(timedStart.getTime() + 60 * 60 * 1000));
      } else {
        const suppliedStart = start ? new Date(start) : null;
        const startValue = isValidDate(suppliedStart)
          ? suppliedStart
          : getDefaultTimedStart();
        const endValueCandidate = end ? new Date(end) : null;
        const endValue = isValidDate(endValueCandidate) && endValueCandidate > startValue
          ? endValueCandidate
          : new Date(startValue.getTime() + 60 * 60 * 1000);
        setTimedValues(startValue, endValue);
        setAllDayValues(startValue, endValue);
      }

      validateRepeatEnd();
      updateDurationHint();
      resetEditorScroll();
      bootstrap.Offcanvas.getOrCreateInstance(document.getElementById('eventFormCanvas')).show();
      window.setTimeout(() => titleBox?.focus(), 250);
    }

    btnNew?.addEventListener('click', () => openNewEvent());

    calendar.setOption('dateClick', (info) => {
      if ((calendar.view?.type || '').startsWith('dayGrid')) {
        openNewEvent(getDefaultTimedStart(info.date), null, false);
      } else {
        openNewEvent(info.date, null, info.allDay);
      }
    });

    calendar.setOption('select', (info) => {
      openNewEvent(info.start, info.end, info.allDay);
      calendar.unselect();
    });

    form.addEventListener('submit', async (ev) => {
      ev.preventDefault();
      clearFormAlert();

      const trimmedTitle = titleBox.value.trim();
      titleBox.setCustomValidity(trimmedTitle ? '' : 'Enter a title for the event.');

      validateDateRange();
      validateRepeatEnd();
      form.classList.add('was-validated');

      if (!form.checkValidity()) {
        revealInvalidField(form.querySelector(':invalid'));
        return;
      }

      const fd = new FormData(form);
      const isAllDay = !!isAllDayBox.checked;
      let startUtc;
      let endUtc;
      let startLocalIso;

      if (isAllDay) {
        const start = new Date(`${startDateAllDayBox.value}T00:00:00`);
        const end = new Date(`${endDateAllDayBox.value}T00:00:00`);
        if (!isValidDate(start) || !isValidDate(end)) {
          showFormAlert('Select a valid all-day date range.');
          return;
        }
        end.setDate(end.getDate() + 1); // API uses an exclusive all-day end.
        startUtc = start.toISOString();
        endUtc = end.toISOString();
        startLocalIso = `${startDateAllDayBox.value}T00:00`;
      } else {
        const start = getTimedStart();
        const end = getTimedEnd();
        if (!isValidDate(start) || !isValidDate(end)) {
          showFormAlert('Select a valid start and end date/time.');
          return;
        }
        startUtc = start.toISOString();
        endUtc = end.toISOString();
        startLocalIso = `${startDateTimedBox.value}T${startTimeTimedBox.value}`;
      }
      const rrule = buildRRule(startLocalIso);
      const recurrenceUntilUtc = rrule && repeatEndOn?.checked && repeatUntil?.value
        ? new Date(`${repeatUntil.value}T23:59:59`).toISOString()
        : null;

      const dto = {
        title: trimmedTitle,
        description: (fd.get('description') || '').toString().trim(),
        category: (fd.get('category') || 'Other').toString(),
        location: (fd.get('location') || '').toString().trim() || null,
        isAllDay,
        startUtc,
        endUtc,
        recurrenceRule: rrule,
        recurrenceUntilUtc
      };

      const id = fd.get('id');
      const url = id ? `/calendar/events/${id}` : '/calendar/events';
      const method = id ? 'PUT' : 'POST';

      let undoPayload = null;
      if (id && editingOriginal) {
        undoPayload = {
          title: editingOriginal.title,
          description: editingOriginal.rawDescription,
          category: editingOriginal.category,
          location: editingOriginal.location,
          isAllDay: editingOriginal.allDay,
          startUtc: new Date(editingOriginal.start).toISOString(),
          endUtc: new Date(editingOriginal.end).toISOString(),
          recurrenceRule: editingOriginal.recurrenceRule || null,
          recurrenceUntilUtc: editingOriginal.recurrenceUntilUtc
            ? new Date(editingOriginal.recurrenceUntilUtc).toISOString()
            : null
        };
      }

      setSaving(true);
      try {
        const response = await fetch(url, {
          method,
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(dto)
        });

        if (!response.ok) {
          const message = await readErrorMessage(response);
          showFormAlert(`The event could not be saved. ${message}`);
          return;
        }

        let createdId = null;
        if (!id) {
          const payload = await response.json();
          createdId = payload.id;
        }

        form.reset();
        setAllDayUI(false);
        hydrateRepeatUI(null);
        setAdvancedOptionsOpen(false);
        resetEditorValidation();
        btnDelete?.classList.add('d-none');

        const canvasEl = document.getElementById('eventFormCanvas');
        const canvas = canvasEl ? bootstrap.Offcanvas.getOrCreateInstance(canvasEl) : null;
        canvas?.hide();
        editingOriginal = null;
        calendar.refetchEvents();

        if (id && undoPayload) {
          showUndo('Event saved.', async () => {
            await fetch(`/calendar/events/${id}`, {
              method: 'PUT',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify(undoPayload)
            });
          });
        } else if (createdId) {
          showUndo('Event created.', async () => {
            await fetch(`/calendar/events/${createdId}`, { method: 'DELETE' });
          });
        }
      } catch (error) {
        console.error(error);
        showFormAlert('The event could not be saved because the server could not be reached.');
      } finally {
        setSaving(false);
      }
    });

    btnDelete?.addEventListener('click', async () => {
      const id = idBox.value;
      if (!id || !confirm('Delete this event?')) return;

      clearFormAlert();
      btnDelete.disabled = true;
      try {
        const response = await fetch(`/calendar/events/${id}`, { method: 'DELETE' });
        if (!response.ok) {
          showFormAlert('The event could not be deleted. Please try again.');
          return;
        }

        bootstrap.Offcanvas.getOrCreateInstance(document.getElementById('eventFormCanvas')).hide();
        form.reset();
        setAllDayUI(false);
        hydrateRepeatUI(null);
        setAdvancedOptionsOpen(false);
        resetEditorValidation();
        btnDelete.classList.add('d-none');
        editingOriginal = null;
        calendar.refetchEvents();
      } catch (error) {
        console.error(error);
        showFormAlert('The event could not be deleted because the server could not be reached.');
      } finally {
        btnDelete.disabled = false;
      }
    });
  } else {
    // Read-only event details for non-editors
    calendar.setOption('eventClick', async (arg) => {
      const ev = arg.event;

      if (ev.extendedProps.isCelebration) {
        showEventDetails({
          title: ev.title,
          start: ev.start,
          end: ev.end,
          allDay: ev.allDay,
          category: canon(ev.extendedProps.category),
          location: ev.extendedProps.location || '',
          description: ev.extendedProps.description || '',
          taskUrl: ev.extendedProps.taskUrl || null
        });
        return;
      }

      const seriesId = ev.extendedProps.seriesId || ev.id;
      const res = await fetch(`/calendar/events/${seriesId}`);
      if (!res.ok) { console.error('Failed to load event'); return; }
      const data = await res.json();
      showEventDetails({
        title: data.title,
        start: data.start,
        end: data.end,
        allDay: data.allDay,
        category: data.category,
        location: data.location || '',
        descriptionHtml: data.description || '',
        taskUrl: ev.extendedProps.taskUrl || `/calendar/events/${seriesId}/task`
      });
    });
  }
})();

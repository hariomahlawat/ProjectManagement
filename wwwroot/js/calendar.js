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
  const toLocalInputValue = (d) => {
    const dt = new Date(d);
    return `${dt.getFullYear()}-${pad(dt.getMonth()+1)}-${pad(dt.getDate())}T${pad(dt.getHours())}:${pad(dt.getMinutes())}`;
  };

  const toLocalDateInputValue = (d) => {
    const dt = new Date(d);
    return `${dt.getFullYear()}-${pad(dt.getMonth()+1)}-${pad(dt.getDate())}`;
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

  // cache form elements
  const form = document.getElementById('eventForm');
  const titleBox = form ? form.querySelector('[name="title"]') : null;
  const idBox = form ? form.querySelector('[name="id"]') : null;
  const catBox = form ? form.querySelector('[name="category"]') : null;
  const locBox = form ? form.querySelector('[name="location"]') : null;
  const descBox = form ? form.querySelector('[name="description"]') : null;
  const isAllDayBox = document.getElementById('toggleAllDay');
  const timePickers = document.getElementById('timePickers');
  const datePickers = document.getElementById('datePickers');
  const btnDelete = document.getElementById('btnDeleteEvent');

  // ----- repeat UI wiring -----
  const repeatFreq     = document.getElementById('repeatFreq');
  const repeatWeekly   = document.getElementById('repeatWeekly');
  const repeatMonthly  = document.getElementById('repeatMonthly');
  const repeatMonthDay = document.getElementById('repeatMonthDay');
  const repeatUntil    = document.getElementById('repeatUntil');

  repeatFreq?.addEventListener('change', () => {
    repeatWeekly.classList.toggle('d-none', repeatFreq.value !== 'WEEKLY');
    repeatMonthly.classList.toggle('d-none', repeatFreq.value !== 'MONTHLY');
  });

  function buildRRule(startLocalIso) {
    const f = repeatFreq?.value || '';
    if (!f) return null;
    const parts = [`FREQ=${f}`, 'INTERVAL=1'];

    if (f === 'WEEKLY') {
      const days = Array.from(repeatWeekly.querySelectorAll('input:checked')).map(x => x.value);
      if (!days.length) {
        const map = ['SU','MO','TU','WE','TH','FR','SA'];
        days.push(map[new Date(startLocalIso).getDay()]);
      }
      parts.push(`BYDAY=${days.join(',')}`);
    }
    if (f === 'MONTHLY') {
      const d = parseInt(repeatMonthDay.value, 10) || new Date(startLocalIso).getDate();
      parts.push(`BYMONTHDAY=${d}`);
    }
    const endChoice = (document.querySelector('input[name="repeatEnd"]:checked')||{}).value;
    if (endChoice === 'on' && repeatUntil.value) {
      const u = new Date(repeatUntil.value + 'T23:59:59');
      const z = new Date(Date.UTC(u.getFullYear(), u.getMonth(), u.getDate(), 23,59,59));
      parts.push(`UNTIL=${z.toISOString().replace(/[-:]/g,'').split('.')[0]}Z`);
    }
    return parts.join(';');
  }

  function hydrateRepeatUI(rrule) {
    if (!repeatFreq) return;
    repeatFreq.value = '';
    repeatWeekly.classList.add('d-none');
    repeatMonthly.classList.add('d-none');
    repeatWeekly.querySelectorAll('input').forEach(i => i.checked=false);
    repeatMonthDay.value = '';
    repeatUntil.value='';
    document.querySelector('input[name="repeatEnd"][value="never"]').checked = true;
    if (!rrule) return;

    const m = Object.fromEntries(rrule.split(';').map(p => p.split('=')));
    if (m.FREQ === 'WEEKLY') {
      repeatFreq.value='WEEKLY'; repeatWeekly.classList.remove('d-none');
      (m.BYDAY||'').split(',').forEach(code => { const box = repeatWeekly.querySelector(`input[value="${code}"]`); if (box) box.checked = true; });
    } else if (m.FREQ === 'MONTHLY') {
      repeatFreq.value='MONTHLY'; repeatMonthly.classList.remove('d-none');
      if (m.BYMONTHDAY) repeatMonthDay.value = m.BYMONTHDAY;
    }
    if (m.UNTIL) {
      const y = m.UNTIL.slice(0,4), mo=m.UNTIL.slice(4,6), d=m.UNTIL.slice(6,8);
      repeatUntil.value = `${y}-${mo}-${d}`;
      document.querySelector('input[name="repeatEnd"][value="on"]').checked = true;
    }
  }
  function setAllDayUI(on) {
    if (!timePickers || !datePickers) return;
    if (on) { timePickers.classList.add('d-none'); datePickers.classList.remove('d-none'); }
    else    { datePickers.classList.add('d-none'); timePickers.classList.remove('d-none'); }
  }
  isAllDayBox && isAllDayBox.addEventListener('change', () => setAllDayUI(isAllDayBox.checked));

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
      let msg = await res.text();
      try { const j = JSON.parse(msg); msg = j.detail || j.title || j; } catch {}
      info.revert();
      alert(`Update failed: ${msg || res.status}`);
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
      let categorySource = info.event.extendedProps.category;
      if (info.event.extendedProps.isCelebration) {
        categorySource = info.event.extendedProps.celebrationType || categorySource;
        if (!categorySource) {
          const match = (info.event.title || '').match(/^([^:]+):/);
          if (match) categorySource = match[1];
        }
      }
      const key = canon(categorySource);
      info.event.setExtendedProp('category', key);
      info.el.classList.add('pm-cat-' + key.toLowerCase());
      const loc = info.event.extendedProps.location;
      info.el.setAttribute('title',
        info.event.title + (loc ? ' — ' + loc : '')
      );
      info.el.setAttribute('aria-label', info.el.title);

      if (activeCategory && key !== activeCategory) {
        info.el.style.display = 'none';
      }
      if (info.event.extendedProps.isRecurring) {
        info.el.classList.add('pm-recurring');
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

  const CELEBRATIONS_ENDPOINT = '/calendar/events/celebrations';
  const HOLIDAYS_ENDPOINT = '/calendar/events/holidays';
  let celebrationSource = null;

  async function loadCelebrationEvents(info) {
    const params = new URLSearchParams({
      start: info.startStr,
      end: info.endStr
    });

    const tryFetch = async (url) => {
      const res = await fetch(url);
      if (!res.ok) {
        const error = new Error(`Celebrations feed failed: ${res.status}`);
        error.status = res.status;
        throw error;
      }
      const data = await res.json().catch(() => []);
      return Array.isArray(data) ? data : [];
    };

    try {
      return await tryFetch(`${CELEBRATIONS_ENDPOINT}?${params}`);
    } catch (err) {
      if (err && typeof err === 'object' && 'status' in err && err.status === 404) {
        try {
          const fallback = await tryFetch(`/calendar/events?${params}&includeCelebrations=true`);
          return fallback.filter(ev => ev?.isCelebration);
        } catch (fallbackErr) {
          throw fallbackErr;
        }
      }
      throw err;
    }
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
          ...(antiforgeryInput?.value ? { 'RequestVerificationToken': antiforgeryInput.value } : {})
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
  emptyEl.className = 'text-muted text-center py-5';
  emptyEl.style.display = 'none';
  emptyEl.textContent = 'No events in this period.';
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
      b.classList.toggle('active', b.getAttribute('data-view') === v);
      b.classList.toggle('btn-secondary', b.classList.contains('active'));
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
      let html = legendCats.map(cat => {
        const n = counts[cat] || 0;
        return `<span class="me-3"><span class="legend-dot pm-cat-${cat.toLowerCase()}"></span>${cat} (${n})</span>`;
      }).join('');
      const holidayCount = holidayMap.size;
      html += `<span class="me-3"><span class="legend-dot pm-holiday"></span>Holiday (${holidayCount})</span>`;
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
    if (viewDescription) viewDescription.innerHTML = payload.descriptionHtml || payload.description || '';

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
          descriptionHtml: ev.extendedProps.description || '',
          taskUrl: ev.extendedProps.taskUrl || null
        });
        return;
      }

      const seriesId = ev.extendedProps.seriesId || ev.id;
      const res = await fetch(`/calendar/events/${seriesId}`);
      if (!res.ok) { console.error('Failed to load event'); return; }
      const data = await res.json();
      editingOriginal = data;
      idBox.value = data.id;
      titleBox.value = data.title || '';
      catBox.value = data.category || 'Other';
      locBox.value = data.location || '';
      descBox.value = data.rawDescription || '';
      isAllDayBox.checked = !!data.allDay;
      setAllDayUI(isAllDayBox.checked);
      hydrateRepeatUI(data.recurrenceRule);
      if (isAllDayBox.checked) {
        const start = new Date(data.start);
        const endEx = new Date(data.end); endEx.setDate(endEx.getDate() - 1);
        form.querySelector('[name="startDate"]').value = toLocalDateInputValue(start);
        form.querySelector('[name="endDate"]').value = toLocalDateInputValue(endEx);
      } else {
        form.querySelector('[name="start"]').value = toLocalInputValue(data.start);
        form.querySelector('[name="end"]').value = toLocalInputValue(data.end);
      }
      document.getElementById('eventFormLabel').textContent = 'Edit event';
      btnDelete && btnDelete.classList.remove('d-none');
      bootstrap.Offcanvas.getOrCreateInstance(document.getElementById('eventFormCanvas')).show();
    });

    const btnNew = document.getElementById('btnNewEvent');
    btnNew && btnNew.addEventListener('click', () => {
      if (!form) return;
      form.reset();
      editingOriginal = null;
      idBox.value = '';
      isAllDayBox.checked = false;
      setAllDayUI(false);
      hydrateRepeatUI(null);
      btnDelete && btnDelete.classList.add('d-none');
      const lbl = document.getElementById('eventFormLabel');
      lbl && (lbl.textContent = 'New event');
    });

    form && form.addEventListener('submit', async (ev) => {
      ev.preventDefault();
      const fd = new FormData(form);
      const isAllDay = !!fd.get('isAllDay');

      let startUtc, endUtc;
      if (isAllDay) {
        const s = fd.get('startDate'); const e = fd.get('endDate');
        if (!s || !e) { alert('Select start & end dates.'); return; }
        const start = new Date(`${s}T00:00:00`);
        const end   = new Date(`${e}T00:00:00`); end.setDate(end.getDate()+1); // exclusive
        startUtc = start.toISOString(); endUtc = end.toISOString();
      } else {
        const s = fd.get('start'); const e = fd.get('end');
        if (!s || !e) { alert('Select start & end times.'); return; }
        startUtc = new Date(s).toISOString();
        endUtc   = new Date(e).toISOString();
      }
      if (new Date(endUtc) <= new Date(startUtc)) { alert('End must be after start.'); return; }

      const startLocalIso = isAllDay
        ? `${fd.get('startDate')}T00:00`
        : fd.get('start');
      const rrule = buildRRule(startLocalIso);

      const dto = {
        title: (fd.get('title')||'').toString().trim(),
        description: (fd.get('description')||'').toString(),
        category: (fd.get('category')||'Other').toString(),
        location: (fd.get('location')||'').toString() || null,
        isAllDay,
        startUtc, endUtc,
        recurrenceRule: rrule,
        recurrenceUntilUtc: repeatUntil.value ? new Date(repeatUntil.value + 'T23:59:59').toISOString() : null
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
          endUtc: new Date(editingOriginal.end).toISOString()
        };
      }

      const r = await fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(dto)
      });
      if (!r.ok) {
        let msg = await r.text();
        try { const j = JSON.parse(msg); msg = j.detail || j.title || j; } catch {}
        alert(`Save failed: ${msg || r.status}`);
        return;
      }

      let createdId = null;
      if (!id) {
        const j = await r.json();
        createdId = j.id;
      }

      form.reset(); setAllDayUI(false); hydrateRepeatUI(null);
      btnDelete && btnDelete.classList.add('d-none');
      const canvasEl = document.getElementById('eventFormCanvas');
      const canvas = canvasEl ? bootstrap.Offcanvas.getOrCreateInstance(canvasEl) : null;
      canvas && canvas.hide();
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
    });

    btnDelete && btnDelete.addEventListener('click', async () => {
      const id = idBox.value;
      if (!id) return;
      if (!confirm('Delete this event?')) return;
      const r = await fetch(`/calendar/events/${id}`, { method: 'DELETE' });
      if (!r.ok) { alert('Delete failed'); return; }
      bootstrap.Offcanvas.getOrCreateInstance(document.getElementById('eventFormCanvas')).hide();
      form.reset(); setAllDayUI(false); hydrateRepeatUI(null);
      btnDelete.classList.add('d-none');
      calendar.refetchEvents();
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
          descriptionHtml: ev.extendedProps.description || '',
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

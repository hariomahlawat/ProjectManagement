(function () {
  // FullCalendar globals from index.global.min.js (bundle or individual files)
  const FC = window.FullCalendar;
  const Calendar = FC && FC.Calendar;
  if (!Calendar) { console.error('FullCalendar global bundle missing'); return; }

  // Build a plugin list that only includes plugins that actually exist
  const pluginList = [];
  // Prefer FC.<plugin>; fall back to legacy window.FullCalendar<Plugin> globals
  const dg = (FC && FC.dayGrid) || window.FullCalendarDayGrid;
  const tg = (FC && FC.timeGrid) || window.FullCalendarTimeGrid;
  const ls = (FC && FC.list)     || window.FullCalendarList;
  const ia = (FC && FC.interaction) || window.FullCalendarInteraction;
  if (dg) pluginList.push(dg);
  if (tg) pluginList.push(tg);
  if (ls) pluginList.push(ls);
  if (ia) pluginList.push(ia);

  const calendarEl = document.getElementById('calendar');
  if (!calendarEl) return;

  const canEdit = (calendarEl.dataset.canEdit || '').toLowerCase() === 'true';

  const canonMap = { visit: 'Visit', insp: 'Insp', inspection: 'Insp', conference: 'Conference' };
  const canon = (raw) => {
    const str = (raw || '').toString();
    return canonMap[str.toLowerCase()] || (str || 'Other');
  };
  let activeCategory = "";

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

  const monthFormatter = new Intl.DateTimeFormat('en-GB', { month: 'short' });
  const formatDisplayDate = (date) => {
    const d = date instanceof Date ? date : new Date(date);
    return `${pad(d.getDate())} ${monthFormatter.format(d)} ${d.getFullYear()}`;
  };
  const formatDisplayDateTime = (date) => {
    const d = date instanceof Date ? date : new Date(date);
    return `${formatDisplayDate(d)} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
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
    eventSources: [{
      url: '/calendar/events',
      method: 'GET',
      failure: (e) => { console.error('Events feed failed', e); alert('Couldn\u2019t load events. See console/Network.'); }
    }],
    eventDidMount(info) {
      const key = canon(info.event.extendedProps.category);
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

  const calendar = new Calendar(calendarEl, opts);

  // title handling
  const lblTitle = document.getElementById('calTitle');
  function updateTitle() {
    if (lblTitle) lblTitle.textContent = calendar.view?.title || '';
  }
  calendar.on('datesSet', updateTitle);

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
    const counts = { Visit:0, Insp:0, Conference:0, Other:0 };
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
      legend.innerHTML = Object.entries(counts).map(([cat, n]) =>
        `<span class="me-3"><span class="legend-dot pm-cat-${cat.toLowerCase()}"></span>${cat} (${n})</span>`
      ).join('');
    }
  }
  calendar.on('eventsSet', updateCounts);
  calendar.on('datesSet', () => setTimeout(updateCounts, 0));

  // Offcanvas form handling (create/edit) — only if editors
  if (canEdit) {
    let editingOriginal = null;

    calendar.setOption('eventClick', async (arg) => {
      if (!form) return;
      const ev = arg.event;
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
    const viewCanvas = document.getElementById('eventDetailsCanvas');
    const viewTitle = document.getElementById('eventDetailsLabel');
    const viewTime = document.getElementById('eventDetailsTime');
    const viewCategory = document.getElementById('eventDetailsCategory');
    const viewLocation = document.getElementById('eventDetailsLocation');
    const viewDescription = document.getElementById('eventDetailsDescription');
    const btnAddToTasks = document.getElementById('btnAddToTasks');
    let viewEventId = null;

    calendar.setOption('eventClick', async (arg) => {
      const seriesId = arg.event.extendedProps.seriesId || arg.event.id;
      const res = await fetch(`/calendar/events/${seriesId}`);
      if (!res.ok) { console.error('Failed to load event'); return; }
      const data = await res.json();
      viewEventId = data.id;
      viewTitle.textContent = data.title;
      const start = new Date(data.start);
      const end = new Date(data.end);
      if (data.allDay) {
        const endInc = new Date(end); endInc.setDate(endInc.getDate() - 1);
        viewTime.textContent = `${formatDisplayDate(start)} – ${formatDisplayDate(endInc)}`;
      } else {
        viewTime.textContent = `${formatDisplayDateTime(start)} – ${formatDisplayDateTime(end)}`;
      }
      viewCategory.textContent = data.category;
      viewLocation.textContent = data.location || '';
      viewDescription.innerHTML = data.description || '';
      bootstrap.Offcanvas.getOrCreateInstance(viewCanvas).show();
    });

    btnAddToTasks && btnAddToTasks.addEventListener('click', async () => {
      if (!viewEventId) return;
      const r = await fetch(`/calendar/events/${viewEventId}/task`, { method: 'POST' });
      if (!r.ok) { alert('Failed to add to tasks'); return; }
      bootstrap.Offcanvas.getOrCreateInstance(viewCanvas).hide();
    });
  }
})();

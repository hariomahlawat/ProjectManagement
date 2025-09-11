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

  // helpers
  const pad = (n) => String(n).padStart(2,'0');
  const toLocalInputValue = (d) => {
    const dt = new Date(d);
    return `${dt.getFullYear()}-${pad(dt.getMonth()+1)}-${pad(dt.getDate())}T${pad(dt.getHours())}:${pad(dt.getMinutes())}`;
  };

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
      daysOfWeek: [1, 2, 3, 4, 5], // Mon-Fri
      startTime: '08:00',
      endTime: '18:00'
    },
    eventSources: [{
      url: '/calendar/events',
      method: 'GET',
      failure: () => console.error('Failed to load events.')
    }],
    eventDidMount(info) {
      const cat = (info.event.extendedProps.category || '').toString().toLowerCase();
      info.el.classList.add('pm-cat-' + cat);
      const loc = info.event.extendedProps.location;
      info.el.title = info.event.title + (loc ? (' — ' + loc) : '');
      info.el.setAttribute('aria-label', info.el.title);
      if (activeCategory && info.event.extendedProps.category !== activeCategory) {
        info.el.style.display = 'none';
      }
    },
    eventClick: async (_arg) => {
      // hook up offcanvas details here if you want
    },
    eventDrop: (info) => saveMoveResize(info),
    eventResize: (info) => saveMoveResize(info)
  };

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
  let activeCategory = "";
  if (catFilters) {
    catFilters.querySelectorAll('button').forEach(btn => {
      btn.addEventListener('click', () => {
        catFilters.querySelectorAll('button').forEach(x => x.classList.remove('active'));
        btn.classList.add('active');
        activeCategory = btn.getAttribute('data-cat') || "";
        // toggle visibility of already-rendered events
        calendar.getEvents().forEach(ev => {
          const el = ev.el;
          if (!el) return;
          el.style.display = (!activeCategory || activeCategory === ev.extendedProps.category) ? '' : 'none';
        });
      }, { passive: true });
    });
  }

  // Offcanvas form handling (create/edit) — only if editors
  if (canEdit) {
    const form = document.getElementById('eventForm');
    const toggleAllDay = document.getElementById('toggleAllDay');
    const timePickers = document.getElementById('timePickers');
    const datePickers = document.getElementById('datePickers');
    const setAllDayUI = (on) => {
      if (on) { timePickers.classList.add('d-none'); datePickers.classList.remove('d-none'); }
      else    { datePickers.classList.add('d-none'); timePickers.classList.remove('d-none'); }
    };
    toggleAllDay && toggleAllDay.addEventListener('change', () => setAllDayUI(toggleAllDay.checked));

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

      const dto = {
        title: (fd.get('title')||'').toString().trim(),
        description: (fd.get('description')||'').toString(),
        category: (fd.get('category')||'Other').toString(),
        location: (fd.get('location')||'').toString() || null,
        isAllDay,
        startUtc, endUtc
      };

      const id = fd.get('id');
      const url = id ? `/calendar/events/${id}` : '/calendar/events';
      const method = id ? 'PUT' : 'POST';

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

      // reset + close
      form.reset(); setAllDayUI(false);
      const canvasEl = document.getElementById('eventFormCanvas');
      const canvas = canvasEl ? bootstrap.Offcanvas.getOrCreateInstance(canvasEl) : null;
      canvas && canvas.hide();
      calendar.refetchEvents();
    });

    async function saveMoveResize(info) {
      const id = info.event.id;
      const payload = {
        title: info.event.title,
        category: info.event.extendedProps.category,
        location: info.event.extendedProps.location || null,
        isAllDay: info.event.allDay,
        startUtc: info.event.start.toISOString(),
        endUtc: info.event.end.toISOString()
        // do not send description here to avoid wiping it
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
      }
    }
  }
})();

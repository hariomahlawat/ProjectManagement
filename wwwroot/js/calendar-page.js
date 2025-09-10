// Initializes FullCalendar and wires up offcanvas + CRUD
(function () {
  if (!window.FullCalendar || !window.bootstrap) return;

  const Calendar = window.FullCalendar.Calendar;
  const dayGridPlugin = window.FullCalendar.dayGrid;
  const timeGridPlugin = window.FullCalendar.timeGrid;
  const listPlugin = window.FullCalendar.list;
  const interactionPlugin = window.FullCalendar.interaction;

  function pad(n){ return String(n).padStart(2, '0'); }
  function toLocalInputValue(date){
    return date.getFullYear() + '-' +
           pad(date.getMonth()+1) + '-' +
           pad(date.getDate()) + 'T' +
           pad(date.getHours()) + ':' +
           pad(date.getMinutes());
  }

  const el = document.getElementById('calendar');
  if (!el) return;

  const detailsCanvasEl = document.getElementById('eventDetailsCanvas');
  const detailsCanvas = detailsCanvasEl ? new bootstrap.Offcanvas(detailsCanvasEl) : null;

  const formCanvasEl = document.getElementById('eventFormCanvas');
  const formCanvas = formCanvasEl ? new bootstrap.Offcanvas(formCanvasEl) : null;

  const viewButtons = document.querySelectorAll('[data-view]');
  const btnToday = document.getElementById('btnToday');
  const catFilters = document.getElementById('categoryFilters');

  let activeCategory = ""; // all

  const calendar = new Calendar(el, {
    plugins: [dayGridPlugin, timeGridPlugin, listPlugin, interactionPlugin],
    initialView: 'dayGridMonth',
    headerToolbar: false,
    firstDay: 1, // Monday
    slotMinTime: '08:00:00',
    slotMaxTime: '18:00:00',
    scrollTime: '08:00:00',
    editable: !!formCanvas, // editors only (page rendered form offcanvas if can edit)
    selectable: !!formCanvas,
    eventSources: [{
      url: '/calendar/events',
      method: 'GET',
      extraParams: () => ({}),
      failure: () => console.error('Failed to load events')
    }],
    eventDidMount: (info) => {
      // Category color via CSS class
      const cat = (info.event.extendedProps.category || '').toLowerCase();
      info.el.classList.add('pm-cal-cat-' + cat);
      // Tooltip
      const loc = info.event.extendedProps.location;
      const tip = document.createElement('div');
      tip.className = 'visually-hidden';
      tip.innerText = info.event.title + (loc ? (' — ' + loc) : '');
      info.el.setAttribute('aria-label', tip.innerText);
      info.el.title = tip.innerText;
      // Filter by category
      if (activeCategory && (info.event.extendedProps.category !== activeCategory)) {
        info.el.style.display = 'none';
      }
    },
    eventClick: async (arg) => {
      const id = arg.event.id;
      const res = await fetch(`/calendar/events/${id}`);
      if (!res.ok) return;
      const e = await res.json();

      // Fill details
      const body = document.getElementById('eventDetailsBody');
      if (!body) return;

      const dt = e.isAllDay
        ? `${new Date(e.startLocal).toLocaleDateString()} — ${new Date(e.endLocal).toLocaleDateString()}`
        : `${new Date(e.startLocal).toLocaleString()} — ${new Date(e.endLocal).toLocaleString()}`;

      body.innerHTML =
        `<div class="mb-1"><span class="badge rounded-pill pm-cal-chip">${e.category}</span></div>
         <div class="fw-semibold">${e.title}</div>
         <div class="text-muted small">${dt}</div>
         ${e.location ? `<div class="small"><i class="bi bi-geo"></i> ${e.location}</div>` : ''}
         <hr class="my-2"/>
         <div class="small">${(e.description || '').replaceAll('<','&lt;')}</div>`;

      detailsCanvasEl.dataset.eventId = e.id;
      const label = document.getElementById('eventDetailsLabel');
      if (label) label.textContent = e.title;

      detailsCanvas && detailsCanvas.show();
    },
    eventDrop: async (info) => {
      await saveMoveResize(info);
    },
    eventResize: async (info) => {
      await saveMoveResize(info);
    }
  });

  calendar.render();

  async function saveMoveResize(info) {
    const canEdit = !!formCanvas;
    if (!canEdit) return;
    const id = info.event.id;
    const start = info.event.start;
    const end = info.event.end; // FullCalendar ensures exclusive end for all-day; for timed, this is end instant
    const allDay = info.event.allDay;

    const payload = {
      id,
      title: info.event.title,
      category: toCategoryEnum(info.event.extendedProps.category),
      location: info.event.extendedProps.location || null,
      isAllDay: allDay,
      startUtc: start ? new Date(start).toISOString() : null,
      endUtc: end ? new Date(end).toISOString() : null
    };
    const res = await fetch(`/calendar/events/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (!res.ok) {
      info.revert();
      alert('Failed to update event.');
    }
  }

  function toCategoryEnum(name) {
    // Server expects enum names; pass through string name
    return name || 'Other';
  }

  // UI wiring

  viewButtons.forEach(b => {
    b.addEventListener('click', () => {
      const v = b.getAttribute('data-view');
      calendar.changeView(v);
    }, { passive: true });
  });

  btnToday && btnToday.addEventListener('click', () => calendar.today(), { passive: true });

  if (catFilters) {
    catFilters.querySelectorAll('button').forEach(btn => {
      btn.addEventListener('click', () => {
        catFilters.querySelectorAll('button').forEach(x => x.classList.remove('active'));
        btn.classList.add('active');
        activeCategory = btn.getAttribute('data-cat') || "";
        // Toggle visibility for already-rendered events:
        calendar.getEvents().forEach(ev => {
          const el = ev.el;
          if (!el) return;
          const cat = ev.extendedProps.category;
          el.style.display = (!activeCategory || activeCategory === cat) ? '' : 'none';
        });
      }, { passive: true });
    });
  }

  // Details canvas buttons
  const btnAddToTasks = document.getElementById('btnAddToTasks');
  btnAddToTasks && btnAddToTasks.addEventListener('click', async () => {
    const id = detailsCanvasEl.dataset.eventId;
    if (!id) return;
    const r = await fetch(`/calendar/events/${id}/add-to-task`, { method: 'POST' });
    if (r.ok) {
      btnAddToTasks.disabled = true;
      btnAddToTasks.textContent = 'Added';
      setTimeout(() => { btnAddToTasks.disabled = false; btnAddToTasks.textContent = 'Add to My Tasks'; }, 2000);
    }
  });

  const btnDeleteEvent = document.getElementById('btnDeleteEvent');
  btnDeleteEvent && btnDeleteEvent.addEventListener('click', async () => {
    const id = detailsCanvasEl.dataset.eventId;
    if (!id) return;
    if (!confirm('Delete this event?')) return;
    const r = await fetch(`/calendar/events/${id}`, { method: 'DELETE' });
    if (r.ok) {
      detailsCanvas && detailsCanvas.hide();
      calendar.refetchEvents();
    }
  });

  const btnEditEvent = document.getElementById('btnEditEvent');
  const form = document.getElementById('eventForm');
  const toggleAllDay = document.getElementById('toggleAllDay');
  const timePickers = document.getElementById('timePickers');
  const datePickers = document.getElementById('datePickers');

  function setAllDayUI(isAllDay) {
    if (isAllDay) {
      timePickers.classList.add('d-none'); datePickers.classList.remove('d-none');
    } else {
      datePickers.classList.add('d-none'); timePickers.classList.remove('d-none');
    }
  }

  toggleAllDay && toggleAllDay.addEventListener('change', () => setAllDayUI(toggleAllDay.checked));

  document.getElementById('btnNewEvent') && document.getElementById('btnNewEvent').addEventListener('click', () => {
    form.reset();
    form.querySelector('[name="id"]').value = '';
    setAllDayUI(false);
    document.getElementById('eventFormLabel').textContent = 'New event';
  });

  btnEditEvent && btnEditEvent.addEventListener('click', async () => {
    const id = detailsCanvasEl.dataset.eventId;
    if (!id) return;
    const res = await fetch(`/calendar/events/${id}`);
    if (!res.ok) return;
    const e = await res.json();
    detailsCanvas && detailsCanvas.hide();

    form.querySelector('[name="id"]').value = e.id;
    form.querySelector('[name="title"]').value = e.title;
    form.querySelector('[name="category"]').value = e.category;
    form.querySelector('[name="location"]').value = e.location || '';
    form.querySelector('[name="description"]').value = e.description || '';
    const isAllDay = !!e.isAllDay;
    toggleAllDay.checked = isAllDay;
    setAllDayUI(isAllDay);
    if (isAllDay) {
      const s = new Date(e.startLocal); const ed = new Date(e.endLocal);
      form.querySelector('[name="startDate"]').value = s.toISOString().substring(0,10);
      // End is exclusive; show inclusive on UI by subtracting a day
      const inclEnd = new Date(ed); inclEnd.setDate(inclEnd.getDate()-1);
      form.querySelector('[name="endDate"]').value = inclEnd.toISOString().substring(0,10);
    } else {
      const s = new Date(e.startLocal); const ed = new Date(e.endLocal);
      form.querySelector('[name="start"]').value = toLocalInputValue(s);
      form.querySelector('[name="end"]').value = toLocalInputValue(ed);
    }
    document.getElementById('eventFormLabel').textContent = 'Edit event';
    formCanvas && formCanvas.show();
  });

  form && form.addEventListener('submit', async (ev) => {
    ev.preventDefault();
    const fd = new FormData(form);
    const isAllDay = !!fd.get('isAllDay');

    let startUtc, endUtc;
    if (isAllDay) {
      const startDate = fd.get('startDate');
      const endDate = fd.get('endDate'); // inclusive on UI
      if (!startDate || !endDate) { alert('Select start and end dates.'); return; }
      // Convert to UTC midnight range, end exclusive (+1 day)
      const s = new Date(`${startDate}T00:00:00`);
      const inclEnd = new Date(`${endDate}T00:00:00`);
      const exEnd = new Date(inclEnd); exEnd.setDate(exEnd.getDate()+1);
      startUtc = new Date(s.toISOString());
      endUtc = new Date(exEnd.toISOString());
    } else {
      const s = fd.get('start'); const ed = fd.get('end');
      if (!s || !ed) { alert('Select start and end time.'); return; }
      startUtc = new Date(s); endUtc = new Date(ed);
    }

    if (endUtc <= startUtc) { alert('End must be after start.'); return; }

    const payload = {
      title: (fd.get('title')||'').toString().trim(),
      description: (fd.get('description')||'').toString(),
      category: (fd.get('category')||'Other').toString(),
      location: (fd.get('location')||'').toString(),
      isAllDay: isAllDay,
      startUtc: startUtc.toISOString(),
      endUtc: endUtc.toISOString()
    };

    const id = fd.get('id');
    const method = id ? 'PUT' : 'POST';
    const url = id ? `/calendar/events/${id}` : '/calendar/events';

    const r = await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    if (!r.ok) { alert('Save failed.'); return; }
    formCanvas && formCanvas.hide();
    calendar.refetchEvents();
  });
})();


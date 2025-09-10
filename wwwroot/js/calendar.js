// No imports; FullCalendar globals are already on window from the script tags

(() => {
const el = document.getElementById('orgCalendar');
if (!el) return;

const canEdit = document.getElementById('btnNewEvent') !== null; // crude but fine
let currentEventId = null;
let currentEventData = null;
const detailsCanvasEl = document.getElementById('eventDetails');
const detailsCanvas = detailsCanvasEl && window.bootstrap
  ? new window.bootstrap.Offcanvas(detailsCanvasEl)
  : null;
const formCanvasEl = document.getElementById('eventFormCanvas');
const formCanvas = formCanvasEl && window.bootstrap
  ? new window.bootstrap.Offcanvas(formCanvasEl)
  : null;
const eventForm = document.getElementById('eventForm');
let editingId = null;
const calendar = new window.FullCalendar.Calendar(el, {
  timeZone: 'Asia/Kolkata',
  initialView: 'dayGridMonth',
  headerToolbar: false,     // we use our own header
  height: '100%',           // fill the wrapper
  firstDay: 1,              // Monday
  slotMinTime: '08:00:00',
  slotMaxTime: '20:00:00',
  expandRows: true,
  weekends: true,

  // plugins come from global builds under the FullCalendar namespace
  plugins: [
    window.FullCalendar.DayGrid,
    window.FullCalendar.TimeGrid,
    window.FullCalendar.List,
    window.FullCalendar.Interaction
  ],

  editable: canEdit,
  selectable: false,
  droppable: false,
  eventTimeFormat: { hour: '2-digit', minute: '2-digit', hour12: false },

  // Fetch events for the visible range
  events: async (info, success, failure) => {
    try {
      const url = `/calendar/events?start=${encodeURIComponent(info.startStr)}&end=${encodeURIComponent(info.endStr)}`;
      const res = await fetch(url, { headers: { 'Accept': 'application/json' } });
      const data = await res.json();
      success(data); // [{ id,title,start,end,allDay,category,location }]
    } catch (e) {
      console.error(e);
      failure(e);
    }
  },

  eventClick: async (info) => {
    try {
      const res = await fetch(`/calendar/events/${encodeURIComponent(info.event.id)}`);
      if (!res.ok) return;
      const data = await res.json();
      currentEventId = data.id;
      currentEventData = data;
      document.getElementById('eventDetailsLabel').textContent = data.title;
      const start = new Date(data.start);
      const end = new Date(data.end);
      const timeStr = data.allDay ?
        `${start.toLocaleDateString()}${start.toDateString() === end.toDateString() ? '' : ' - ' + new Date(end.getTime()-86400000).toLocaleDateString()}` :
        `${start.toLocaleString()} – ${end.toLocaleString()}`;
      document.getElementById('eventMeta').textContent = `${timeStr}${data.location ? ' · ' + data.location : ''}`;
      document.getElementById('eventDescription').innerHTML = data.descriptionHtml;
      if (canEdit) {
        document.getElementById('btnShowEdit').classList.remove('d-none');
        document.getElementById('btnDelete').classList.remove('d-none');
      }
      detailsCanvas.show();
    } catch (e) {
      console.error(e);
    }
  },

  // Editors: drag / resize to update
  eventDrop: async (info) => {
    if (!canEdit) return;
    await updateEventTime(info.event, info.revert);
  },
  eventResize: async (info) => {
    if (!canEdit) return;
    await updateEventTime(info.event, info.revert);
  },

  // Accessibility labels
  eventDidMount: (arg) => {
    const s = arg.event.allDay
      ? 'All day'
      : `${arg.event.start?.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false })}–${arg.event.end?.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false })}`;
    arg.el.setAttribute('aria-label', `${arg.event.title}, ${s}`);
    const colorMap = {
      Training: '#0d6efd',
      Holiday: '#198754',
      TownHall: '#6f42c1',
      Hiring: '#fd7e14',
      Other: '#adb5bd'
    };
    const cat = arg.event.extendedProps.category;
    if (colorMap[cat]) {
      arg.el.style.backgroundColor = colorMap[cat];
      arg.el.style.borderColor = colorMap[cat];
    }
  }
});

calendar.render();

// Header bindings
document.querySelectorAll('[data-cal-view]').forEach(btn => {
  btn.addEventListener('click', () => {
    calendar.changeView(btn.getAttribute('data-cal-view'));
    document.getElementById('calTitle').textContent = calendar.view.title;
  });
});
document.querySelector('[data-cal="today"]')?.addEventListener('click', () => {
  calendar.today();
  document.getElementById('calTitle').textContent = calendar.view.title;
});

// Set initial title
document.getElementById('calTitle').textContent = calendar.view.title;

// PUT update on drag/resize
async function updateEventTime(event, revert) {
  try {
    const payload = {
      startUtc: event.start?.toISOString(),
      endUtc: event.end?.toISOString(),
      isAllDay: event.allDay
    };
    const res = await fetch(`/calendar/events/${encodeURIComponent(event.id)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getAntiforgery() },
      body: JSON.stringify(payload)
    });
    if (!res.ok) throw new Error('Save failed');
  } catch (err) {
    console.error(err);
    if (typeof revert === 'function') revert();
  }
}

function getAntiforgery() {
  return document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
}

document.getElementById('btnAddTask')?.addEventListener('click', async () => {
  if (!currentEventId) return;
  try {
    await fetch(`/calendar/events/${currentEventId}/task`, {
      method: 'POST',
      headers: { 'RequestVerificationToken': getAntiforgery() }
    });
  } catch (e) { console.error(e); }
});

document.getElementById('btnShowEdit')?.addEventListener('click', () => {
  if (!currentEventData) return;
  detailsCanvas.hide();
  openForm(currentEventData);
});

document.getElementById('btnDelete')?.addEventListener('click', async () => {
  if (!currentEventId) return;
  try {
    const res = await fetch(`/calendar/events/${currentEventId}`, {
      method: 'DELETE',
      headers: { 'RequestVerificationToken': getAntiforgery() }
    });
    if (res.ok) { detailsCanvas.hide(); calendar.refetchEvents(); }
  } catch (e) { console.error(e); }
});

document.getElementById('btnNewEvent')?.addEventListener('click', () => {
  openForm();
});

function openForm(data) {
  editingId = data?.id ?? null;
  document.getElementById('eventFormLabel').textContent = editingId ? 'Edit Event' : 'New Event';
  eventForm.reset();
  document.getElementById('evtAllDay').checked = false;
  toggleAllDay(false);
  if (data) {
    document.getElementById('evtTitle').value = data.title;
    document.getElementById('evtCategory').value = data.category;
    document.getElementById('evtLocation').value = data.location ?? '';
    document.getElementById('evtDescription').value = data.description ?? '';
    document.getElementById('evtAllDay').checked = data.allDay;
    toggleAllDay(data.allDay);
    setDateInputs(data.start, data.end, data.allDay);
  }
  formCanvas.show();
}

function setDateInputs(start, end, allDay) {
  const s = new Date(start);
  const e = new Date(end);
  if (allDay) {
    const sLocal = s.toISOString().substring(0,10);
    const eLocal = new Date(e.getTime()-86400000).toISOString().substring(0,10);
    document.getElementById('evtStart').value = sLocal;
    document.getElementById('evtEnd').value = eLocal;
  } else {
    document.getElementById('evtStart').value = s.toISOString().slice(0,16);
    document.getElementById('evtEnd').value = e.toISOString().slice(0,16);
  }
}

document.getElementById('evtAllDay')?.addEventListener('change', e => {
  toggleAllDay(e.target.checked);
});

function toggleAllDay(allDay) {
  const start = document.getElementById('evtStart');
  const end = document.getElementById('evtEnd');
  start.type = allDay ? 'date' : 'datetime-local';
  end.type = allDay ? 'date' : 'datetime-local';
}

eventForm?.addEventListener('submit', async e => {
  e.preventDefault();
  const allDay = document.getElementById('evtAllDay').checked;
  let startVal = document.getElementById('evtStart').value;
  let endVal = document.getElementById('evtEnd').value;
  if (allDay) {
    const s = new Date(startVal);
    const e = new Date(endVal);
    s.setHours(0,0,0,0);
    e.setHours(0,0,0,0);
    startVal = s.toISOString();
    endVal = new Date(e.getTime() + 86400000).toISOString();
  } else {
    startVal = new Date(startVal).toISOString();
    endVal = new Date(endVal).toISOString();
  }
  const payload = {
    title: document.getElementById('evtTitle').value,
    category: document.getElementById('evtCategory').value,
    location: document.getElementById('evtLocation').value || null,
    description: document.getElementById('evtDescription').value || null,
    startUtc: startVal,
    endUtc: endVal,
    isAllDay: allDay
  };
  const url = editingId ? `/calendar/events/${editingId}` : '/calendar/events';
  const method = editingId ? 'PUT' : 'POST';
  try {
    const res = await fetch(url, {
      method,
      headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getAntiforgery() },
      body: JSON.stringify(payload)
    });
    if (!res.ok) throw new Error('Save failed');
    formCanvas.hide();
    calendar.refetchEvents();
  } catch (err) {
    console.error(err);
  }
});
})();

// No imports; FullCalendar globals are already on window from the script tags

const el = document.getElementById('orgCalendar');
if (!el) return;

const canEdit = document.getElementById('btnNewEvent') !== null; // crude but fine
const calendar = new FullCalendar.Calendar(el, {
  timeZone: 'Asia/Kolkata',
  initialView: 'dayGridMonth',
  headerToolbar: false,     // we use our own header
  height: '100%',           // fill the wrapper
  firstDay: 1,              // Monday
  slotMinTime: '08:00:00',
  slotMaxTime: '20:00:00',
  expandRows: true,
  weekends: true,

  // plugins come from global builds; just reference the names
  plugins: [ dayGridPlugin, timeGridPlugin, listPlugin, interactionPlugin ],

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
    // TODO: open offcanvas with /calendar/events/{id} details
    // For now, just log:
    console.log('event', info.event.id);
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
      start: event.start?.toISOString(),
      end: event.end?.toISOString(),
      allDay: event.allDay
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

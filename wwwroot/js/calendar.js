import { Calendar } from '../lib/fullcalendar/core/index.js';
import dayGridPlugin from '../lib/fullcalendar/daygrid/index.js';
import timeGridPlugin from '../lib/fullcalendar/timegrid/index.js';
import listPlugin from '../lib/fullcalendar/list/index.js';
import interactionPlugin from '../lib/fullcalendar/interaction/index.js';

const calendarEl = document.getElementById('calendar');
if (calendarEl) {
  const token = document.querySelector('#calendar-token input[name="__RequestVerificationToken"]').value;
  const editable = calendarEl.dataset.editable === 'true';
  const colors = {
    Training: '#0d6efd',
    Holiday: '#198754',
    TownHall: '#6f42c1',
    Hiring: '#d63384',
    Other: '#6c757d'
  };
  const calendar = new Calendar(calendarEl, {
    initialView: 'dayGridMonth',
    locale: 'en',
    height: 'auto',
    plugins: [dayGridPlugin, timeGridPlugin, listPlugin, interactionPlugin],
    headerToolbar: {
      left: 'prev,next today',
      center: 'title',
      right: 'dayGridMonth,timeGridWeek,listMonth'
    },
    editable,
    eventSources: [{
      events: (info, success, failure) => {
        fetch(`/calendar/events?start=${info.startStr}&end=${info.endStr}`, {credentials:'same-origin'})
          .then(r => r.ok ? r.json() : [])
          .then(evts => success(evts))
          .catch(() => failure());
      }
    }],
    eventClick: info => showDetails(info.event),
    eventDrop: info => updateTimes(info.event, info.oldEvent),
    eventResize: info => updateTimes(info.event, info.oldEvent),
    eventDidMount: info => {
      const cat = info.event.extendedProps.category;
      if (colors[cat]) {
        info.el.style.setProperty('--fc-event-bg-color', colors[cat]);
        info.el.style.setProperty('--fc-event-border-color', colors[cat]);
      }
    }
  });
  calendar.render();

  // Offcanvas setup
  const detailCanvas = document.getElementById('event-details');
  const detailOffcanvas = new bootstrap.Offcanvas(detailCanvas);
  const formCanvas = document.getElementById('event-form');
  const formOffcanvas = new bootstrap.Offcanvas(formCanvas);
  const formEl = document.getElementById('event-form-element');
  let editingId = null;

  document.getElementById('btn-new')?.addEventListener('click', () => {
    openForm();
  });

  document.getElementById('add-task-btn').addEventListener('click', () => {
    if (!editingId) return;
    fetch(`/calendar/events/${editingId}/task`, {method:'POST', headers:{'RequestVerificationToken':token}})
      .then(() => showToast('Task created'));
  });

  document.getElementById('edit-btn')?.addEventListener('click', () => {
    if (!currentEvent) return;
    openForm(currentEvent);
  });

  document.getElementById('delete-btn')?.addEventListener('click', () => {
    if (!currentEvent) return;
    if (!confirm('Delete this event?')) return;
    fetch(`/calendar/events/${currentEvent.id}`, {method:'DELETE', headers:{'RequestVerificationToken':token}})
      .then(r => { if (r.ok) { calendar.refetchEvents(); detailOffcanvas.hide(); } });
  });

  formEl.addEventListener('submit', e => {
    e.preventDefault();
    const data = Object.fromEntries(new FormData(formEl).entries());
    const body = {
      title: data.title,
      description: data.description,
      category: data.category,
      location: data.location,
      startUtc: data.start ? new Date(data.start).toISOString() : null,
      endUtc: data.end ? new Date(data.end).toISOString() : null,
      isAllDay: data.isAllDay === 'on'
    };
    const method = editingId ? 'PUT' : 'POST';
    const url = editingId ? `/calendar/events/${editingId}` : '/calendar/events';
    fetch(url, {
      method,
      headers: {'Content-Type':'application/json','RequestVerificationToken':token},
      body: JSON.stringify(body)
    }).then(r => {
      if (r.ok) {
        calendar.refetchEvents();
        formOffcanvas.hide();
        showToast('Saved');
      }
    });
  });

  let currentEvent = null;

  function showDetails(ev){
    currentEvent = ev;
    editingId = ev.id;
    fetch(`/calendar/events/${ev.id}`, {credentials:'same-origin'})
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if(!data) return;
        ev.setExtendedProp('description', data.description);
        document.getElementById('detail-title').textContent = data.title;
        const info = document.getElementById('detail-info');
        const start = new Date(data.start);
        const end = new Date(data.end);
        const timeStr = data.allDay ? `${start.toLocaleDateString()} - ${new Date(end - 1).toLocaleDateString()}` : `${start.toLocaleString()} - ${end.toLocaleString()}`;
        info.innerHTML = `<div class="mb-2"><strong>${timeStr}</strong></div>`+
                         (data.location?`<div class="mb-2">${data.location}</div>`:'')+
                         (data.descriptionHtml||'');
        detailOffcanvas.show();
      });
  }

  function openForm(ev){
    editingId = ev ? ev.id : null;
    formEl.reset();
    document.getElementById('form-title').textContent = ev ? 'Edit event' : 'New event';
    if(ev){
      formEl.title.value = ev.title;
      formEl.category.value = ev.extendedProps.category;
      formEl.location.value = ev.extendedProps.location || '';
      formEl.isAllDay.checked = ev.allDay;
      formEl.start.value = ev.startStr.slice(0,16);
      formEl.end.value = ev.endStr.slice(0,16);
      formEl.description.value = ev.extendedProps.description || '';
    }
    formOffcanvas.show();
  }

  function updateTimes(ev, oldEv){
    editingId = ev.id;
    const body = {
      title: ev.title,
      description: ev.extendedProps.description,
      category: ev.extendedProps.category,
      location: ev.extendedProps.location,
      startUtc: ev.start.toISOString(),
      endUtc: ev.end ? ev.end.toISOString() : ev.start.toISOString(),
      isAllDay: ev.allDay
    };
    fetch(`/calendar/events/${ev.id}`, {
      method:'PUT',
      headers:{'Content-Type':'application/json','RequestVerificationToken':token},
      body: JSON.stringify(body)
    }).then(r => {
      if(r.ok) {
        const prevStart = oldEv.start.toISOString();
        const prevEnd = oldEv.end ? oldEv.end.toISOString() : prevStart;
        showToast('Saved', () => {
          ev.setStart(prevStart);
          ev.setEnd(prevEnd);
          const undoBody = { ...body, startUtc: prevStart, endUtc: prevEnd };
          fetch(`/calendar/events/${ev.id}`, {
            method:'PUT',
            headers:{'Content-Type':'application/json','RequestVerificationToken':token},
            body: JSON.stringify(undoBody)
          });
        });
      } else {
        ev.revert();
      }
    });
  }

  function showToast(msg, undo){
    const toastEl = document.getElementById('calendar-toast');
    const body = toastEl.querySelector('.toast-body');
    body.innerHTML = '';
    const span = document.createElement('span');
    span.textContent = msg;
    body.appendChild(span);
    const toast = new bootstrap.Toast(toastEl);
    if(undo){
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'btn btn-link btn-sm ms-2';
      btn.textContent = 'Undo';
      btn.addEventListener('click', () => { undo(); toast.hide(); });
      body.appendChild(btn);
    }
    toast.show();
  }
}

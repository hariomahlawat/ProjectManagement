import { Calendar } from '../lib/fullcalendar/core/index.global.min.js';
import dayGridPlugin from '../lib/fullcalendar/daygrid/index.global.min.js';
import timeGridPlugin from '../lib/fullcalendar/timegrid/index.global.min.js';
import listPlugin from '../lib/fullcalendar/list/index.global.min.js';
import interactionPlugin from '../lib/fullcalendar/interaction/index.global.min.js';

const calendarEl = document.getElementById('calendar');
const canEdit = calendarEl.dataset.canEdit === 'True';

const calendar = new Calendar(calendarEl, {
    plugins: [dayGridPlugin, timeGridPlugin, listPlugin, interactionPlugin],
    initialView: 'dayGridMonth',
    editable: canEdit,
    eventSources: [{
        url: '/calendar/events',
        method: 'GET',
        extraParams: function() {
            return {};
        }
    }],
    headerToolbar: false,
    eventClick: async function(info) {
        const id = info.event.id;
        const resp = await fetch(`/calendar/events/${id}`);
        if (!resp.ok) return;
        const data = await resp.json();
        document.getElementById('detailTitle').textContent = data.title;
        const time = data.allDay ? `${new Date(data.start).toLocaleDateString()} - ${new Date(data.end).toLocaleDateString()}`
            : `${new Date(data.start).toLocaleString()} - ${new Date(data.end).toLocaleString()}`;
        document.getElementById('detailTime').textContent = time;
        document.getElementById('detailLocation').textContent = data.location || '';
        document.getElementById('detailDescription').innerHTML = data.description || '';
        document.getElementById('addTaskBtn').onclick = async () => {
            await fetch(`/calendar/events/${id}/task`, { method: 'POST' });
        };
        if (canEdit) {
            document.getElementById('editBtn').onclick = () => openForm(data);
            document.getElementById('deleteBtn').onclick = async () => {
                if (confirm('Delete event?')) {
                    await fetch(`/calendar/events/${id}`, { method: 'DELETE' });
                    calendar.refetchEvents();
                }
            };
        }
        new bootstrap.Offcanvas('#eventDetails').show();
    },
    eventDrop: async function(info) {
        await saveMove(info.event);
    },
    eventResize: async function(info) {
        await saveMove(info.event);
    }
});
calendar.render();

function viewButtons() {
    document.querySelectorAll('[data-view]').forEach(btn => {
        btn.addEventListener('click', () => {
            calendar.changeView(btn.dataset.view);
        });
    });
    document.getElementById('todayBtn').addEventListener('click', () => calendar.today());
}
viewButtons();

if (canEdit) {
    document.getElementById('newEventBtn').addEventListener('click', () => openForm());
}

function openForm(data) {
    const form = document.getElementById('eventFormElement');
    form.reset();
    if (data) {
        form.elements['title'].value = data.title;
        form.elements['category'].value = data.category;
        form.elements['location'].value = data.location || '';
        form.elements['isAllDay'].checked = data.allDay;
        form.elements['start'].value = data.start.substring(0,16);
        form.elements['end'].value = data.end.substring(0,16);
        form.elements['description'].value = data.rawDescription || '';
        form.dataset.id = data.id;
        document.getElementById('formHeading').textContent = 'Edit Event';
    } else {
        delete form.dataset.id;
        document.getElementById('formHeading').textContent = 'New Event';
    }
    new bootstrap.Offcanvas('#eventForm').show();
}

document.getElementById('eventFormElement').addEventListener('submit', async e => {
    e.preventDefault();
    const form = e.target;
    const dto = {
        title: form.elements['title'].value,
        category: form.elements['category'].value,
        location: form.elements['location'].value,
        isAllDay: form.elements['isAllDay'].checked,
        startUtc: form.elements['start'].value,
        endUtc: form.elements['end'].value,
        description: form.elements['description'].value
    };
    const id = form.dataset.id;
    const method = id ? 'PUT' : 'POST';
    const url = id ? `/calendar/events/${id}` : '/calendar/events';
    const resp = await fetch(url, {
        method: method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(dto)
    });
    if (resp.ok) {
        new bootstrap.Offcanvas('#eventForm').hide();
        calendar.refetchEvents();
    }
});

async function saveMove(event) {
    const dto = {
        title: event.title,
        category: event.extendedProps.category,
        location: event.extendedProps.location,
        isAllDay: event.allDay,
        startUtc: event.start.toISOString(),
        endUtc: event.end.toISOString(),
        description: event.extendedProps.description
    };
    await fetch(`/calendar/events/${event.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(dto)
    });
    calendar.refetchEvents();
}

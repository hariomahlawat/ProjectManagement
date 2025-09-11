const Calendar = window.FullCalendar?.Calendar;
const dayGridPlugin = window.FullCalendar?.dayGrid || window.FullCalendarDayGrid;
const timeGridPlugin = window.FullCalendar?.timeGrid || window.FullCalendarTimeGrid;
const listPlugin = window.FullCalendar?.list || window.FullCalendarList;
const interactionPlugin = window.FullCalendar?.interaction || window.FullCalendarInteraction;

if (!Calendar) {
    console.error('FullCalendar globals not found. Check script order.');
}

function pad(n) { return String(n).padStart(2,'0'); }
function toLocalInputValue(d) {
    const dt = new Date(d);
    return `${dt.getFullYear()}-${pad(dt.getMonth()+1)}-${pad(dt.getDate())}T${pad(dt.getHours())}:${pad(dt.getMinutes())}`;
}

const calendarEl = document.getElementById('calendar');
const canEdit = (calendarEl.dataset.canEdit || '').toLowerCase() === 'true';

const calendar = new Calendar(calendarEl, {
    plugins: [dayGridPlugin, timeGridPlugin, listPlugin, interactionPlugin],
    initialView: 'dayGridMonth',
    slotMinTime: '08:00:00',
    slotMaxTime: '18:00:00',
    scrollTime: '08:00:00',
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
        await saveMove(info.event, info.oldEvent);
    },
    eventResize: async function(info) {
        await saveMove(info.event, info.oldEvent);
    },
    eventDidMount: function(info) {
        const cat = (info.event.extendedProps.category || '').toString().toLowerCase();
        info.el.classList.add('pm-cat-' + cat);
        if (info.event.extendedProps.location) {
            info.el.setAttribute('title', `${info.event.title} — ${info.event.extendedProps.location}`);
        } else {
            info.el.setAttribute('title', info.event.title);
        }
        info.el.setAttribute('aria-label', info.event.title);
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

function toggleAllDayInputs(isAllDay) {
    const dtFields = document.querySelectorAll('.dt-field');
    const dFields = document.querySelectorAll('.d-field');
    dtFields.forEach(f => f.classList.toggle('d-none', isAllDay));
    dtFields.forEach(f => f.querySelector('input').disabled = isAllDay);
    dFields.forEach(f => f.classList.toggle('d-none', !isAllDay));
    dFields.forEach(f => f.querySelector('input').disabled = !isAllDay);
}

function openForm(data) {
    const form = document.getElementById('eventFormElement');
    form.reset();
    toggleAllDayInputs(false);
    if (data) {
        form.elements['title'].value = data.title;
        form.elements['category'].value = data.category;
        form.elements['location'].value = data.location || '';
        form.elements['isAllDay'].checked = data.allDay;
        if (data.allDay) {
            form.elements['startDate'].value = toLocalInputValue(data.start).substring(0,10);
            form.elements['endDate'].value = toLocalInputValue(new Date(new Date(data.end).getTime()-86400000)).substring(0,10);
            toggleAllDayInputs(true);
        } else {
            form.elements['start'].value = toLocalInputValue(data.start);
            form.elements['end'].value = toLocalInputValue(data.end);
        }
        form.elements['description'].value = data.rawDescription || '';
        form.dataset.id = data.id;
        document.getElementById('formHeading').textContent = 'Edit Event';
    } else {
        delete form.dataset.id;
        document.getElementById('formHeading').textContent = 'New Event';
    }
    lastFormOldTimes = data ? { id: data.id, startUtc: data.start, endUtc: data.end } : null;
    new bootstrap.Offcanvas('#eventForm').show();
}

document.getElementById('allDaySwitch').addEventListener('change', e => {
    toggleAllDayInputs(e.target.checked);
});

document.getElementById('eventFormElement').addEventListener('submit', async e => {
    e.preventDefault();
    const form = e.target;
    const isAllDay = form.elements['isAllDay'].checked;
    let startUtc, endUtc;
    if (isAllDay) {
        const startDate = form.elements['startDate'].value;
        const endDate = form.elements['endDate'].value;
        const s = new Date(startDate + 'T00:00');
        const e = new Date(endDate + 'T00:00');
        e.setDate(e.getDate() + 1);
        startUtc = s.toISOString();
        endUtc = e.toISOString();
    } else {
        const startLocal = form.elements['start'].value;
        const endLocal = form.elements['end'].value;
        startUtc = new Date(startLocal).toISOString();
        endUtc = new Date(endLocal).toISOString();
    }

    if (new Date(endUtc) <= new Date(startUtc)) { alert('End must be after start.'); return; }

    const dto = {
        title: form.elements['title'].value.trim(),
        category: form.elements['category'].value,
        location: form.elements['location'].value || null,
        isAllDay,
        startUtc,
        endUtc,
        description: form.elements['description'].value ?? ''
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
        if (lastFormOldTimes) {
            showUndoToast('Saved', async () => {
                await fetch(`/calendar/events/${lastFormOldTimes.id}`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        title: dto.title,
                        category: dto.category,
                        location: dto.location,
                        isAllDay: dto.isAllDay,
                        startUtc: lastFormOldTimes.startUtc,
                        endUtc: lastFormOldTimes.endUtc,
                        description: dto.description
                    })
                });
                calendar.refetchEvents();
            });
        } else {
            showUndoToast('Saved');
        }
        lastFormOldTimes = null;
    }
});

async function saveMove(event, oldEvent) {
    const dto = {
        title: event.title,
        category: event.extendedProps.category,
        location: event.extendedProps.location,
        isAllDay: event.allDay,
        startUtc: event.start.toISOString(),
        endUtc: event.end.toISOString()
    };
    await fetch(`/calendar/events/${event.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(dto)
    });
    calendar.refetchEvents();
    if (oldEvent) {
        const prev = {
            title: event.title,
            category: event.extendedProps.category,
            location: event.extendedProps.location,
            isAllDay: oldEvent.allDay,
            startUtc: oldEvent.start.toISOString(),
            endUtc: oldEvent.end.toISOString(),
            description: event.extendedProps.description || ''
        };
        showUndoToast('Saved', async () => {
            await fetch(`/calendar/events/${event.id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(prev)
            });
            calendar.refetchEvents();
        });
    } else {
        showUndoToast('Saved');
    }
}

function showUndoToast(message, undo) {
    const container = document.getElementById('toastContainer');
    const toastEl = document.createElement('div');
    toastEl.className = 'toast';
    toastEl.innerHTML = `<div class="toast-body d-flex justify-content-between align-items-center">${message}` +
        (undo ? `<button type="button" class="btn btn-link btn-sm ms-2">Undo</button>` : '') +
        `</div>`;
    container.appendChild(toastEl);
    const toast = new bootstrap.Toast(toastEl, { delay: 5000 });
    if (undo) {
        toastEl.querySelector('button').addEventListener('click', async () => {
            await undo();
            toast.hide();
        });
    }
    toast.show();
    toastEl.addEventListener('hidden.bs.toast', () => toastEl.remove());
}

function categoryFilters() {
    document.querySelectorAll('#catFilters [data-cat]').forEach(btn => {
        btn.addEventListener('click', () => {
            document.querySelectorAll('#catFilters [data-cat]').forEach(b => {
                b.classList.remove('active');
                b.setAttribute('aria-pressed', 'false');
            });
            btn.classList.add('active');
            btn.setAttribute('aria-pressed', 'true');
            const cat = btn.dataset.cat;
            calendar.getEvents().forEach(ev => {
                const match = !cat || ev.extendedProps.category === cat;
                ev.setProp('display', match ? 'auto' : 'none');
            });
        });
    });
}
categoryFilters();

let lastFormOldTimes = null;

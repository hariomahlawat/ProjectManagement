const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

const scriptPath = path.resolve(__dirname, 'calendar.js');
const scriptContent = fs.readFileSync(scriptPath, 'utf8');

class FakeCalendar {
    constructor(el, opts = {}) {
        this.el = el;
        this.opts = opts;
        this.view = { title: '', type: 'dayGridMonth' };
        this._handlers = new Map();
        this._events = [];
        this._sources = [];
        FakeCalendar.lastInstance = this;
    }

    on(name, handler) {
        if (!this._handlers.has(name)) {
            this._handlers.set(name, []);
        }
        this._handlers.get(name).push(handler);
    }

    addEventSource(config) {
        this._sources.push(config);
        return { remove() {}, refetch() {} };
    }

    getEvents() {
        return this._events;
    }

    setOption(name, value) {
        this.opts[name] = value;
    }

    refetchEvents() {}
    prev() {}
    next() {}
    today() {}

    changeView(view) {
        this.view.type = view;
        this._emit('datesSet', {
            startStr: '2024-12-01T00:00:00.000Z',
            endStr: '2024-12-31T23:59:59.999Z',
            view: this.view
        });
    }

    render() {
        this.view = {
            title: 'December 2024',
            type: 'dayGridMonth',
            currentStart: new Date('2024-12-01T00:00:00Z'),
            currentEnd: new Date('2024-12-31T23:59:59Z')
        };
        const info = {
            startStr: '2024-12-01T00:00:00.000Z',
            endStr: '2024-12-31T23:59:59.999Z',
            view: this.view
        };
        this._emit('datesSet', info);
        this._emit('eventsSet', this._events);
    }

    _emit(name, payload) {
        const handlers = this._handlers.get(name) || [];
        for (const handler of handlers) {
            try {
                const result = handler(payload);
                if (result && typeof result.then === 'function') {
                    result.catch(() => {});
                }
            } catch (err) {
                console.error('FakeCalendar handler error', err);
            }
        }
        const optionHandler = this.opts?.[name];
        if (typeof optionHandler === 'function') {
            try {
                const result = optionHandler(payload);
                if (result && typeof result.then === 'function') {
                    result.catch(() => {});
                }
            } catch (err) {
                console.error('FakeCalendar option error', err);
            }
        }
    }
}

test('calendar uses compact month rows and official 24-hour time labels', async () => {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <div id="calendar" data-show-celebrations="false"></div>
    </body></html>`, { url: 'https://example.test/Calendar', runScripts: 'dangerously' });

    const { window } = dom;
    window.alert = () => {};
    window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
    window.FullCalendar = {
        Calendar: FakeCalendar,
        dayGrid: () => ({}),
        timeGrid: () => ({}),
        list: () => ({}),
        interaction: () => ({})
    };
    window.fetch = async () => ({ ok: true, status: 200, json: async () => [] });

    const scriptEl = window.document.createElement('script');
    scriptEl.textContent = scriptContent;
    window.document.body.appendChild(scriptEl);
    await new Promise(resolve => setTimeout(resolve, 25));

    const options = FakeCalendar.lastInstance.opts;
    assert.equal(options.fixedWeekCount, false);
    assert.equal(options.eventTimeFormat.hour, '2-digit');
    assert.equal(options.eventTimeFormat.minute, '2-digit');
    assert.equal(options.eventTimeFormat.hour12, false);
    assert.equal(options.slotLabelFormat.hour, '2-digit');
    assert.equal(options.slotLabelFormat.minute, '2-digit');
    assert.equal(options.slotLabelFormat.hour12, false);
});

test('calendar highlights admin holidays during initial load', async () => {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <div id="calendar" data-show-celebrations="false">
            <div class="fc-daygrid-day" data-date="2024-12-25">
                <div class="fc-daygrid-day-number" title="25"></div>
            </div>
            <div class="fc-daygrid-day" data-date="2024-12-26">
                <div class="fc-daygrid-day-number" title="26"></div>
            </div>
            <div class="fc-col-header-cell" data-date="2024-12-25">
                <div class="fc-col-header-cell-cushion"></div>
            </div>
            <div class="fc-timegrid-col" data-date="2024-12-25">
                <div class="fc-timegrid-col-frame"></div>
            </div>
            <div class="fc-list-day" data-date="2024-12-25">
                <div class="fc-list-day-cushion">
                    <div class="fc-list-day-text"></div>
                </div>
            </div>
        </div>
    </body></html>`, { url: 'https://example.test/', runScripts: 'dangerously' });

    const { window } = dom;
    window.alert = () => {};
    window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
    window.FullCalendar = {
        Calendar: FakeCalendar,
        dayGrid: () => ({}),
        timeGrid: () => ({}),
        list: () => ({}),
        interaction: () => ({})
    };

    const requests = [];
    window.fetch = async (url) => {
        requests.push(url.toString());
        return {
            ok: true,
            status: 200,
            json: async () => ([{
                date: '2024-12-25',
                isOfficeClosed: true,
                closureType: 'Gazetted',
                entries: [{
                    id: 1,
                    name: 'Founders Day',
                    type: 'Gazetted',
                    isObservedAsOfficeHoliday: true,
                    affectsSchedule: true
                }]
            }])
        };
    };

    const scriptEl = window.document.createElement('script');
    scriptEl.textContent = scriptContent;
    window.document.body.appendChild(scriptEl);

    await new Promise(resolve => setTimeout(resolve, 25));

    assert.ok(requests.length > 0, 'holiday endpoint should be queried');
    const calendarEl = window.document.getElementById('calendar');
    const holidayCell = calendarEl.querySelector('.fc-daygrid-day[data-date="2024-12-25"]');
    assert.ok(holidayCell.classList.contains('pm-holiday'));
    assert.ok(holidayCell.classList.contains('pm-holiday--gazetted'));
    const holidayBadge = holidayCell.querySelector('.pm-holiday-badge');
    assert.ok(holidayBadge, 'holiday cell should receive badge');
    assert.equal(holidayBadge.querySelector('.pm-holiday-badge__primary')?.textContent, 'Gazetted holiday');
    assert.equal(holidayBadge.querySelector('.pm-holiday-badge__secondary')?.textContent, 'Founders Day');

    const nonHolidayCell = calendarEl.querySelector('.fc-daygrid-day[data-date="2024-12-26"]');
    assert.ok(!nonHolidayCell.classList.contains('pm-holiday'));
    assert.equal(nonHolidayCell.querySelector('.pm-holiday-badge'), null);

    const numberEl = holidayCell.querySelector('.fc-daygrid-day-number');
    assert.ok((numberEl.getAttribute('title') || '').includes('Gazetted Holiday: Founders Day'));
    assert.ok((numberEl.getAttribute('aria-label') || '').includes('Office closed'));
    assert.ok((numberEl.getAttribute('aria-label') || '').includes('Affects project schedules'));

    const headerCell = calendarEl.querySelector('.fc-col-header-cell');
    assert.ok(headerCell.classList.contains('pm-holiday'));

    const timeGridFrame = calendarEl.querySelector('.fc-timegrid-col-frame');
    assert.ok(timeGridFrame.classList.contains('pm-holiday'));
});


test('informational RH remains subtle and is always visible', async () => {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <div id="calendar" data-show-celebrations="false">
            <div class="fc-daygrid-day" data-date="2024-12-26">
                <div class="fc-daygrid-day-top"><div class="fc-daygrid-day-number" title="26"></div></div>
            </div>
        </div>
    </body></html>`, { url: 'https://example.test/', runScripts: 'dangerously' });

    const { window } = dom;
    window.alert = () => {};
    window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
    window.FullCalendar = {
        Calendar: FakeCalendar,
        dayGrid: () => ({}),
        timeGrid: () => ({}),
        list: () => ({}),
        interaction: () => ({})
    };
    window.fetch = async () => ({
        ok: true,
        status: 200,
        json: async () => ([{
            date: '2024-12-26',
            isOfficeClosed: false,
            closureType: null,
            entries: [{
                id: 2,
                name: 'Optional Day',
                type: 'Restricted',
                isObservedAsOfficeHoliday: false,
                affectsSchedule: false
            }]
        }])
    });

    const scriptEl = window.document.createElement('script');
    scriptEl.textContent = scriptContent;
    window.document.body.appendChild(scriptEl);
    await new Promise(resolve => setTimeout(resolve, 25));

    const cell = window.document.querySelector('.fc-daygrid-day[data-date="2024-12-26"]');
    assert.ok(cell.classList.contains('pm-holiday--rh-info'));
    assert.equal(
        cell.querySelector('.pm-holiday-badge__primary')?.textContent,
        'RH · Optional Day');
    const number = cell.querySelector('.fc-daygrid-day-number');
    assert.ok((number.getAttribute('title') || '').includes('Office open'));
    assert.ok((number.getAttribute('title') || '').includes('No effect on project schedules'));
    assert.equal(window.document.getElementById('showInformationalRhToggle'), null);
});

test('calendar celebration source uses the supported events endpoint and filters celebrations', async () => {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <form id="calendarPreferences" data-preference-endpoint="/calendar/events/preferences/show-celebrations">
            <input name="__RequestVerificationToken" value="token" />
            <input id="showCelebrationsToggle" type="checkbox" checked />
        </form>
        <div id="calendar" data-show-celebrations="true"></div>
    </body></html>`, { url: 'https://example.test/Calendar', runScripts: 'dangerously' });

    const { window } = dom;
    window.alert = () => {};
    window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
    window.FullCalendar = {
        Calendar: FakeCalendar,
        dayGrid: () => ({}),
        timeGrid: () => ({}),
        list: () => ({}),
        interaction: () => ({})
    };

    const requests = [];
    window.fetch = async (url) => {
        const requestUrl = url.toString();
        requests.push(requestUrl);
        if (requestUrl.includes('/calendar/events/holidays')) {
            return { ok: true, status: 200, json: async () => [] };
        }
        return {
            ok: true,
            status: 200,
            json: async () => ([
                { id: 'regular', title: 'Meeting', isCelebration: false },
                { id: 'birthday', title: 'Birthday: Hariom Ahlawat', isCelebration: true }
            ])
        };
    };

    const scriptEl = window.document.createElement('script');
    scriptEl.textContent = scriptContent;
    window.document.body.appendChild(scriptEl);
    await new Promise(resolve => setTimeout(resolve, 10));

    const source = FakeCalendar.lastInstance._sources.find(item => item.id === 'celebrations');
    assert.ok(source, 'celebration source should be registered');

    const events = await new Promise((resolve, reject) => {
        source.events({
            startStr: '2026-09-01T00:00:00.000Z',
            endStr: '2026-10-01T00:00:00.000Z'
        }, resolve, reject);
    });

    assert.equal(events.length, 1);
    assert.equal(events[0].id, 'birthday');
    const celebrationRequest = requests.find(url => url.includes('includeCelebrations=true'));
    assert.ok(celebrationRequest, 'supported combined events endpoint should be used');
    assert.ok(!requests.some(url => url.includes('/calendar/events/celebrations')),
        'missing celebrations endpoint must not be probed');
});

test('ordinary event renderer preserves time, title and recurrence content', async () => {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <div id="calendar" data-show-celebrations="false"></div>
    </body></html>`, { url: 'https://example.test/Calendar', runScripts: 'dangerously' });

    const { window } = dom;
    window.alert = () => {};
    window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
    window.fetch = async () => ({ ok: true, status: 200, json: async () => [] });
    window.FullCalendar = {
        Calendar: FakeCalendar,
        dayGrid: () => ({}),
        timeGrid: () => ({}),
        list: () => ({}),
        interaction: () => ({})
    };

    const scriptEl = window.document.createElement('script');
    scriptEl.textContent = scriptContent;
    window.document.body.appendChild(scriptEl);
    await new Promise(resolve => setTimeout(resolve, 10));

    const props = {
        category: 'Conference',
        isCelebration: false,
        isRecurring: true,
        location: 'Conference Hall'
    };
    const event = {
        title: 'Project review conference',
        extendedProps: props,
        setExtendedProp(name, value) { this.extendedProps[name] = value; }
    };

    const rendered = FakeCalendar.lastInstance.opts.eventContent({
        event,
        timeText: '09:30',
        view: { type: 'dayGridMonth' }
    });

    assert.ok(rendered, 'ordinary events must always return visible content');
    assert.equal(rendered.domNodes.length, 1);
    const content = rendered.domNodes[0];
    assert.equal(content.querySelector('.pm-calendar-event__time').textContent, '09:30');
    assert.equal(content.querySelector('.pm-calendar-event__title').textContent, 'Project review conference');
    assert.ok(content.querySelector('.pm-calendar-event__recurrence .bi-arrow-repeat'));

    const eventEl = window.document.createElement('a');
    eventEl.className = 'fc-event fc-daygrid-event';
    eventEl.appendChild(content);
    FakeCalendar.lastInstance.opts.eventDidMount({ event, el: eventEl, timeText: '09:30' });

    assert.ok(eventEl.classList.contains('pm-standard-event'));
    assert.ok(eventEl.classList.contains('pm-cat-conference'));
    assert.ok(eventEl.classList.contains('pm-recurring'));
    assert.equal(
        eventEl.getAttribute('aria-label'),
        '09:30 — Project review conference — Conference Hall — Recurring event');
});

test('list renderer avoids duplicate time and gives untitled legacy events a visible fallback', async () => {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <div id="calendar" data-show-celebrations="false"></div>
    </body></html>`, { url: 'https://example.test/Calendar', runScripts: 'dangerously' });

    const { window } = dom;
    window.alert = () => {};
    window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
    window.fetch = async () => ({ ok: true, status: 200, json: async () => [] });
    window.FullCalendar = {
        Calendar: FakeCalendar,
        dayGrid: () => ({}),
        timeGrid: () => ({}),
        list: () => ({}),
        interaction: () => ({})
    };

    const scriptEl = window.document.createElement('script');
    scriptEl.textContent = scriptContent;
    window.document.body.appendChild(scriptEl);
    await new Promise(resolve => setTimeout(resolve, 10));

    const props = {
        category: 'Other',
        isCelebration: false,
        isRecurring: false,
        location: null
    };
    const event = {
        title: '   ',
        extendedProps: props,
        setExtendedProp(name, value) { this.extendedProps[name] = value; }
    };

    const rendered = FakeCalendar.lastInstance.opts.eventContent({
        event,
        timeText: '09:30',
        view: { type: 'listWeek' }
    });
    const content = rendered.domNodes[0];

    assert.equal(content.querySelector('.pm-calendar-event__time'), null,
        'list view already owns a dedicated time column');
    assert.equal(content.querySelector('.pm-calendar-event__title').textContent, 'Untitled event');

    const eventEl = window.document.createElement('tr');
    eventEl.className = 'fc-list-event';
    eventEl.appendChild(content);
    FakeCalendar.lastInstance.opts.eventDidMount({ event, el: eventEl, timeText: '09:30' });

    assert.ok(eventEl.classList.contains('pm-standard-event'));
    assert.ok(eventEl.classList.contains('pm-event--untitled'));
    assert.equal(eventEl.getAttribute('aria-label'), '09:30 — Untitled event');
});

test('birthday event uses a single compact custom content node without duplicating its title', async () => {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <div id="calendar" data-show-celebrations="false"></div>
    </body></html>`, { url: 'https://example.test/Calendar', runScripts: 'dangerously' });

    const { window } = dom;
    window.alert = () => {};
    window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
    window.fetch = async () => ({ ok: true, status: 200, json: async () => [] });
    window.FullCalendar = {
        Calendar: FakeCalendar,
        dayGrid: () => ({}),
        timeGrid: () => ({}),
        list: () => ({}),
        interaction: () => ({})
    };

    const scriptEl = window.document.createElement('script');
    scriptEl.textContent = scriptContent;
    window.document.body.appendChild(scriptEl);
    await new Promise(resolve => setTimeout(resolve, 10));

    const props = {
        category: 'Celebration',
        isCelebration: true,
        isRecurring: true,
        location: null
    };
    const event = {
        title: 'Birthday: Hariom Ahlawat',
        extendedProps: props,
        setExtendedProp(name, value) { this.extendedProps[name] = value; }
    };

    const rendered = FakeCalendar.lastInstance.opts.eventContent({ event });
    assert.ok(rendered);
    assert.equal(rendered.domNodes.length, 1);
    const content = rendered.domNodes[0];
    assert.equal(content.querySelector('.pm-celebration-event__name').textContent, 'Hariom Ahlawat');
    assert.ok(content.querySelector('.pm-celebration-event__icon .bi-gift'));
    assert.equal(content.textContent.trim(), 'Hariom Ahlawat');

    const eventEl = window.document.createElement('a');
    eventEl.className = 'fc-event';
    eventEl.appendChild(content);
    FakeCalendar.lastInstance.opts.eventDidMount({ event, el: eventEl });

    assert.ok(eventEl.classList.contains('pm-cat-birthday'));
    assert.ok(eventEl.classList.contains('pm-celebration-event'));
    assert.ok(!eventEl.classList.contains('pm-recurring'));
    assert.equal(eventEl.querySelectorAll('.pm-celebration-event__name').length, 1);
    assert.equal(eventEl.textContent.trim(), 'Hariom Ahlawat');
    assert.equal(eventEl.getAttribute('aria-label'), 'Birthday: Hariom Ahlawat');
});

test('celebration details show one date, annual recurrence, and no task action', async () => {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <div id="calendar" data-show-celebrations="false"></div>
        <div id="eventDetailsCanvas" class="offcanvas">
            <span id="eventDetailsIcon"><i></i></span>
            <div id="eventDetailsEyebrow"></div>
            <h5 id="eventDetailsLabel"></h5>
            <div id="eventDetailsTime"></div>
            <div id="eventDetailsRepeat" class="d-none"><span>Repeats annually</span></div>
            <div id="eventDetailsCategoryRow"><span id="eventDetailsCategory"></span></div>
            <div id="eventDetailsLocationRow"><span id="eventDetailsLocation"></span></div>
            <div id="eventDetailsDescriptionRow"><span id="eventDetailsDescription"></span></div>
            <button id="btnAddToTasks"></button>
        </div>
    </body></html>`, { url: 'https://example.test/Calendar', runScripts: 'dangerously' });

    const { window } = dom;
    let shown = false;
    window.alert = () => {};
    window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
    window.fetch = async () => ({ ok: true, status: 200, json: async () => [] });
    window.bootstrap = {
        Offcanvas: {
            getOrCreateInstance: () => ({ show: () => { shown = true; }, hide() {} })
        }
    };
    window.FullCalendar = {
        Calendar: FakeCalendar,
        dayGrid: () => ({}),
        timeGrid: () => ({}),
        list: () => ({}),
        interaction: () => ({})
    };

    const scriptEl = window.document.createElement('script');
    scriptEl.textContent = scriptContent;
    window.document.body.appendChild(scriptEl);
    await new Promise(resolve => setTimeout(resolve, 10));

    const event = {
        id: 'birthday-1',
        title: 'Birthday: Hariom Ahlawat',
        start: new Date('2026-09-21T00:00:00Z'),
        end: new Date('2026-09-22T00:00:00Z'),
        allDay: true,
        extendedProps: {
            category: 'Birthday',
            isCelebration: true,
            isRecurring: true,
            taskUrl: '/calendar/events/birthday-1/task'
        }
    };

    await FakeCalendar.lastInstance.opts.eventClick({ event });

    assert.equal(window.document.getElementById('eventDetailsEyebrow').textContent, 'Birthday');
    assert.equal(window.document.getElementById('eventDetailsLabel').textContent, 'Hariom Ahlawat');
    assert.equal(window.document.getElementById('eventDetailsTime').textContent, '21 September 2026');
    assert.ok(!window.document.getElementById('eventDetailsRepeat').classList.contains('d-none'));
    assert.ok(window.document.getElementById('eventDetailsCategoryRow').classList.contains('d-none'));
    assert.ok(window.document.getElementById('btnAddToTasks').classList.contains('d-none'));
    assert.ok(window.document.getElementById('eventDetailsIcon').classList.contains('is-birthday'));
    assert.ok(shown, 'details offcanvas should open');
});

function loadCalendar(bodyHtml, holidays) {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <div id="calendar" data-show-celebrations="false">${bodyHtml}</div>
    </body></html>`, { url: 'https://example.test/Calendar', runScripts: 'dangerously' });
    const { window } = dom;
    window.alert = () => {};
    window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
    window.FullCalendar = {
        Calendar: FakeCalendar,
        dayGrid: () => ({}),
        timeGrid: () => ({}),
        list: () => ({}),
        interaction: () => ({})
    };
    window.fetch = async (url) => ({
        ok: true,
        status: 200,
        json: async () => (url.toString().includes('/holidays') ? holidays : [])
    });
    const scriptEl = window.document.createElement('script');
    scriptEl.textContent = scriptContent;
    window.document.body.appendChild(scriptEl);
    return window;
}

const gazettedHoliday = [{
    date: '2024-12-25',
    isOfficeClosed: true,
    closureType: 'Gazetted',
    entries: [{ id: 1, name: 'Founders Day', type: 'Gazetted', isObservedAsOfficeHoliday: true, affectsSchedule: true }]
}];

test('dates read as elsewhere in PRISM, not US month/day', async () => {
    loadCalendar('', []);
    await new Promise(resolve => setTimeout(resolve, 25));

    const options = FakeCalendar.lastInstance.opts;
    assert.equal(options.locale, 'en-GB');
    assert.equal(JSON.stringify(options.views.timeGridWeek.dayHeaderFormat), JSON.stringify({ weekday: 'short', day: 'numeric', month: 'short' }));
    assert.equal(JSON.stringify(options.views.listMonth.listDayFormat), JSON.stringify({ day: 'numeric', month: 'long', year: 'numeric' }));
});

test('holidays are rows of their own in List view only, and never open the editor', async () => {
    const window = loadCalendar('', gazettedHoliday);
    await new Promise(resolve => setTimeout(resolve, 25));
    const calendar = FakeCalendar.lastInstance;
    const source = calendar._sources.find(s => s.id === 'holidays-list');
    assert.ok(source, 'a holiday list source is registered');
    const fetchRows = () => new Promise(resolve => source.events(
        { startStr: '2024-12-01T00:00:00.000Z', endStr: '2024-12-31T23:59:59.999Z' }, resolve, resolve));

    calendar.view.type = 'dayGridMonth';
    assert.equal((await fetchRows()).length, 0, 'grid views keep holidays as tinted cells only');

    calendar.view.type = 'listMonth';
    const rows = await fetchRows();
    assert.equal(rows.length, 1);
    assert.equal(rows[0].title, 'Founders Day');
    assert.equal(rows[0].start, '2024-12-25');
    assert.equal(rows[0].allDay, true);
    assert.equal(rows[0].editable, false);
    assert.equal(rows[0].extendedProps.isHoliday, true);
    assert.equal(rows[0].extendedProps.holidayLabel, 'Gazetted holiday');

    const content = calendar.opts.eventContent({ event: { title: 'Founders Day', extendedProps: rows[0].extendedProps } });
    assert.equal(content.domNodes[0].querySelector('.pm-list-holiday__label').textContent, 'Gazetted holiday');

    // The click handler ignores holiday rows (read-only or editor variant, whichever is set).
    let opened = false;
    window.bootstrap = { Offcanvas: { getOrCreateInstance: () => ({ show: () => { opened = true; } }) } };
    await calendar.opts.eventClick?.({ event: { title: 'Founders Day', extendedProps: { isHoliday: true } }, jsEvent: { preventDefault() {} } });
    assert.equal(opened, false);
});

test('Week and Day time columns are tinted for a holiday but carry no second badge', async () => {
    const window = loadCalendar(`
        <div class="fc-timegrid-col fc-day" data-date="2024-12-25"><div class="fc-timegrid-col-frame"></div></div>`, gazettedHoliday);
    await new Promise(resolve => setTimeout(resolve, 25));
    const calendar = FakeCalendar.lastInstance;
    const col = window.document.querySelector('.fc-timegrid-col');
    calendar.opts.dayCellDidMount({ el: col, date: new Date('2024-12-25T00:00:00Z'), dateStr: '2024-12-25' });

    assert.ok(col.classList.contains('pm-holiday--gazetted'));
    assert.equal(window.document.querySelectorAll('.pm-holiday-badge').length, 0);
});

test('List holiday rows also read the pre-classification payload (date and name only)', async () => {
    loadCalendar('', [{ date: '2024-12-25', name: 'Founders Day' }]);
    await new Promise(resolve => setTimeout(resolve, 25));
    const calendar = FakeCalendar.lastInstance;
    const source = calendar._sources.find(s => s.id === 'holidays-list');
    calendar.view.type = 'listMonth';

    const rows = await new Promise(resolve => source.events(
        { startStr: '2024-12-01T00:00:00.000Z', endStr: '2024-12-31T23:59:59.999Z' }, resolve, resolve));

    assert.equal(rows.length, 1);
    assert.equal(rows[0].title, 'Founders Day');
    assert.equal(rows[0].extendedProps.holidayLabel, 'Gazetted holiday');
});

const DETAILS_HTML = `
    <div id="eventDetailsCanvas" class="offcanvas">
        <span id="eventDetailsIcon"><i></i></span>
        <div id="eventDetailsEyebrow"></div>
        <h5 id="eventDetailsLabel"></h5>
        <div id="eventDetailsTime"></div>
        <div id="eventDetailsRepeat" class="d-none"><span id="eventDetailsRepeatText">Repeats annually</span></div>
        <div id="eventDetailsCategoryRow"><span id="eventDetailsCategory"></span></div>
        <div id="eventDetailsLocationRow"><span id="eventDetailsLocation"></span></div>
        <div id="eventDetailsDescriptionRow"><span id="eventDetailsDescription"></span></div>
        <button id="btnEditEvent" class="d-none"><span data-edit-label>Edit event</span></button>
        <button id="btnAddToTasks"></button>
    </div>`;

const EDITOR_HTML = `
    <div id="eventFormCanvas">
        <form id="eventForm">
            <input name="id" /><input name="title" /><input name="category" />
            <input name="location" /><textarea name="description"></textarea>
            <input type="checkbox" id="toggleAllDay" />
            <div id="timedDateTimeFields">
                <input type="date" id="eventStartDateTimed" /><input type="time" id="eventStartTimeTimed" />
                <input type="date" id="eventEndDateTimed" /><input type="time" id="eventEndTimeTimed" />
            </div>
            <div id="allDayDateFields">
                <input type="date" id="eventStartDateAllDay" /><input type="date" id="eventEndDateAllDay" />
            </div>
            <button id="btnDeleteEvent" class="d-none"></button>
        </form>
    </div>`;

function loadEditorCalendar(storedEvent, requests = []) {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <div id="calendar" data-show-celebrations="false" data-can-edit="true"></div>
        ${DETAILS_HTML}${EDITOR_HTML}
    </body></html>`, { url: 'https://example.test/Calendar', runScripts: 'dangerously' });
    const { window } = dom;
    const shown = [];
    window.alert = () => {};
    window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
    window.bootstrap = {
        Offcanvas: {
            getOrCreateInstance: (el) => ({
                show: () => { shown.push(el?.id); },
                hide: () => {}
            })
        }
    };
    window.FullCalendar = {
        Calendar: FakeCalendar,
        dayGrid: () => ({}),
        timeGrid: () => ({}),
        list: () => ({}),
        interaction: () => ({})
    };
    window.fetch = async (url, init) => {
        requests.push({ url: url.toString(), init });
        if (url.toString().includes('/holidays')) return { ok: true, status: 200, json: async () => [] };
        if (/\/calendar\/events\/[\w-]+$/.test(url.toString())) return { ok: true, status: 200, json: async () => storedEvent };
        return { ok: true, status: 200, json: async () => [] };
    };
    const scriptEl = window.document.createElement('script');
    scriptEl.textContent = scriptContent;
    window.document.body.appendChild(scriptEl);
    return { window, shown };
}

// A weekly Monday meeting, 10:00–11:00 IST, stored as UTC instants by the server.
const weeklyMeeting = {
    id: 'series-1',
    title: 'Demo · Weekly review',
    start: '2026-10-05T04:30:00Z',
    end: '2026-10-05T05:30:00Z',
    allDay: false,
    category: 'Meeting',
    location: 'Conference room',
    description: '<p>Agenda</p>',
    rawDescription: 'Agenda',
    recurrenceRule: 'FREQ=WEEKLY;INTERVAL=1;BYDAY=MO',
    recurrenceUntilUtc: '2026-11-30T18:29:59Z'
};

test('editors see an event before editing it; Edit opens the editor in IST', async () => {
    const { window, shown } = loadEditorCalendar(weeklyMeeting);
    await new Promise(resolve => setTimeout(resolve, 25));
    const calendar = FakeCalendar.lastInstance;
    const doc = window.document;

    // The clicked occurrence (19 Oct) as FullCalendar hands it over: IST wall clock.
    await calendar.opts.eventClick({
        event: {
            id: 'series-1_20261019T043000Z',
            title: weeklyMeeting.title,
            start: new Date('2026-10-19T10:00:00Z'),
            end: new Date('2026-10-19T11:00:00Z'),
            allDay: false,
            extendedProps: { seriesId: 'series-1', category: 'Meeting', isRecurring: true }
        }
    });

    assert.deepEqual(shown, ['eventDetailsCanvas'], 'details open, not the editor');
    assert.equal(doc.getElementById('eventDetailsTime').textContent, '19 Oct 2026, 10:00–11:00 IST');
    assert.equal(doc.getElementById('eventDetailsRepeatText').textContent, 'Repeats weekly on Mon until 30 Nov 2026');
    assert.ok(!doc.getElementById('eventDetailsRepeat').classList.contains('d-none'));
    const edit = doc.getElementById('btnEditEvent');
    assert.ok(!edit.classList.contains('d-none'), 'editors get an Edit button');
    assert.equal(edit.textContent.trim(), 'Edit series');

    edit.click();
    assert.equal(shown[shown.length - 1], 'eventFormCanvas');
    assert.equal(doc.getElementById('eventStartDateTimed').value, '2026-10-05');
    assert.equal(doc.getElementById('eventStartTimeTimed').value, '10:00');
    assert.equal(doc.getElementById('eventEndTimeTimed').value, '11:00');
    assert.equal(doc.querySelector('[name="title"]').value, weeklyMeeting.title);
});

test('readers get no Edit button', async () => {
    const dom = new JSDOM(`<!DOCTYPE html><html><body>
        <div id="calendar" data-show-celebrations="false"></div>${DETAILS_HTML}
    </body></html>`, { url: 'https://example.test/Calendar', runScripts: 'dangerously' });
    const { window } = dom;
    window.alert = () => {};
    window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
    window.bootstrap = { Offcanvas: { getOrCreateInstance: () => ({ show() {}, hide() {} }) } };
    window.FullCalendar = { Calendar: FakeCalendar, dayGrid: () => ({}), timeGrid: () => ({}), list: () => ({}), interaction: () => ({}) };
    window.fetch = async (url) => ({ ok: true, status: 200, json: async () => (url.toString().includes('/holidays') ? [] : { ...weeklyMeeting, recurrenceRule: null, recurrenceUntilUtc: null }) });
    const scriptEl = window.document.createElement('script');
    scriptEl.textContent = scriptContent;
    window.document.body.appendChild(scriptEl);
    await new Promise(resolve => setTimeout(resolve, 25));

    await FakeCalendar.lastInstance.opts.eventClick({
        event: {
            id: 'series-1', title: weeklyMeeting.title,
            start: new Date('2026-10-05T10:00:00Z'), end: new Date('2026-10-05T11:00:00Z'),
            allDay: false, extendedProps: { category: 'Meeting' }
        }
    });

    assert.ok(window.document.getElementById('btnEditEvent').classList.contains('d-none'));
    assert.ok(window.document.getElementById('eventDetailsRepeat').classList.contains('d-none'), 'a one-off event does not say it repeats');
    assert.equal(window.document.getElementById('eventDetailsTime').textContent, '05 Oct 2026, 10:00–11:00 IST');
});

test('times are IST whatever the browser zone: feed in, range out, moves saved as UTC', async () => {
    const requests = [];
    const { window } = loadEditorCalendar(weeklyMeeting, requests);
    await new Promise(resolve => setTimeout(resolve, 25));
    const calendar = FakeCalendar.lastInstance;

    assert.equal(calendar.opts.timeZone, 'UTC');
    const now = calendar.opts.now();
    assert.ok(Math.abs(now.getTime() - (Date.now() + 330 * 60000)) < 5000, '"now" is IST wall clock');

    // The month grid starts at IST midnight on 28 Sep; the server is asked from that instant.
    const primary = calendar.opts.eventSources.find(source => source.id === 'primary');
    window.fetch = async (url) => {
        requests.push({ url: url.toString() });
        return { ok: true, status: 200, json: async () => [{ id: 'a', title: 'Demo', start: '2026-10-05T04:30:00Z', end: '2026-10-05T05:30:00Z' }] };
    };
    const events = await new Promise((resolve, reject) => primary.events(
        { startStr: '2026-09-28T00:00:00Z', endStr: '2026-11-09T00:00:00Z' }, resolve, reject));
    const feedUrl = new URL(requests[requests.length - 1].url, 'https://example.test');
    assert.equal(feedUrl.searchParams.get('start'), '2026-09-27T18:30:00.000Z');
    assert.equal(feedUrl.searchParams.get('end'), '2026-11-08T18:30:00.000Z');
    assert.equal(events[0].start.toISOString(), '2026-10-05T10:00:00.000Z', '04:30 UTC shows as 10:00 IST');

    // Dragging the 10:00 IST meeting to 14:00 IST saves 08:30 UTC.
    let body = null;
    window.fetch = async (url, init) => { body = JSON.parse(init.body); return { ok: true, status: 200, json: async () => ({}) }; };
    await calendar.opts.eventDrop({
        event: { id: 'a', title: 'Demo', allDay: false, start: new Date('2026-10-05T14:00:00Z'), end: new Date('2026-10-05T15:00:00Z'), extendedProps: { category: 'Meeting' } },
        oldEvent: { title: 'Demo', allDay: false, start: new Date('2026-10-05T10:00:00Z'), end: new Date('2026-10-05T11:00:00Z'), extendedProps: { category: 'Meeting' } },
        revert() {}
    });
    assert.equal(body.startUtc, '2026-10-05T08:30:00.000Z');
    assert.equal(body.endUtc, '2026-10-05T09:30:00.000Z');
});

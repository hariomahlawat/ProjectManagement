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
                name: 'Founders Day',
                startUtc: '2024-12-25T00:00:00Z',
                endUtc: '2024-12-26T00:00:00Z'
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
    const holidayBadge = holidayCell.querySelector('.pm-holiday-badge');
    assert.ok(holidayBadge, 'holiday cell should receive badge');
    assert.equal(holidayBadge.textContent, 'Holiday: Founders Day');

    const nonHolidayCell = calendarEl.querySelector('.fc-daygrid-day[data-date="2024-12-26"]');
    assert.ok(!nonHolidayCell.classList.contains('pm-holiday'));
    assert.equal(nonHolidayCell.querySelector('.pm-holiday-badge'), null);

    const numberEl = holidayCell.querySelector('.fc-daygrid-day-number');
    assert.equal(numberEl.getAttribute('title'), 'Holiday: Founders Day');
    assert.ok((numberEl.getAttribute('aria-label') || '').includes('Holiday: Founders Day'));

    const headerCell = calendarEl.querySelector('.fc-col-header-cell');
    assert.ok(headerCell.classList.contains('pm-holiday'));

    const timeGridFrame = calendarEl.querySelector('.fc-timegrid-col-frame');
    assert.ok(timeGridFrame.classList.contains('pm-holiday'));
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

test('birthday event is rendered as a compact accessible celebration chip', async () => {
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

    const eventEl = window.document.createElement('a');
    eventEl.className = 'fc-event';
    eventEl.innerHTML = '<div class="fc-event-main"><div class="fc-event-main-frame"><div class="fc-event-title-container"><div class="fc-event-title">Birthday: Hariom Ahlawat</div></div></div></div>';
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

    FakeCalendar.lastInstance.opts.eventDidMount({ event, el: eventEl });

    assert.ok(eventEl.classList.contains('pm-cat-birthday'));
    assert.ok(eventEl.classList.contains('pm-celebration-event'));
    assert.ok(!eventEl.classList.contains('pm-recurring'));
    assert.equal(eventEl.querySelector('.fc-event-title').textContent, 'Hariom Ahlawat');
    assert.ok(eventEl.querySelector('.pm-celebration-event__icon .bi-gift'));
    assert.equal(eventEl.getAttribute('aria-label'), 'Birthday: Hariom Ahlawat');
});

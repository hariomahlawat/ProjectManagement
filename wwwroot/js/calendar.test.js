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
    }

    on(name, handler) {
        if (!this._handlers.has(name)) {
            this._handlers.set(name, []);
        }
        this._handlers.get(name).push(handler);
    }

    addEventSource() {
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

    const nonHolidayCell = calendarEl.querySelector('.fc-daygrid-day[data-date="2024-12-26"]');
    assert.ok(!nonHolidayCell.classList.contains('pm-holiday'));

    const numberEl = holidayCell.querySelector('.fc-daygrid-day-number');
    assert.equal(numberEl.getAttribute('title'), 'Holiday: Founders Day');
    assert.ok((numberEl.getAttribute('aria-label') || '').includes('Holiday: Founders Day'));

    const headerCell = calendarEl.querySelector('.fc-col-header-cell');
    assert.ok(headerCell.classList.contains('pm-holiday'));

    const timeGridFrame = calendarEl.querySelector('.fc-timegrid-col-frame');
    assert.ok(timeGridFrame.classList.contains('pm-holiday'));
});

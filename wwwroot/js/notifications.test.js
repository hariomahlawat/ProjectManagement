const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const fs = require('node:fs');
const path = require('node:path');

const scriptContent = fs.readFileSync(path.resolve(__dirname, 'notifications.js'), 'utf8');

function notification(overrides = {}) {
  return {
    id: 17,
    module: 'Projects',
    eventType: 'StageStatusChanged',
    projectId: 4,
    projectName: 'Project AURA',
    route: '/projects/overview/4',
    title: 'TEC stage completed',
    summary: 'The stage moved to Completed.',
    createdUtc: '2026-06-30T06:00:00Z',
    createdDisplayIst: '30 Jun 2026, 11:30 IST',
    readUtc: null,
    seenUtc: null,
    isProjectMuted: false,
    category: 'Project lifecycle',
    iconCssClass: 'bi bi-diagram-3',
    ...overrides
  };
}

async function createBell({ csrfToken = 'token-123', items = [notification()], unreadCount = 1 } = {}) {
  const tokenMarkup = csrfToken == null
    ? ''
    : `<meta name="csrf-token" content="${csrfToken}">`;
  const dom = new JSDOM(`<!DOCTYPE html><html><head>${tokenMarkup}</head><body>
    <div data-notification-bell
         data-api-base="/api/notifications"
         data-unread-url="/api/notifications/count"
         data-hub-url=""
         data-notification-limit="10"
         data-unread-count="${unreadCount}"
         data-is-authenticated="true"
         data-notification-center-url="/Notifications">
      <script type="application/json" data-notification-bootstrap>${JSON.stringify(items)}</script>
      <button type="button" class="notification-bell__trigger"></button>
      <span data-notification-unread><span data-notification-unread-count>${unreadCount}</span></span>
      <span data-notification-header-count>${unreadCount}</span>
      <button type="button" data-notification-action="mark-all-read">Mark all</button>
      <div data-notification-status></div>
      <div data-notification-empty hidden></div>
      <ul data-notification-list></ul>
      <template data-notification-item-template>
        <li data-notification-item>
          <i data-notification-icon></i>
          <span data-notification-project></span>
          <a data-notification-link><span data-notification-title></span></a>
          <span data-notification-summary></span>
          <time data-notification-created></time>
          <span data-notification-actor-separator>•</span>
          <span data-notification-actor></span>
          <span data-notification-unread-dot></span>
          <button type="button" data-notification-action="toggle-read"><i class="bi"></i></button>
        </li>
      </template>
    </div>
  </body></html>`, {
    url: 'https://example.test/Dashboard',
    runScripts: 'outside-only'
  });

  const { window } = dom;
  const requests = [];
  window.Headers = global.Headers;
  window.setInterval = () => 1;
  window.clearInterval = () => {};
  window.fetch = async (url, options = {}) => {
    requests.push({ url: String(url), options });
    const pathname = new URL(String(url), window.location.origin).pathname;
    let payload = {};
    if (pathname.endsWith('/read-all')) {
      payload = {
        notificationIds: [],
        isRead: true,
        readUtc: '2026-06-30T07:00:00Z',
        seenUtc: '2026-06-30T07:00:00Z',
        appliesToAll: true,
        affectedCount: unreadCount,
        unreadCount: 0
      };
    } else if (pathname.endsWith('/seen')) {
      const ids = JSON.parse(options.body).ids;
      payload = {
        notificationIds: ids,
        seenUtc: '2026-06-30T07:00:00Z',
        affectedCount: ids.length
      };
    }

    return {
      ok: true,
      status: 200,
      headers: new global.Headers({ 'content-type': 'application/json' }),
      json: async () => payload
    };
  };

  // Let JSDOM complete document loading so the production initializer runs once immediately.
  await new Promise(resolve => window.setTimeout(resolve, 0));
  window.eval(scriptContent);
  await new Promise(resolve => window.setTimeout(resolve, 0));

  return { dom, window, document: window.document, requests };
}

test('mark all read uses one protected bulk request and updates the unread badge', async () => {
  const fixture = await createBell();
  const { dom, window, document, requests } = fixture;

  document.querySelector('[data-notification-action="mark-all-read"]')
    .dispatchEvent(new window.MouseEvent('click', { bubbles: true, button: 0 }));
  await new Promise(resolve => window.setTimeout(resolve, 0));

  const mutations = requests.filter(request => request.options.method === 'POST');
  assert.equal(mutations.length, 1);
  assert.equal(new URL(mutations[0].url, window.location.origin).pathname, '/api/notifications/read-all');
  assert.equal(mutations[0].options.headers.get('X-CSRF-TOKEN'), 'token-123');
  assert.equal(document.querySelector('[data-notification-unread-count]').textContent, '0');

  dom.window.close();
});

test('opening the notification dropdown records unseen rows through the protected seen endpoint', async () => {
  const fixture = await createBell({ items: [notification({ id: 17 }), notification({ id: 18, seenUtc: '2026-06-29T07:00:00Z' })], unreadCount: 2 });
  const { dom, window, document, requests } = fixture;

  document.querySelector('.notification-bell__trigger')
    .dispatchEvent(new window.Event('shown.bs.dropdown', { bubbles: true }));
  await new Promise(resolve => window.setTimeout(resolve, 0));

  const seenRequest = requests.find(request => new URL(request.url, window.location.origin).pathname.endsWith('/seen'));
  assert.ok(seenRequest);
  assert.equal(seenRequest.options.headers.get('X-CSRF-TOKEN'), 'token-123');
  assert.deepEqual(JSON.parse(seenRequest.options.body), { ids: [17] });

  dom.window.close();
});

test('a missing antiforgery token blocks mutation before any request is sent', async () => {
  const fixture = await createBell({ csrfToken: null });
  const { dom, window, document, requests } = fixture;

  document.querySelector('[data-notification-action="mark-all-read"]')
    .dispatchEvent(new window.MouseEvent('click', { bubbles: true, button: 0 }));
  await new Promise(resolve => window.setTimeout(resolve, 0));

  assert.equal(requests.length, 0);
  assert.match(document.querySelector('[data-notification-status]').textContent, /security token is unavailable/i);

  dom.window.close();
});

test('bell groups notifications by date and exposes the full summary as a tooltip', async () => {
  const fixture = await createBell({
    items: [notification({
      summary: 'noting sheets – 1',
      summaryTooltip: 'noting sheets - 1-3676667439909_127133026_2_2026.pdf'
    })]
  });
  const { dom, document } = fixture;

  assert.ok(document.querySelector('[data-notification-date-group]'));
  assert.equal(
    document.querySelector('[data-notification-summary]').getAttribute('title'),
    'noting sheets - 1-3676667439909_127133026_2_2026.pdf');

  dom.window.close();
});

test('opening a bell notification closes the dropdown before navigation', async () => {
  const fixture = await createBell();
  const { dom, window, document } = fixture;
  let hideCalls = 0;

  window.bootstrap = {
    Dropdown: {
      getOrCreateInstance: () => ({ hide: () => { hideCalls += 1; } })
    }
  };
  // Keep the read request pending so JSDOM is not asked to perform the eventual navigation.
  window.fetch = () => new Promise(() => {});

  document.querySelector('[data-notification-link]')
    .dispatchEvent(new window.MouseEvent('click', { bubbles: true, button: 0 }));

  assert.equal(hideCalls, 1);
  dom.window.close();
});

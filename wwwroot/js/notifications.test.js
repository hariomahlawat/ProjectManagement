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

async function createCenter() {
  const inboxItem = notification({ id: 21, title: 'TEC stage completed' });
  const documentItem = notification({
    id: 22,
    kind: 'DocumentPublished',
    module: 'Documents',
    eventType: 'DocumentPublished',
    title: 'Document published',
    summary: 'Noting sheets – 1',
    category: 'Documents',
    iconCssClass: 'bi bi-file-earmark-check',
    readUtc: '2026-06-30T07:00:00Z'
  });
  const folders = [
    { key: 'inbox', totalCount: 2, unreadCount: 1 },
    { key: 'unread', totalCount: 1, unreadCount: 1 },
    { key: 'documents', totalCount: 1, unreadCount: 0 },
    { key: 'all', totalCount: 2, unreadCount: 1 },
    { key: 'muted', totalCount: 0, unreadCount: 0 }
  ];
  const bootstrap = {
    items: [inboxItem, documentItem],
    totalCount: 2,
    unreadCount: 1,
    nextCursor: null,
    hasMore: false,
    projects: [{ id: 4, label: 'Project AURA', isMuted: false }],
    modules: ['Documents', 'Projects'],
    folders
  };

  const dom = new JSDOM(`<!DOCTYPE html><html><head><meta name="csrf-token" content="center-token"></head><body>
    <section data-notification-center data-active-folder="inbox"
             data-api-base="/api/notifications" data-unread-url="/api/notifications/count"
             data-hub-url="" data-notification-limit="30" data-unread-count="1"
             data-notification-center-url="/Notifications">
      <script type="application/json" data-notification-bootstrap>${JSON.stringify(bootstrap)}</script>
      <span data-notification-unread-count>1</span>
      <span data-notification-total-count>2</span>
      <span data-notification-total-label>notifications</span>
      <button data-notification-folder="inbox" class="is-active"><span>Inbox</span><span data-folder-count data-folder-key="inbox" data-folder-count-mode="unread">1</span></button>
      <button data-notification-folder="documents"><span>Documents</span><span data-folder-count data-folder-key="documents" data-folder-count-mode="unread"></span></button>
      <div data-default-actions><button data-notification-action="refresh"></button><button data-notification-action="mark-all-read"></button></div>
      <div data-selection-actions hidden>
        <span data-selection-count></span>
        <button data-notification-bulk="mark-read"></button>
        <button data-notification-bulk="mark-unread"></button>
        <button data-notification-bulk="mute-project"></button>
        <button data-notification-bulk="unmute-project"></button>
      </div>
      <input type="checkbox" data-select-all>
      <span data-notification-list-summary></span>
      <form data-notification-filters>
        <input data-filter-search>
        <select data-filter-status><option value="all">All</option><option value="unread">Unread</option><option value="read">Read</option></select>
        <select data-filter-project><option value="">All projects</option></select>
        <select data-filter-module><option value="">All modules</option></select>
        <button type="button" data-notification-action="clear-filters" disabled></button>
      </form>
      <div data-active-filter-chips hidden></div>
      <div data-notification-loading hidden></div>
      <div data-notification-rows></div>
      <div data-notification-empty hidden></div>
      <div data-notification-status></div>
      <button data-notification-load-more hidden></button>
      <template data-notification-row-template>
        <article data-notification-row tabindex="0">
          <input type="checkbox" data-notification-select>
          <i data-notification-icon></i>
          <span data-notification-unread-dot></span>
          <span data-notification-project></span>
          <span data-notification-category></span>
          <a data-notification-link data-notification-title></a>
          <span data-notification-summary-separator>—</span>
          <span data-notification-summary></span>
          <span data-notification-actor></span>
          <span data-notification-action-required hidden></span>
          <span data-notification-muted hidden></span>
          <time data-notification-created></time>
          <button data-notification-row-action="toggle-read"><i class="bi"></i></button>
          <button data-notification-row-action="toggle-mute"><i class="bi"></i></button>
          <a data-notification-open-new></a>
          <span data-notification-state></span>
        </article>
      </template>
    </section>
  </body></html>`, {
    url: 'https://example.test/Notifications',
    runScripts: 'outside-only'
  });

  const { window } = dom;
  const requests = [];
  window.Headers = global.Headers;
  window.setInterval = () => 1;
  window.clearInterval = () => {};
  window.fetch = async (url, options = {}) => {
    requests.push({ url: String(url), options });
    const parsed = new URL(String(url), window.location.origin);
    let payload;
    if ((options.method || 'GET').toUpperCase() === 'GET') {
      const folder = parsed.searchParams.get('folder');
      payload = {
        items: folder === 'documents' ? [documentItem] : [inboxItem, documentItem],
        totalCount: folder === 'documents' ? 1 : 2,
        unreadCount: 1,
        nextCursor: null,
        hasMore: false,
        projects: bootstrap.projects,
        modules: bootstrap.modules,
        folders
      };
    } else if (parsed.pathname.endsWith('/read')) {
      const ids = JSON.parse(options.body).ids;
      payload = {
        notificationIds: ids,
        isRead: true,
        readUtc: '2026-06-30T07:30:00Z',
        seenUtc: '2026-06-30T07:30:00Z',
        appliesToAll: false,
        affectedCount: ids.length,
        unreadCount: 0
      };
    } else {
      payload = {};
    }

    return {
      ok: true,
      status: 200,
      headers: new global.Headers({ 'content-type': 'application/json' }),
      json: async () => payload
    };
  };

  await new Promise(resolve => window.setTimeout(resolve, 0));
  window.eval(scriptContent);
  await new Promise(resolve => window.setTimeout(resolve, 0));

  return { dom, window, document: window.document, requests };
}

test('Notification Centre folder rail applies a server-side Gmail folder filter', async () => {
  const fixture = await createCenter();
  const { dom, window, document, requests } = fixture;

  document.querySelector('[data-notification-folder="documents"]')
    .dispatchEvent(new window.MouseEvent('click', { bubbles: true, button: 0 }));
  await new Promise(resolve => window.setTimeout(resolve, 0));

  const request = requests.find(item => new URL(item.url, window.location.origin).searchParams.get('folder') === 'documents');
  assert.ok(request);
  assert.ok(document.querySelector('[data-notification-folder="documents"]').classList.contains('is-active'));
  assert.equal(document.querySelectorAll('[data-notification-row]').length, 1);
  assert.equal(document.querySelector('[data-notification-title]').textContent, 'Document published');

  dom.window.close();
});

test('Notification Centre shows selection actions only while rows are selected', async () => {
  const fixture = await createCenter();
  const { dom, window, document } = fixture;
  const checkbox = document.querySelector('[data-notification-select]');

  checkbox.checked = true;
  checkbox.dispatchEvent(new window.Event('change', { bubbles: true }));

  assert.equal(document.querySelector('[data-default-actions]').hidden, true);
  assert.equal(document.querySelector('[data-selection-actions]').hidden, false);
  assert.equal(document.querySelector('[data-selection-count]').textContent, '1 selected');

  checkbox.checked = false;
  checkbox.dispatchEvent(new window.Event('change', { bubbles: true }));
  assert.equal(document.querySelector('[data-default-actions]').hidden, false);
  assert.equal(document.querySelector('[data-selection-actions]').hidden, true);

  dom.window.close();
});

test('Notification Centre row quick action sends one protected bulk mutation', async () => {
  const fixture = await createCenter();
  const { dom, window, document, requests } = fixture;

  document.querySelector('[data-notification-row-action="toggle-read"]')
    .dispatchEvent(new window.MouseEvent('click', { bubbles: true, button: 0 }));
  await new Promise(resolve => window.setTimeout(resolve, 0));

  const mutation = requests.find(item => (item.options.method || '').toUpperCase() === 'POST');
  assert.ok(mutation);
  assert.equal(new URL(mutation.url, window.location.origin).pathname, '/api/notifications/read');
  assert.equal(mutation.options.headers.get('X-CSRF-TOKEN'), 'center-token');

  dom.window.close();
});

test('Notification Centre exposes active filters as removable chips', async () => {
  const fixture = await createCenter();
  const { dom, window, document } = fixture;

  document.querySelector('[data-notification-folder="documents"]')
    .dispatchEvent(new window.MouseEvent('click', { bubbles: true, button: 0 }));
  await new Promise(resolve => window.setTimeout(resolve, 0));

  const chip = document.querySelector('[data-notification-filter-chip="folder"]');
  assert.ok(chip);
  assert.equal(chip.textContent.trim(), 'Documents');
  assert.equal(document.querySelector('[data-notification-total-label]').textContent, 'matching');
  assert.equal(document.querySelector('[data-notification-action="clear-filters"]').disabled, false);

  chip.dispatchEvent(new window.MouseEvent('click', { bubbles: true, button: 0 }));
  await new Promise(resolve => window.setTimeout(resolve, 0));

  assert.ok(document.querySelector('[data-notification-folder="inbox"]').classList.contains('is-active'));
  assert.equal(document.querySelector('[data-notification-total-label]').textContent, 'notifications');
  assert.equal(document.querySelector('[data-active-filter-chips]').hidden, true);

  dom.window.close();
});

test('Notification Centre uses a compact inbox range', async () => {
  const fixture = await createCenter();
  const { dom, document } = fixture;

  assert.equal(document.querySelector('[data-notification-list-summary]').textContent, '1–2 of 2');

  dom.window.close();
});


test('notification realtime uses application-owned diagnostics instead of duplicate SignalR console logging', async () => {
  const dom = new JSDOM(`<!DOCTYPE html><html><head><meta name="csrf-token" content="token"></head><body>
    <div data-notification-bell data-api-base="/api/notifications" data-unread-url="/api/notifications/count"
         data-hub-url="/hubs/notifications" data-notification-limit="10" data-unread-count="0"
         data-is-authenticated="true" data-notification-center-url="/Notifications">
      <script type="application/json" data-notification-bootstrap>[]</script>
      <button class="notification-bell__trigger"></button>
      <span data-notification-unread><span data-notification-unread-count>0</span></span>
      <span data-notification-header-count>0</span>
      <button data-notification-action="mark-all-read"></button>
      <div data-notification-status></div><div data-notification-empty></div><ul data-notification-list></ul>
      <template data-notification-item-template><li data-notification-item><a data-notification-link><span data-notification-title></span></a></li></template>
    </div>
  </body></html>`, { url: 'https://example.test/Notebook', runScripts: 'outside-only' });

  const { window } = dom;
  let configuredLogLevel = null;
  const handlers = {};
  const connection = {
    state: 'Disconnected',
    start: async () => { connection.state = 'Connected'; },
    on: () => {},
    onreconnecting: handler => { handlers.reconnecting = handler; },
    onreconnected: handler => { handlers.reconnected = handler; },
    onclose: handler => { handlers.close = handler; }
  };
  const builder = {
    withUrl: () => builder,
    withAutomaticReconnect: () => builder,
    configureLogging: level => { configuredLogLevel = level; return builder; },
    build: () => connection
  };
  window.Headers = global.Headers;
  window.fetch = async () => ({
    ok: true,
    status: 200,
    headers: new global.Headers({ 'content-type': 'application/json' }),
    json: async () => ({ items: [], unreadCount: 0 })
  });
  window.signalR = {
    HubConnectionBuilder: function HubConnectionBuilder() { return builder; },
    HubConnectionState: { Disconnected: 'Disconnected' },
    LogLevel: { None: 6 }
  };

  await new Promise(resolve => window.setTimeout(resolve, 0));
  window.eval(scriptContent);
  await new Promise(resolve => window.setTimeout(resolve, 0));

  assert.equal(configuredLogLevel, window.signalR.LogLevel.None);
  assert.equal(typeof handlers.reconnecting, 'function');
  assert.equal(typeof handlers.close, 'function');
  dom.window.close();
});

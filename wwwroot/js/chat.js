/*
 * PRISM chat: buddy list and docked one-to-one chat windows (bottom-right, Gmail-style).
 *
 * Server contract (Features/Chat/ChatApi.cs, Hubs/ChatHub.cs):
 *   GET  {api}/contacts                         buddy list with presence and unread counts
 *   GET  {api}/with/{peer}/messages?before=&limit=
 *   POST {api}/messages                         { recipientUserId, clientMessageId, body }
 *   POST {api}/with/{peer}/read                 { upToMessageId }
 *   hub  ReceiveMessage / ConversationRead / PresenceChanged
 *
 * Every piece of user text is written with textContent; nothing from the server is parsed as HTML.
 */
(function (global) {
  'use strict';

  const document = global.document;
  const TIME_ZONE = 'Asia/Kolkata';
  const HISTORY_PAGE = 40;
  const POLL_MS = 30000;
  const CATCH_UP_PAGES = 25; // up to 1,000 missed messages are stitched in before older ones are dropped
  const WINDOW_WIDTH = 300;
  const BUDDY_WIDTH = 280;
  const DOCK_GAP = 12;

  const timeFormat = new Intl.DateTimeFormat('en-IN', { hour: '2-digit', minute: '2-digit', hour12: false, timeZone: TIME_ZONE });
  const dayKeyFormat = new Intl.DateTimeFormat('en-CA', { year: 'numeric', month: '2-digit', day: '2-digit', timeZone: TIME_ZONE });
  const dayLabelFormat = new Intl.DateTimeFormat('en-IN', { day: 'numeric', month: 'short', year: 'numeric', timeZone: TIME_ZONE });

  // ---------------------------------------------------------------------------
  // Pure helpers (exposed for tests)
  // ---------------------------------------------------------------------------

  /** RFC 4122 v4 id. crypto.randomUUID needs a secure context, which a plain-HTTP LAN site is not. */
  function newClientId(cryptoImpl) {
    const bytes = new Uint8Array(16);
    (cryptoImpl || global.crypto).getRandomValues(bytes);
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
  }

  function dayKey(date) {
    return dayKeyFormat.format(date);
  }

  function dayLabel(date, now) {
    const key = dayKey(date);
    if (key === dayKey(now)) return 'Today';
    if (key === dayKey(new Date(now.getTime() - 86400000))) return 'Yesterday';
    return dayLabelFormat.format(date);
  }

  /** Unread first, then online, then by name. Inactive accounts sink to the bottom. */
  function sortContacts(contacts) {
    return contacts.slice().sort((a, b) =>
      (Number(b.unreadCount > 0) - Number(a.unreadCount > 0))
      || (Number(b.isActive) - Number(a.isActive))
      || (Number(b.isOnline) - Number(a.isOnline))
      || a.displayName.localeCompare(b.displayName, undefined, { sensitivity: 'base' }));
  }

  function filterContacts(contacts, term) {
    const needle = (term || '').trim().toLowerCase();
    if (!needle) return contacts;
    return contacts.filter((c) =>
      c.displayName.toLowerCase().includes(needle) || (c.rank || '').toLowerCase().includes(needle));
  }

  /** How many chat windows fit beside the buddy list. */
  function windowCapacity(viewportWidth) {
    if (viewportWidth < 576) return 1;
    return Math.max(1, Math.floor((viewportWidth - BUDDY_WIDTH - 2 * DOCK_GAP) / (WINDOW_WIDTH + DOCK_GAP)));
  }

  /** Merge messages by server id, keeping order by id; pending (id-less) messages stay last. */
  function mergeMessages(existing, incoming) {
    const byId = new Map();
    const pending = [];
    for (const m of existing) {
      if (m.id) byId.set(m.id, m); else pending.push(m);
    }
    for (const m of incoming) {
      byId.set(m.id, m);
      const index = pending.findIndex((p) => p.clientMessageId === m.clientMessageId);
      if (index >= 0) pending.splice(index, 1);
    }
    return Array.from(byId.values()).sort((a, b) => a.id - b.id).concat(pending);
  }

  /**
   * Fetches pages newest-first until they reach a message newer than knownMaxId has already
   * covered, so a window that missed messages while offline gets all of them, not just the
   * latest page. `fetchPage(beforeId)` returns `{ messages, hasMore }` with messages oldest first.
   * `overlaps` is false when the page cap ran out first; the caller then drops its older copy.
   */
  async function fetchSince(fetchPage, knownMaxId, maxPages) {
    let collected = [];
    let before = null;
    for (let page = 0; page < maxPages; page += 1) {
      const history = await fetchPage(before);
      collected = history.messages.concat(collected);
      const oldest = history.messages[0];
      if (!history.hasMore || !oldest || oldest.id <= knownMaxId) {
        return { messages: collected, overlaps: true, hasMore: history.hasMore };
      }
      before = oldest.id;
    }
    return { messages: collected, overlaps: false, hasMore: true };
  }

  global.PMChatInternals = { newClientId, dayLabel, sortContacts, filterContacts, windowCapacity, mergeMessages, fetchSince };

  // ---------------------------------------------------------------------------
  // DOM helper
  // ---------------------------------------------------------------------------

  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined && text !== null) node.textContent = text;
    return node;
  }

  function icon(name) {
    const i = el('i', `bi bi-${name}`);
    i.setAttribute('aria-hidden', 'true');
    return i;
  }

  function iconButton(name, label, className) {
    const button = el('button', `pm-chat__icon-btn ${className || ''}`.trim());
    button.type = 'button';
    button.setAttribute('aria-label', label);
    button.title = label;
    button.append(icon(name));
    return button;
  }

  function avatar(contact) {
    const wrap = el('span', 'pm-chat__avatar');
    wrap.append(el('span', 'pm-chat__initials', contact ? contact.initials : '?'));
    const dot = el('span', 'pm-chat__presence');
    wrap.append(dot);
    return wrap;
  }

  function setPresence(avatarNode, contact) {
    const dot = avatarNode.querySelector('.pm-chat__presence');
    dot.classList.toggle('is-online', !!(contact && contact.isOnline));
    dot.classList.toggle('is-inactive', !!(contact && !contact.isActive));
  }

  function statusText(contact) {
    if (!contact) return '';
    if (!contact.isActive) return 'Inactive account';
    return contact.isOnline ? 'Online' : 'Offline';
  }

  // ---------------------------------------------------------------------------
  // Controller
  // ---------------------------------------------------------------------------

  function ChatApp(root) {
    this.root = root;
    this.me = root.dataset.currentUserId;
    this.api = root.dataset.apiBase.replace(/\/$/, '');
    this.hubUrl = root.dataset.hubUrl;
    this.maxLength = Number(root.dataset.maxLength) || 4000;
    this.storageKey = `prism.chat.dock.${this.me}`;
    this.contacts = new Map();
    this.windows = new Map(); // peerId -> window state
    this.order = []; // peer ids, most recently opened first
    this.buddyOpen = false;
    this.connection = null;
    this.pollTimer = null;
    this.stopped = false;
    this.launchers = Array.from(document.querySelectorAll('[data-chat-launcher]'));
  }

  ChatApp.prototype.start = function () {
    this.buildDock();
    this.launchers.forEach((button) => button.addEventListener('click', () => this.toggleBuddy()));
    global.addEventListener('resize', () => this.enforceCapacity());
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible') this.windows.forEach((w) => this.maybeMarkRead(w));
    });

    const saved = this.readState();
    this.buddyOpen = !!saved.buddyOpen;
    this.renderBuddyVisibility();

    this.refreshContacts().then(() => {
      (saved.open || []).slice().reverse().forEach((entry) => {
        if (this.contacts.has(entry.id)) this.openWindow(entry.id, { minimized: !!entry.minimized, focus: false, restore: true });
      });
    });

    this.connect();
  };

  // ----- persistence (per browser, per user; ids only) -----------------------

  ChatApp.prototype.readState = function () {
    try {
      const value = JSON.parse(global.localStorage.getItem(this.storageKey) || '{}');
      return value && typeof value === 'object' ? value : {};
    } catch (_) {
      return {};
    }
  };

  ChatApp.prototype.saveState = function () {
    try {
      global.localStorage.setItem(this.storageKey, JSON.stringify({
        buddyOpen: this.buddyOpen,
        open: this.order.map((id) => ({ id, minimized: this.windows.get(id).minimized })),
      }));
    } catch (_) {
      // Storage blocked: the dock simply starts closed on the next page.
    }
  };

  // ----- HTTP ------------------------------------------------------------------

  ChatApp.prototype.request = async function (method, path, body) {
    const headers = { Accept: 'application/json' };
    if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
      const token = document.querySelector('meta[name="csrf-token"]')?.getAttribute('content');
      if (token) headers['X-CSRF-TOKEN'] = token;
    }

    const response = await fetch(this.api + path, {
      method,
      headers,
      credentials: 'same-origin',
      body: body === undefined ? undefined : JSON.stringify(body),
    });

    if (response.status === 401 || response.status === 404) {
      // Signed out, or chat switched off on the server: stand down quietly.
      this.shutdown();
      throw new Error('Chat is not available.');
    }

    if (!response.ok) {
      let detail = response.status === 429
        ? 'You are sending messages too quickly. Wait a moment and retry.'
        : 'The message could not be sent.';
      try {
        const problem = await response.json();
        if (problem && problem.detail) detail = problem.detail;
      } catch (_) { /* not JSON */ }
      const error = new Error(detail);
      error.status = response.status;
      throw error;
    }

    return response.status === 204 ? null : response.json();
  };

  ChatApp.prototype.shutdown = function () {
    this.stopped = true;
    if (this.pollTimer) global.clearInterval(this.pollTimer);
    if (this.connection) this.connection.stop().catch(() => {});
    this.root.hidden = true;
    this.launchers.forEach((b) => { b.hidden = true; });
  };

  // ----- realtime ----------------------------------------------------------------

  ChatApp.prototype.connect = function () {
    const signalR = global.signalR;
    if (!this.hubUrl || !signalR || !signalR.HubConnectionBuilder) {
      this.startPolling();
      return;
    }

    let builder = new signalR.HubConnectionBuilder()
      .withUrl(this.hubUrl)
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (context) => {
          if (this.stopped) return null;
          const delays = [0, 2000, 10000, 30000, 60000];
          return delays[Math.min(context.previousRetryCount, delays.length - 1)];
        },
      });
    if (typeof builder.configureLogging === 'function' && signalR.LogLevel) {
      builder = builder.configureLogging(signalR.LogLevel.None);
    }

    const connection = builder.build();
    connection.serverTimeoutInMilliseconds = 90000;
    connection.on('ReceiveMessage', (message) => this.onMessage(message));
    connection.on('ConversationRead', (read) => this.onRead(read));
    connection.on('PresenceChanged', (presence) => this.onPresence(presence));
    connection.onreconnecting(() => this.startPolling());
    connection.onreconnected(() => this.resync());
    connection.onclose(() => {
      if (this.stopped) return;
      this.startPolling();
      global.setTimeout(() => this.startConnection(), 30000);
    });
    this.connection = connection;
    this.startConnection();
  };

  ChatApp.prototype.startConnection = function () {
    if (this.stopped || !this.connection) return;
    const state = this.connection.state;
    if (state && state !== 'Disconnected') return;
    this.connection.start()
      .then(() => this.resync())
      .catch(() => {
        this.startPolling();
        global.setTimeout(() => this.startConnection(), 30000);
      });
  };

  /** While the live connection is down, keep badges and open windows roughly current. */
  ChatApp.prototype.startPolling = function () {
    if (this.pollTimer || this.stopped) return;
    this.pollTimer = global.setInterval(() => {
      this.refreshContacts();
      this.windows.forEach((w) => this.loadLatest(w));
    }, POLL_MS);
  };

  ChatApp.prototype.resync = function () {
    if (this.pollTimer) {
      global.clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
    this.refreshContacts();
    this.windows.forEach((w) => this.loadLatest(w));
  };

  ChatApp.prototype.onMessage = function (message) {
    const peerId = message.senderUserId === this.me ? message.recipientUserId : message.senderUserId;
    const incoming = message.senderUserId !== this.me;
    const contact = this.contacts.get(peerId);
    if (contact) {
      contact.lastMessageUtc = message.sentUtc;
    }
    if (!contact || (incoming && !contact.isActive)) {
      // Someone this tab has not seen yet, or still holds as inactive, is writing: the account
      // was created or reactivated since. Fetch their details so the window shows their name
      // and lets the user reply. The refresh replaces unread counts with the server's.
      this.refreshContacts();
    }

    let win = this.windows.get(peerId);
    if (!win && incoming) {
      // Classic behaviour: a new message pops its conversation open.
      win = this.openWindow(peerId, { focus: false });
    }

    if (win) {
      const known = win.messages.some((m) => m.id === message.id);
      win.messages = mergeMessages(win.messages, [message]);
      this.renderMessages(win, { stickToBottom: true });
      if (incoming && !known && !this.isAttended(win)) {
        win.attention = true;
        this.renderWindowState(win);
      }
      if (incoming && !known && contact) contact.unreadCount += 1;
      this.maybeMarkRead(win);
    } else if (incoming && contact) {
      contact.unreadCount += 1;
    }

    this.renderContacts();
  };

  ChatApp.prototype.onRead = function (read) {
    const contact = this.contacts.get(read.peerUserId);
    const win = this.windows.get(read.peerUserId);
    if (win) {
      win.lastReadId = Math.max(win.lastReadId || 0, read.upToMessageId);
      win.attention = false;
      this.renderWindowState(win);
    }
    if (contact) {
      // Recount from what this tab knows; the next contacts refresh corrects any gap.
      contact.unreadCount = win
        ? win.messages.filter((m) => m.id && m.senderUserId !== this.me && m.id > win.lastReadId).length
        : 0;
    }
    this.renderContacts();
  };

  ChatApp.prototype.onPresence = function (presence) {
    const contact = this.contacts.get(presence.userId);
    if (!contact || (presence.isOnline && !contact.isActive)) {
      // New or reactivated account coming online: its details here are missing or stale.
      if (presence.isOnline && presence.userId !== this.me) this.refreshContacts();
      if (!contact) return;
    }
    contact.isOnline = presence.isOnline;
    this.renderContacts();
    const win = this.windows.get(presence.userId);
    if (win) this.renderWindowState(win);
  };

  // ----- buddy list ----------------------------------------------------------------

  ChatApp.prototype.buildDock = function () {
    const dock = el('div', 'pm-chat__dock');
    this.dock = dock;

    const buddy = el('section', 'pm-chat__buddy');
    buddy.setAttribute('aria-label', 'Chat contacts');
    const header = el('header', 'pm-chat__buddy-head');
    const title = el('div', 'pm-chat__buddy-title');
    title.append(el('span', null, 'Chat'));
    this.onlineCount = el('span', 'pm-chat__buddy-count');
    title.append(this.onlineCount);
    const close = iconButton('x-lg', 'Close contacts');
    close.addEventListener('click', () => this.toggleBuddy(false));
    header.append(title, close);

    const searchWrap = el('div', 'pm-chat__search');
    const search = el('input', 'form-control form-control-sm');
    search.type = 'search';
    search.placeholder = 'Search people';
    search.setAttribute('aria-label', 'Search people');
    search.addEventListener('input', () => this.renderContacts());
    searchWrap.append(search);
    this.search = search;

    this.list = el('ul', 'pm-chat__contacts');
    this.list.setAttribute('role', 'list');
    this.emptyNote = el('p', 'pm-chat__empty', 'Loading…');

    buddy.append(header, searchWrap, this.list, this.emptyNote);
    this.buddy = buddy;

    this.windowRow = el('div', 'pm-chat__windows');
    dock.append(this.windowRow, buddy);
    this.root.append(dock);
  };

  ChatApp.prototype.toggleBuddy = function (force) {
    this.buddyOpen = typeof force === 'boolean' ? force : !this.buddyOpen;
    this.renderBuddyVisibility();
    this.saveState();
    if (this.buddyOpen) {
      this.refreshContacts();
      this.search.focus();
    }
  };

  ChatApp.prototype.renderBuddyVisibility = function () {
    this.buddy.hidden = !this.buddyOpen;
    this.root.classList.toggle('has-buddy', this.buddyOpen);
    this.launchers.forEach((b) => b.setAttribute('aria-expanded', String(this.buddyOpen)));
    this.enforceCapacity();
  };

  /** Overlapping calls share one request, plus at most one follow-up for calls made meanwhile. */
  ChatApp.prototype.refreshContacts = function () {
    if (this.contactsRequest) {
      this.contactsAgain = true;
      return this.contactsRequest;
    }
    this.contactsRequest = this.loadContacts().finally(() => {
      this.contactsRequest = null;
      if (this.contactsAgain) {
        this.contactsAgain = false;
        this.refreshContacts();
      }
    });
    return this.contactsRequest;
  };

  ChatApp.prototype.loadContacts = async function () {
    if (this.stopped) return;
    try {
      const contacts = await this.request('GET', '/contacts');
      const fresh = new Map(contacts.map((c) => [c.userId, c]));
      this.contacts = fresh;
      this.renderContacts();
      this.windows.forEach((w) => {
        // A window restored on a new page still flags messages that arrived before it.
        const contact = fresh.get(w.peerId);
        if (contact && contact.unreadCount > 0 && !this.isAttended(w)) w.attention = true;
        this.renderWindowState(w);
      });
    } catch (_) {
      if (!this.contacts.size) this.emptyNote.textContent = 'Chat is unavailable right now.';
    }
  };

  ChatApp.prototype.renderContacts = function () {
    const all = sortContacts(Array.from(this.contacts.values()));
    const shown = filterContacts(all, this.search.value);
    const online = all.filter((c) => c.isOnline && c.isActive).length;
    this.onlineCount.textContent = `${online} online`;

    this.list.replaceChildren(...shown.map((contact) => {
      const item = el('li');
      const button = el('button', 'pm-chat__contact');
      button.type = 'button';
      if (!contact.isActive) button.classList.add('is-inactive');
      const face = avatar(contact);
      setPresence(face, contact);
      const text = el('span', 'pm-chat__contact-text');
      text.append(el('span', 'pm-chat__contact-name', contact.displayName));
      text.append(el('span', 'pm-chat__contact-meta', contact.rank ? `${contact.rank} · ${statusText(contact)}` : statusText(contact)));
      button.append(face, text);
      if (contact.unreadCount > 0) {
        button.append(el('span', 'badge rounded-pill pm-chat__unread', contact.unreadCount > 99 ? '99+' : String(contact.unreadCount)));
      }
      button.setAttribute('aria-label', `${contact.displayName}, ${statusText(contact)}${contact.unreadCount ? `, ${contact.unreadCount} unread` : ''}`);
      button.addEventListener('click', () => this.openWindow(contact.userId, { focus: true }));
      item.append(button);
      return item;
    }));

    this.emptyNote.hidden = shown.length > 0;
    this.emptyNote.textContent = all.length ? 'No one matches that search.' : 'No other users yet.';
    this.renderLauncherBadge();
  };

  ChatApp.prototype.renderLauncherBadge = function () {
    let total = 0;
    this.contacts.forEach((c) => { total += c.unreadCount; });
    document.querySelectorAll('[data-chat-unread]').forEach((badge) => {
      badge.hidden = total === 0;
      const count = badge.querySelector('[data-chat-unread-count]');
      if (count) count.textContent = total > 99 ? '99+' : String(total);
    });
    this.launchers.forEach((b) => b.setAttribute('aria-label', total ? `Open chat, ${total} unread` : 'Open chat'));
  };

  // ----- chat windows --------------------------------------------------------------

  ChatApp.prototype.openWindow = function (peerId, options) {
    const opts = options || {};
    let win = this.windows.get(peerId);
    if (!win) {
      win = this.createWindow(peerId);
      this.windows.set(peerId, win);
      this.windowRow.prepend(win.node);
      this.loadLatest(win);
    }

    if (opts.restore) {
      win.minimized = !!opts.minimized;
    } else if (opts.focus) {
      win.minimized = false;
    }

    this.order = [peerId].concat(this.order.filter((id) => id !== peerId));
    this.windowRow.prepend(win.node);
    this.renderWindowState(win);
    this.enforceCapacity();
    this.saveState();

    if (opts.focus) {
      // On a phone the window takes the whole width, so the contact list steps aside.
      if (windowCapacity(global.innerWidth || 1280) === 1 && this.buddyOpen) this.toggleBuddy(false);
      win.input.focus();
      this.maybeMarkRead(win);
    }
    return win;
  };

  ChatApp.prototype.closeWindow = function (peerId) {
    const win = this.windows.get(peerId);
    if (!win) return;
    win.node.remove();
    this.windows.delete(peerId);
    this.order = this.order.filter((id) => id !== peerId);
    this.saveState();
  };

  /** Too many windows for the screen: the least recently opened ones close (history is kept). */
  ChatApp.prototype.enforceCapacity = function () {
    const capacity = windowCapacity(global.innerWidth || 1280);
    while (this.order.length > capacity) {
      this.closeWindow(this.order[this.order.length - 1]);
    }
  };

  ChatApp.prototype.createWindow = function (peerId) {
    const contact = this.contacts.get(peerId);
    const node = el('section', 'pm-chat__window');
    node.setAttribute('role', 'dialog');
    node.setAttribute('aria-modal', 'false');

    const head = el('header', 'pm-chat__window-head');
    const toggle = el('button', 'pm-chat__window-title');
    toggle.type = 'button';
    const face = avatar(contact);
    const names = el('span', 'pm-chat__window-names');
    const name = el('span', 'pm-chat__window-name', contact ? contact.displayName : 'Chat');
    const status = el('span', 'pm-chat__window-status');
    names.append(name, status);
    toggle.append(face, names);
    const minimize = iconButton('dash-lg', 'Minimise');
    const close = iconButton('x-lg', 'Close chat');
    head.append(toggle, minimize, close);

    const body = el('div', 'pm-chat__window-body');
    const earlier = el('button', 'btn btn-link btn-sm pm-chat__earlier', 'Show earlier messages');
    earlier.type = 'button';
    earlier.hidden = true;
    const log = el('ol', 'pm-chat__log');
    log.setAttribute('role', 'log');
    log.setAttribute('aria-live', 'polite');
    body.append(earlier, log);

    const notice = el('p', 'pm-chat__notice');
    notice.hidden = true;

    const form = el('form', 'pm-chat__compose');
    const input = el('textarea', 'form-control form-control-sm pm-chat__input');
    input.rows = 1;
    input.maxLength = this.maxLength;
    input.placeholder = 'Type a message';
    input.setAttribute('aria-label', `Message ${contact ? contact.displayName : ''}`.trim());
    const send = iconButton('send', 'Send', 'pm-chat__send');
    send.type = 'submit';
    form.append(input, send);

    node.append(head, body, notice, form);

    const win = {
      peerId, node, face, name, status, body, log, earlier, notice, input, form, minimize,
      messages: [], hasMore: false, historyLoaded: false, syncing: false, syncAgain: false, loading: false, minimized: false,
      attention: false, lastReadId: 0, readSending: 0,
    };

    toggle.addEventListener('click', () => {
      win.minimized = !win.minimized;
      if (!win.minimized) input.focus();
      this.renderWindowState(win);
      this.saveState();
      this.maybeMarkRead(win);
    });
    minimize.addEventListener('click', () => {
      win.minimized = true;
      this.renderWindowState(win);
      this.saveState();
    });
    close.addEventListener('click', () => this.closeWindow(peerId));
    earlier.addEventListener('click', () => this.loadEarlier(win));
    node.addEventListener('focusin', () => this.maybeMarkRead(win));
    node.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        win.minimized = true;
        this.renderWindowState(win);
        this.saveState();
      }
    });
    input.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
        event.preventDefault();
        form.requestSubmit ? form.requestSubmit() : form.dispatchEvent(new Event('submit', { cancelable: true }));
      }
    });
    input.addEventListener('input', () => {
      input.style.height = 'auto';
      input.style.height = `${Math.min(input.scrollHeight, 120)}px`;
    });
    form.addEventListener('submit', (event) => {
      event.preventDefault();
      this.send(win);
    });

    return win;
  };

  ChatApp.prototype.renderWindowState = function (win) {
    const contact = this.contacts.get(win.peerId);
    win.node.classList.toggle('is-minimized', win.minimized);
    win.node.classList.toggle('needs-attention', win.attention);
    win.minimize.hidden = win.minimized;
    if (contact) {
      win.name.textContent = contact.displayName;
      win.face.querySelector('.pm-chat__initials').textContent = contact.initials;
      win.node.setAttribute('aria-label', `Chat with ${contact.displayName}`);
    }
    setPresence(win.face, contact);
    win.status.textContent = win.attention ? 'New message' : statusText(contact);

    const canSend = !!contact && contact.isActive;
    win.form.hidden = !canSend;
    win.notice.hidden = canSend;
    win.notice.textContent = canSend ? '' : 'This account is inactive. You can read past messages but cannot send new ones.';
  };

  /**
   * A conversation counts as read only while the person is in it (keyboard focus inside an
   * expanded window on a visible tab), not when a window merely pops open on an incoming message.
   */
  ChatApp.prototype.isAttended = function (win) {
    return !win.minimized
      && document.visibilityState !== 'hidden'
      && win.node.contains(document.activeElement);
  };

  ChatApp.prototype.maybeMarkRead = function (win) {
    if (!this.isAttended(win)) return;
    const newest = win.messages.reduce((max, m) => (m.id && m.senderUserId !== this.me && m.id > max ? m.id : max), 0);
    if (win.attention) {
      win.attention = false;
      this.renderWindowState(win);
    }
    // lastReadId only advances once the server has stored it, so a failed save is retried on
    // the next focus or refresh; readSending stops duplicate requests while one is in flight.
    if (newest <= Math.max(win.lastReadId || 0, win.readSending || 0)) return;
    win.readSending = newest;
    this.request('POST', `/with/${encodeURIComponent(win.peerId)}/read`, { upToMessageId: newest })
      .then(() => { win.lastReadId = Math.max(win.lastReadId || 0, newest); })
      .catch(() => {})
      .finally(() => { if (win.readSending === newest) win.readSending = 0; });
    const contact = this.contacts.get(win.peerId);
    if (contact) {
      contact.unreadCount = 0;
      this.renderContacts();
    }
  };

  /**
   * First call: the latest page, which also decides whether "Show earlier messages" is offered
   * (messages pushed while it was in flight don't count as history). Later calls (reconnect,
   * polling): every message since the newest one already shown, so nothing in between is skipped.
   */
  ChatApp.prototype.loadLatest = async function (win) {
    if (win.syncing) {
      win.syncAgain = true;
      return;
    }
    win.syncing = true;
    const path = `/with/${encodeURIComponent(win.peerId)}/messages?limit=${HISTORY_PAGE}`;
    try {
      if (!win.historyLoaded) {
        const history = await this.request('GET', path);
        win.messages = mergeMessages(win.messages, history.messages);
        win.hasMore = history.hasMore;
        win.historyLoaded = true;
      } else {
        const knownMax = win.messages.reduce((max, m) => (m.id > max ? m.id : max), 0);
        const result = await fetchSince(
          (before) => this.request('GET', before ? `${path}&before=${before}` : path),
          knownMax,
          CATCH_UP_PAGES);
        if (!result.overlaps) {
          // Too much was missed to stitch together: show the recent stretch and let
          // "Show earlier messages" page back from there instead of leaving a silent gap.
          win.messages = win.messages.filter((m) => !m.id);
          win.hasMore = true;
        }
        win.messages = mergeMessages(win.messages, result.messages);
      }
      this.renderMessages(win, { stickToBottom: true });
      this.maybeMarkRead(win);
    } catch (_) {
      if (!win.messages.length) {
        win.log.replaceChildren(el('li', 'pm-chat__system', 'Messages could not be loaded.'));
      }
    } finally {
      win.syncing = false;
      if (win.syncAgain) {
        win.syncAgain = false;
        this.loadLatest(win);
      }
    }
  };

  ChatApp.prototype.loadEarlier = async function (win) {
    const oldest = win.messages.find((m) => m.id);
    if (!oldest || win.loading) return;
    win.loading = true;
    const before = win.body.scrollHeight - win.body.scrollTop;
    try {
      const history = await this.request('GET', `/with/${encodeURIComponent(win.peerId)}/messages?before=${oldest.id}&limit=${HISTORY_PAGE}`);
      win.messages = mergeMessages(win.messages, history.messages);
      win.hasMore = history.hasMore;
      this.renderMessages(win, { stickToBottom: false });
      win.body.scrollTop = win.body.scrollHeight - before;
    } catch (_) {
      // Leave the button so the person can try again.
    } finally {
      win.loading = false;
    }
  };

  ChatApp.prototype.renderMessages = function (win, options) {
    const body = win.body;
    const nearBottom = body.scrollHeight - body.scrollTop - body.clientHeight < 48;
    const now = new Date();
    const items = [];
    let lastDay = null;

    if (!win.messages.length) {
      items.push(el('li', 'pm-chat__system', 'No messages yet. Say hello.'));
    }

    for (const message of win.messages) {
      const sent = new Date(message.sentUtc || Date.now());
      const key = dayKey(sent);
      if (key !== lastDay) {
        items.push(el('li', 'pm-chat__day', dayLabel(sent, now)));
        lastDay = key;
      }
      const mine = message.senderUserId === this.me;
      const item = el('li', `pm-chat__msg ${mine ? 'is-mine' : 'is-theirs'}`);
      const bubble = el('div', 'pm-chat__bubble', message.body);
      const meta = el('span', 'pm-chat__time');
      if (message.failed) {
        item.classList.add('is-failed');
        meta.textContent = message.error || 'Not sent.';
        const retry = el('button', 'btn btn-link btn-sm p-0 pm-chat__retry', 'Retry');
        retry.type = 'button';
        retry.addEventListener('click', () => this.deliver(win, message));
        meta.append(' ', retry);
      } else if (!message.id) {
        item.classList.add('is-pending');
        meta.textContent = 'Sending…';
      } else {
        meta.textContent = timeFormat.format(sent);
      }
      item.append(bubble, meta);
      items.push(item);
    }

    win.log.replaceChildren(...items);
    win.earlier.hidden = !win.hasMore;
    if (options && options.stickToBottom && (nearBottom || win.messages.length <= HISTORY_PAGE)) {
      body.scrollTop = body.scrollHeight;
    }
  };

  ChatApp.prototype.send = function (win) {
    const text = win.input.value.replace(/\r\n?/g, '\n').trim();
    if (!text) return;
    if (text.length > this.maxLength) return;

    const message = {
      id: 0,
      clientMessageId: newClientId(),
      senderUserId: this.me,
      recipientUserId: win.peerId,
      body: text,
      sentUtc: new Date().toISOString(),
    };
    win.input.value = '';
    win.input.style.height = 'auto';
    win.messages.push(message);
    this.renderMessages(win, { stickToBottom: true });
    win.body.scrollTop = win.body.scrollHeight;
    this.deliver(win, message);
  };

  ChatApp.prototype.deliver = async function (win, message) {
    message.failed = false;
    message.error = null;
    this.renderMessages(win, { stickToBottom: true });
    try {
      const saved = await this.request('POST', '/messages', {
        recipientUserId: win.peerId,
        clientMessageId: message.clientMessageId,
        body: message.body,
      });
      win.messages = mergeMessages(win.messages, [saved]);
    } catch (error) {
      message.failed = true;
      message.error = error.message;
    }
    this.renderMessages(win, { stickToBottom: true });
  };

  // ---------------------------------------------------------------------------

  function boot() {
    const root = document.querySelector('[data-chat-root]');
    if (!root || !root.dataset.currentUserId || typeof fetch !== 'function') return;
    const app = new ChatApp(root);
    global.PMChat = app;
    app.start();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot, { once: true });
  } else {
    boot();
  }
})(window);

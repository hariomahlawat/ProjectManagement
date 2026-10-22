/*
 * PRISM chat: buddy list and docked one-to-one chat windows (bottom-right, Gmail-style), plus
 * the full Messages page (/Chat) when it is on screen.
 *
 * Server contract (Features/Chat/ChatApi.cs, Hubs/ChatHub.cs):
 *   GET  {api}/contacts                         buddy list with presence and unread counts
 *   GET  {api}/with/{peer}/messages?before=&after=&limit=
 *   GET  {api}/search?q=                        message search across the user's conversations
 *   POST {api}/messages                         { recipientUserId, clientMessageId, body }
 *   POST {api}/with/{peer}/read                 { upToMessageId }
 *   hub  in:  ReceiveMessage / ConversationRead / PeerRead / PresenceChanged / Typing
 *   hub  out: Typing(peerUserId), SetAway(bool)  (ephemeral signals only; nothing is stored)
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
  const TYPING_SEND_MS = 2500; // how often "typing" is re-sent while keys keep coming
  const TYPING_SHOW_MS = 6000; // how long "typing…" stays up after the last signal
  const IDLE_MS = 5 * 60 * 1000; // no input for this long in this tab = away
  const IDLE_CHECK_MS = 30000;
  const SEARCH_DELAY_MS = 300;

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

  /** Short timestamp for lists: the time today, otherwise the date. */
  function shortStamp(date, now) {
    return dayKey(date) === dayKey(now) ? timeFormat.format(date) : dayLabel(date, now);
  }

  /** Unread first, then active accounts; online before away before offline; then by name. */
  function sortContacts(contacts) {
    const rank = (c) => (c.isOnline ? (c.isAway ? 1 : 2) : 0);
    return contacts.slice().sort((a, b) =>
      (Number(b.unreadCount > 0) - Number(a.unreadCount > 0))
      || (Number(b.isActive) - Number(a.isActive))
      || (rank(b) - rank(a))
      || a.displayName.localeCompare(b.displayName, undefined, { sensitivity: 'base' }));
  }

  function filterContacts(contacts, term) {
    const needle = (term || '').trim().toLowerCase();
    if (!needle) return contacts;
    return contacts.filter((c) =>
      c.displayName.toLowerCase().includes(needle) || (c.rank || '').toLowerCase().includes(needle));
  }

  /** Conversations for the Messages page: people with history, most recent first. */
  function conversationList(contacts) {
    return contacts
      .filter((c) => c.lastMessageUtc)
      .sort((a, b) => Date.parse(b.lastMessageUtc) - Date.parse(a.lastMessageUtc));
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

  /** Splits text around case-insensitive matches of term: [{ text, match }]. For <mark> rendering. */
  function highlightParts(text, term) {
    const needle = (term || '').trim().toLowerCase();
    if (!needle) return [{ text, match: false }];
    const parts = [];
    const lower = text.toLowerCase();
    let from = 0;
    let at = lower.indexOf(needle, from);
    while (at >= 0) {
      if (at > from) parts.push({ text: text.slice(from, at), match: false });
      parts.push({ text: text.slice(at, at + needle.length), match: true });
      from = at + needle.length;
      at = lower.indexOf(needle, from);
    }
    if (from < text.length) parts.push({ text: text.slice(from), match: false });
    return parts;
  }

  /**
   * Alternates document.title with a notice ("Ravi says…") until stopped, the way old webmail
   * chat drew the eye to a background tab. Restores the page's own title when it stops.
   */
  function TitleFlasher(doc, setTimer, clearTimer) {
    this.doc = doc;
    this.setTimer = setTimer;
    this.clearTimer = clearTimer;
    this.timer = null;
    this.original = null;
  }

  TitleFlasher.prototype.start = function (notice) {
    if (this.timer) this.clearTimer(this.timer);
    if (this.original === null) this.original = this.doc.title;
    let showNotice = true;
    this.doc.title = notice;
    this.timer = this.setTimer(() => {
      showNotice = !showNotice;
      this.doc.title = showNotice ? notice : this.original;
    }, 1200);
  };

  TitleFlasher.prototype.stop = function () {
    if (this.timer) this.clearTimer(this.timer);
    this.timer = null;
    if (this.original !== null) this.doc.title = this.original;
    this.original = null;
  };

  TitleFlasher.prototype.active = function () {
    return this.timer !== null;
  };

  global.PMChatInternals = {
    newClientId, dayLabel, shortStamp, sortContacts, filterContacts, conversationList,
    windowCapacity, mergeMessages, fetchSince, highlightParts, TitleFlasher,
  };

  // ---------------------------------------------------------------------------
  // DOM helpers
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
    const online = !!(contact && contact.isOnline);
    dot.classList.toggle('is-online', online && !contact.isAway);
    dot.classList.toggle('is-away', online && !!contact.isAway);
    dot.classList.toggle('is-inactive', !!(contact && !contact.isActive));
  }

  function statusText(contact) {
    if (!contact) return '';
    if (!contact.isActive) return 'Inactive account';
    if (!contact.isOnline) return 'Offline';
    return contact.isAway ? 'Away' : 'Online';
  }

  function appendHighlighted(node, text, term) {
    for (const part of highlightParts(text, term)) {
      node.append(part.match ? el('mark', null, part.text) : document.createTextNode(part.text));
    }
  }

  // ---------------------------------------------------------------------------
  // Sound: a short two-note chime made with Web Audio, so no audio file has to ship.
  // ---------------------------------------------------------------------------

  function Chime() {
    this.context = null;
  }

  /** Browsers only allow audio after the person has interacted with the page. */
  Chime.prototype.unlock = function () {
    const AudioContext = global.AudioContext || global.webkitAudioContext;
    if (!AudioContext) return;
    try {
      if (!this.context) this.context = new AudioContext();
      if (this.context.state === 'suspended') this.context.resume().catch(() => {});
    } catch (_) {
      this.context = null;
    }
  };

  Chime.prototype.play = function () {
    const ctx = this.context;
    if (!ctx || ctx.state !== 'running') return;
    try {
      const start = ctx.currentTime;
      [[880, 0], [1320, 0.12]].forEach(([frequency, offset]) => {
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.type = 'sine';
        osc.frequency.value = frequency;
        gain.gain.setValueAtTime(0.0001, start + offset);
        gain.gain.exponentialRampToValueAtTime(0.12, start + offset + 0.02);
        gain.gain.exponentialRampToValueAtTime(0.0001, start + offset + 0.25);
        osc.connect(gain).connect(ctx.destination);
        osc.start(start + offset);
        osc.stop(start + offset + 0.3);
      });
    } catch (_) {
      // No sound is better than a broken chat.
    }
  };

  // ---------------------------------------------------------------------------
  // Controller
  // ---------------------------------------------------------------------------

  function ChatApp(root) {
    this.root = root;
    this.me = root.dataset.currentUserId;
    this.api = root.dataset.apiBase.replace(/\/$/, '');
    this.hubUrl = root.dataset.hubUrl;
    this.messagesUrl = root.dataset.messagesUrl || '';
    this.maxLength = Number(root.dataset.maxLength) || 4000;
    this.storageKey = `prism.chat.dock.${this.me}`;
    this.soundKey = `prism.chat.sound.${this.me}`;
    this.dingKey = `prism.chat.ding.${this.me}`;
    this.contacts = new Map();
    this.windows = new Map(); // peerId -> window state (docked, or the one embedded in the Messages page)
    this.order = []; // docked peer ids, most recently opened first
    this.buddyOpen = false;
    this.connection = null;
    this.pollTimer = null;
    this.stopped = false;
    this.away = false;
    this.lastActivity = Date.now();
    this.chime = new Chime();
    this.soundOn = this.readSoundPreference();
    this.title = new TitleFlasher(document, global.setInterval.bind(global), global.clearInterval.bind(global));
    this.page = null;
    this.launchers = Array.from(document.querySelectorAll('[data-chat-launcher]'));
  }

  ChatApp.prototype.start = function () {
    this.buildDock();
    this.launchers.forEach((button) => button.addEventListener('click', () => this.toggleBuddy()));
    global.addEventListener('resize', () => this.enforceCapacity());
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible') {
        this.noteActivity();
        this.windows.forEach((w) => this.maybeMarkRead(w));
      }
    });
    global.addEventListener('focus', () => this.title.stop());
    this.watchActivity();

    const pageNode = document.querySelector('[data-chat-page]');
    if (pageNode) this.page = new MessagesPage(this, pageNode);

    const saved = this.readState();
    this.buddyOpen = !!saved.buddyOpen;
    this.renderBuddyVisibility();

    this.refreshContacts().then(() => {
      (saved.open || []).slice().reverse().forEach((entry) => {
        if (this.contacts.has(entry.id)) this.openWindow(entry.id, { minimized: !!entry.minimized, focus: false, restore: true });
      });
      if (this.page) this.page.start();
    });

    this.connect();
  };

  // ----- persistence (per browser, per user; ids and preferences only) ----------

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

  ChatApp.prototype.readSoundPreference = function () {
    try {
      return global.localStorage.getItem(this.soundKey) !== 'off';
    } catch (_) {
      return true;
    }
  };

  ChatApp.prototype.setSound = function (on) {
    this.soundOn = on;
    try {
      global.localStorage.setItem(this.soundKey, on ? 'on' : 'off');
    } catch (_) { /* per-page only */ }
    if (on) {
      this.chime.unlock();
      this.chime.play();
    }
    this.renderSoundToggle();
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
    if (this.idleTimer) global.clearInterval(this.idleTimer);
    if (this.connection) this.connection.stop().catch(() => {});
    this.title.stop();
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
    connection.on('PeerRead', (read) => this.onPeerRead(read));
    connection.on('PresenceChanged', (presence) => this.onPresence(presence));
    connection.on('Typing', (typing) => this.onTyping(typing));
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

  ChatApp.prototype.isConnected = function () {
    return !!this.connection && this.connection.state === 'Connected';
  };

  /** Fire-and-forget hub call for ephemeral signals; losing one is harmless. */
  ChatApp.prototype.signal = function (method, arg) {
    if (!this.isConnected()) return;
    try {
      const pending = this.connection.invoke(method, arg);
      if (pending && typeof pending.catch === 'function') pending.catch(() => {});
    } catch (_) { /* ignore */ }
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
    // A new connection starts as active on the server; repeat this tab's idle state.
    if (this.away) this.signal('SetAway', true);
    this.refreshContacts();
    this.windows.forEach((w) => this.loadLatest(w));
  };

  // ----- away ------------------------------------------------------------------------

  ChatApp.prototype.watchActivity = function () {
    let last = 0;
    const note = () => {
      const now = Date.now();
      if (now - last < 1000) return; // mousemove fires constantly
      last = now;
      this.noteActivity();
    };
    ['pointerdown', 'keydown', 'mousemove', 'wheel', 'touchstart'].forEach((type) =>
      document.addEventListener(type, note, { passive: true, capture: true }));
    // Audio may only start after a gesture; unlock on the first one.
    const unlock = () => this.chime.unlock();
    ['pointerdown', 'keydown'].forEach((type) => document.addEventListener(type, unlock, { capture: true }));
    this.idleTimer = global.setInterval(() => this.checkIdle(), IDLE_CHECK_MS);
  };

  ChatApp.prototype.noteActivity = function () {
    this.lastActivity = Date.now();
    if (this.away) this.setAway(false);
  };

  ChatApp.prototype.checkIdle = function (now) {
    const idle = (now || Date.now()) - this.lastActivity >= IDLE_MS;
    if (idle !== this.away) this.setAway(idle);
  };

  ChatApp.prototype.setAway = function (away) {
    this.away = away;
    this.signal('SetAway', away);
  };

  // ----- incoming ----------------------------------------------------------------------

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

    let fresh = incoming;
    if (win) {
      const known = win.messages.some((m) => m.id === message.id);
      fresh = incoming && !known;
      if (incoming) win.typingUntil = 0; // the message they were typing has arrived
      // A conversation opened at an old message (from search) shows a slice of history; the
      // newest message is reached with "Show newer messages", so don't paste it below a gap.
      if (!win.hasNewer) {
        win.messages = mergeMessages(win.messages, [message]);
        this.renderMessages(win, { stickToBottom: true });
      } else {
        // Held until "Show newer" reaches the latest page, which may have been read on the
        // server just before this message was saved.
        win.heldLive.push(message);
      }
      if (fresh && !this.isAttended(win)) {
        win.attention = true;
      }
      this.renderWindowState(win);
      if (fresh && contact) contact.unreadCount += 1;
      this.maybeMarkRead(win);
    } else if (incoming && contact) {
      contact.unreadCount += 1;
    }

    if (fresh && !(win && this.isAttended(win))) {
      this.alert(message, contact);
    }

    this.renderContacts();
  };

  /** Sound and title flash for a message the person is not looking at. */
  ChatApp.prototype.alert = function (message, contact) {
    const name = contact ? contact.displayName : 'Someone';
    if (document.visibilityState === 'hidden' || !document.hasFocus()) {
      this.title.start(`${name} says…`);
    }
    if (!this.soundOn) return;
    // Several tabs get the same push; let one chime. A visible tab claims first, a hidden one
    // waits a moment so the visible one wins when both are open.
    const claim = () => {
      try {
        const last = Number(global.localStorage.getItem(this.dingKey) || 0);
        if (last >= message.id) return;
        global.localStorage.setItem(this.dingKey, String(message.id));
      } catch (_) { /* no storage: just play */ }
      this.chime.play();
    };
    if (document.visibilityState === 'hidden') global.setTimeout(claim, 250); else claim();
  };

  ChatApp.prototype.onRead = function (read) {
    const contact = this.contacts.get(read.peerUserId);
    const win = this.windows.get(read.peerUserId);
    if (win) {
      win.lastReadId = Math.max(win.lastReadId || 0, read.upToMessageId);
      win.attention = false;
      this.renderWindowState(win);
    }
    if (contact && win && win.hasNewer) {
      // An older slice (opened from search) doesn't hold the newer messages, so it can't count
      // what is still unread; ask the server instead.
      this.refreshContacts();
    } else if (contact) {
      // Recount from what this tab knows; the next contacts refresh corrects any gap.
      contact.unreadCount = win
        ? win.messages.filter((m) => m.id && m.senderUserId !== this.me && m.id > win.lastReadId).length
        : 0;
    }
    this.renderContacts();
    // Read in another tab: nothing left to call attention to here.
    let total = 0;
    this.contacts.forEach((c) => { total += c.unreadCount; });
    if (total === 0) this.title.stop();
  };

  /** The other person has read up to a message: tick our messages as seen. */
  ChatApp.prototype.onPeerRead = function (read) {
    const win = this.windows.get(read.peerUserId);
    if (!win || read.upToMessageId <= (win.peerReadId || 0)) return;
    win.peerReadId = read.upToMessageId;
    this.renderMessages(win, { stickToBottom: false });
  };

  ChatApp.prototype.onPresence = function (presence) {
    const contact = this.contacts.get(presence.userId);
    if (!contact || (presence.isOnline && !contact.isActive)) {
      // New or reactivated account coming online: its details here are missing or stale.
      if (presence.isOnline && presence.userId !== this.me) this.refreshContacts();
      if (!contact) return;
    }
    contact.isOnline = presence.isOnline;
    contact.isAway = !!presence.isAway;
    this.renderContacts();
    const win = this.windows.get(presence.userId);
    if (win) this.renderWindowState(win);
  };

  ChatApp.prototype.onTyping = function (typing) {
    const win = this.windows.get(typing.userId);
    if (!win) return;
    win.typingUntil = Date.now() + TYPING_SHOW_MS;
    this.renderWindowState(win);
    if (win.typingTimer) global.clearTimeout(win.typingTimer);
    win.typingTimer = global.setTimeout(() => this.renderWindowState(win), TYPING_SHOW_MS + 50);
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
    header.append(title);

    this.soundToggle = iconButton('volume-up', 'Mute message sound');
    this.soundToggle.addEventListener('click', () => this.setSound(!this.soundOn));
    header.append(this.soundToggle);
    this.renderSoundToggle();

    if (this.messagesUrl) {
      const all = el('a', 'pm-chat__icon-btn');
      all.href = this.messagesUrl;
      all.title = 'All messages';
      all.setAttribute('aria-label', 'Open all messages');
      all.append(icon('box-arrow-up-right'));
      header.append(all);
    }

    const close = iconButton('x-lg', 'Close contacts');
    close.addEventListener('click', () => this.toggleBuddy(false));
    header.append(close);

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

  ChatApp.prototype.renderSoundToggle = function () {
    const button = this.soundToggle;
    if (!button) return;
    const label = this.soundOn ? 'Mute message sound' : 'Turn message sound on';
    button.replaceChildren(icon(this.soundOn ? 'volume-up' : 'volume-mute'));
    button.title = label;
    button.setAttribute('aria-label', label);
    button.setAttribute('aria-pressed', String(!this.soundOn));
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
    const online = all.filter((c) => c.isOnline && c.isActive && !c.isAway).length;
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
    if (this.page) this.page.renderConversations();
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
    return total;
  };

  // ----- chat windows --------------------------------------------------------------

  ChatApp.prototype.openWindow = function (peerId, options) {
    const opts = options || {};
    let win = this.windows.get(peerId);
    if (win && win.embedded) {
      // The Messages page holds this conversation (on screen or kept aside): show it there,
      // bring it back into view on a phone, and get the contact sheet out of the way.
      if (opts.focus) {
        if (this.page) this.page.open(peerId, 0);
        if (windowCapacity(global.innerWidth || 1280) === 1 && this.buddyOpen) this.toggleBuddy(false);
        win.input.focus();
        this.maybeMarkRead(win);
      }
      return win;
    }

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
    if (win.typingTimer) global.clearTimeout(win.typingTimer);
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

  /**
   * One conversation view. Docked windows sit in the dock; an embedded one (options.embedded)
   * fills the Messages page and has no minimise/close buttons.
   */
  ChatApp.prototype.createWindow = function (peerId, options) {
    const embedded = !!(options && options.embedded);
    const contact = this.contacts.get(peerId);
    const node = el('section', `pm-chat__window${embedded ? ' is-embedded' : ''}`);
    node.setAttribute('role', embedded ? 'region' : 'dialog');
    if (!embedded) node.setAttribute('aria-modal', 'false');

    const head = el('header', 'pm-chat__window-head');
    const toggle = el(embedded ? 'div' : 'button', 'pm-chat__window-title');
    if (!embedded) toggle.type = 'button';
    const face = avatar(contact);
    const names = el('span', 'pm-chat__window-names');
    const name = el('span', 'pm-chat__window-name', contact ? contact.displayName : 'Chat');
    const status = el('span', 'pm-chat__window-status');
    status.setAttribute('aria-live', 'polite');
    names.append(name, status);
    toggle.append(face, names);
    const minimize = iconButton('dash-lg', 'Minimise');
    const close = iconButton('x-lg', 'Close chat');
    head.append(toggle);
    if (!embedded) head.append(minimize, close);

    const body = el('div', 'pm-chat__window-body');
    const earlier = el('button', 'btn btn-link btn-sm pm-chat__earlier', 'Show earlier messages');
    earlier.type = 'button';
    earlier.hidden = true;
    const log = el('ol', 'pm-chat__log');
    log.setAttribute('role', 'log');
    log.setAttribute('aria-live', 'polite');
    const newer = el('button', 'btn btn-link btn-sm pm-chat__earlier', 'Show newer messages');
    newer.type = 'button';
    newer.hidden = true;
    body.append(earlier, log, newer);

    const notice = el('p', 'pm-chat__notice');
    notice.hidden = true;

    const form = el('form', 'pm-chat__compose');
    const input = el('textarea', 'form-control form-control-sm pm-chat__input');
    input.rows = embedded ? 2 : 1;
    input.maxLength = this.maxLength;
    input.placeholder = 'Type a message';
    input.setAttribute('aria-label', `Message ${contact ? contact.displayName : ''}`.trim());
    const send = iconButton('send', 'Send', 'pm-chat__send');
    send.type = 'submit';
    form.append(input, send);

    node.append(head, body, notice, form);

    const win = {
      peerId, node, face, name, status, body, log, earlier, newer, notice, input, form, minimize, embedded,
      messages: [], hasMore: false, hasNewer: false, historyLoaded: false, syncing: false, syncAgain: false,
      loading: false, minimized: false, attention: false, lastReadId: 0, readSending: 0, peerReadId: 0,
      typingUntil: 0, typingTimer: null, typingSentAt: 0, highlightId: 0, heldLive: [], loadSeq: 0,
    };

    if (!embedded) {
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
      node.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
          win.minimized = true;
          this.renderWindowState(win);
          this.saveState();
        }
      });
    }
    earlier.addEventListener('click', () => this.loadEarlier(win));
    newer.addEventListener('click', () => this.loadNewer(win));
    node.addEventListener('focusin', () => {
      this.title.stop();
      this.maybeMarkRead(win);
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
      this.sendTyping(win);
    });
    form.addEventListener('submit', (event) => {
      event.preventDefault();
      this.send(win);
    });

    return win;
  };

  ChatApp.prototype.sendTyping = function (win) {
    if (!win.input.value.trim()) return;
    const now = Date.now();
    if (now - win.typingSentAt < TYPING_SEND_MS) return;
    win.typingSentAt = now;
    this.signal('Typing', win.peerId);
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
    const typing = win.typingUntil > Date.now();
    win.node.classList.toggle('is-typing', typing);
    win.status.textContent = typing ? 'typing…' : (win.attention ? 'New message' : statusText(contact));

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
    if (contact && !win.hasNewer) {
      contact.unreadCount = 0;
      this.renderContacts();
    }
  };

  ChatApp.prototype.historyPath = function (win, query) {
    return `/with/${encodeURIComponent(win.peerId)}/messages?limit=${HISTORY_PAGE}${query || ''}`;
  };

  ChatApp.prototype.applyPeerRead = function (win, history) {
    if (history && history.peerLastReadMessageId) {
      win.peerReadId = Math.max(win.peerReadId || 0, history.peerLastReadMessageId);
    }
  };

  /**
   * First call: the latest page, which also decides whether "Show earlier messages" is offered
   * (messages pushed while it was in flight don't count as history). Later calls (reconnect,
   * polling): every message since the newest one already shown, so nothing in between is skipped.
   */
  ChatApp.prototype.loadLatest = async function (win) {
    if (win.hasNewer) return; // a slice opened at an old message catches up via "Show newer"
    if (win.syncing) {
      win.syncAgain = true;
      return;
    }
    win.syncing = true;
    const seq = win.loadSeq;
    const path = this.historyPath(win);
    try {
      if (!win.historyLoaded) {
        const history = await this.request('GET', path);
        if (seq !== win.loadSeq) return;
        win.messages = mergeMessages(win.messages, history.messages);
        win.hasMore = history.hasMore;
        win.historyLoaded = true;
        this.applyPeerRead(win, history);
      } else {
        const knownMax = win.messages.reduce((max, m) => (m.id > max ? m.id : max), 0);
        let peerRead = null;
        const result = await fetchSince(
          async (before) => {
            const history = await this.request('GET', before ? `${path}&before=${before}` : path);
            if (!peerRead) peerRead = history;
            return history;
          },
          knownMax,
          CATCH_UP_PAGES);
        if (seq !== win.loadSeq) return;
        if (!result.overlaps) {
          // Too much was missed to stitch together: show the recent stretch and let
          // "Show earlier messages" page back from there instead of leaving a silent gap.
          win.messages = win.messages.filter((m) => !m.id);
          win.hasMore = true;
        }
        win.messages = mergeMessages(win.messages, result.messages);
        this.applyPeerRead(win, peerRead);
      }
      this.renderMessages(win, { stickToBottom: true });
      this.maybeMarkRead(win);
    } catch (_) {
      if (seq === win.loadSeq && !win.messages.length) {
        win.log.replaceChildren(el('li', 'pm-chat__system', 'Messages could not be loaded.'));
      }
    } finally {
      if (seq === win.loadSeq) {
        win.syncing = false;
        if (win.syncAgain) {
          win.syncAgain = false;
          this.loadLatest(win);
        }
      }
    }
  };

  /**
   * Leaves an older slice (opened from search) for the latest messages. Any load still in
   * flight is superseded, so its result cannot overwrite the view; unsent and failed messages
   * are kept.
   */
  ChatApp.prototype.jumpToLatest = function (win) {
    win.loadSeq += 1;
    win.syncing = false;
    win.syncAgain = false;
    win.loading = false;
    win.hasNewer = false;
    win.hasMore = false;
    win.highlightId = 0;
    win.heldLive = []; // the latest page about to load includes them
    win.messages = win.messages.filter((m) => !m.id);
    win.historyLoaded = false;
    this.loadLatest(win);
  };

  /** Opens a conversation at an older message (a search hit) instead of at the latest. */
  ChatApp.prototype.loadAround = async function (win, messageId) {
    win.loadSeq += 1;
    const seq = win.loadSeq;
    win.syncing = true;
    // Paging is locked until the new slice is in: requests still in flight for the previous
    // slice are stale (load sequence), and "Show earlier/newer" must not start from the old
    // slice's cursor while this one loads.
    win.loading = true;
    win.earlier.hidden = true;
    win.newer.hidden = true;
    // Until the slice arrives, treat the view as behind: live pushes are held (onMessage) rather
    // than merged into messages this response is about to replace.
    win.hasNewer = true;
    try {
      const history = await this.request('GET', this.historyPath(win, `&before=${messageId + 1}`));
      if (seq !== win.loadSeq) return; // the person moved on (sent a reply, opened another hit)
      win.messages = mergeMessages(win.messages.filter((m) => !m.id), history.messages);
      win.hasMore = history.hasMore;
      win.hasNewer = history.hasNewer;
      if (!win.hasNewer && win.heldLive.length) {
        // The slice reaches the latest message: what arrived meanwhile follows it.
        win.messages = mergeMessages(win.messages, win.heldLive);
        win.heldLive = [];
      }
      win.historyLoaded = true;
      win.highlightId = messageId;
      this.applyPeerRead(win, history);
      this.renderMessages(win, { scrollToHighlight: true });
      this.maybeMarkRead(win);
    } catch (_) {
      if (seq !== win.loadSeq) return;
      win.loading = false;
      win.hasNewer = false;
      win.messages = mergeMessages(win.messages.filter((m) => !m.id), win.heldLive);
      win.heldLive = [];
      win.log.replaceChildren(el('li', 'pm-chat__system', 'Messages could not be loaded.'));
    } finally {
      if (seq === win.loadSeq) {
        win.syncing = false;
        win.loading = false;
      }
    }
  };

  ChatApp.prototype.loadEarlier = async function (win) {
    const oldest = win.messages.find((m) => m.id);
    if (!oldest || win.loading) return;
    win.loading = true;
    const seq = win.loadSeq;
    const hadFocus = win.node.contains(document.activeElement);
    const before = win.body.scrollHeight - win.body.scrollTop;
    try {
      const history = await this.request('GET', this.historyPath(win, `&before=${oldest.id}`));
      // Another search hit (or a jump to the latest) replaced the view meanwhile: this page
      // belongs to the old one and would leave a gap in the new one.
      if (seq !== win.loadSeq) return;
      win.messages = mergeMessages(win.messages, history.messages);
      win.hasMore = history.hasMore;
      this.applyPeerRead(win, history);
      this.renderMessages(win, { stickToBottom: false });
      win.body.scrollTop = win.body.scrollHeight - before;
      this.keepFocus(win, hadFocus);
    } catch (_) {
      // Leave the button so the person can try again.
    } finally {
      if (seq === win.loadSeq) win.loading = false;
    }
  };

  ChatApp.prototype.loadNewer = async function (win) {
    const newest = win.messages.reduce((max, m) => (m.id > max ? m.id : max), 0);
    if (!newest || win.loading) return;
    win.loading = true;
    const seq = win.loadSeq;
    const hadFocus = win.node.contains(document.activeElement);
    const keep = win.body.scrollTop;
    try {
      const history = await this.request('GET', this.historyPath(win, `&after=${newest}`));
      if (seq !== win.loadSeq) return;
      win.messages = mergeMessages(win.messages, history.messages);
      win.hasNewer = history.hasNewer;
      if (!win.hasNewer && win.heldLive.length) {
        // Caught up: add what arrived live while the slice was behind (duplicates merge away).
        win.messages = mergeMessages(win.messages, win.heldLive);
        win.heldLive = [];
      }
      this.applyPeerRead(win, history);
      this.renderMessages(win, { stickToBottom: false });
      win.body.scrollTop = keep;
      this.keepFocus(win, hadFocus);
      this.maybeMarkRead(win);
    } catch (_) {
      // Leave the button so the person can try again.
    } finally {
      if (seq === win.loadSeq) win.loading = false;
    }
  };

  /**
   * "Show earlier/newer" hides itself when nothing is left to load; if it held focus, focus
   * would fall out of the window and the conversation would stop counting as being read.
   */
  ChatApp.prototype.keepFocus = function (win, hadFocus) {
    const active = document.activeElement;
    const lost = !win.node.contains(active) || (active && active.hidden);
    if (hadFocus && lost && !win.form.hidden) {
      win.input.focus({ preventScroll: true });
    }
  };

  ChatApp.prototype.renderMessages = function (win, options) {
    const body = win.body;
    const nearBottom = body.scrollHeight - body.scrollTop - body.clientHeight < 48;
    const now = new Date();
    const items = [];
    let lastDay = null;
    let highlighted = null;

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
      if (message.id && message.id === win.highlightId) {
        item.classList.add('is-highlight');
        highlighted = item;
      }
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
        if (mine) {
          // Read ticks: one tick once stored, two (coloured) once the other person has read it.
          const seen = message.id <= (win.peerReadId || 0);
          const tick = icon(seen ? 'check2-all' : 'check2');
          tick.classList.add('pm-chat__tick');
          if (seen) tick.classList.add('is-seen');
          tick.removeAttribute('aria-hidden');
          tick.setAttribute('role', 'img');
          tick.setAttribute('aria-label', seen ? 'Seen' : 'Sent');
          tick.title = seen ? 'Seen' : 'Sent';
          meta.append(' ', tick);
        }
      }
      item.append(bubble, meta);
      items.push(item);
    }

    win.log.replaceChildren(...items);
    win.earlier.hidden = !win.hasMore;
    win.newer.hidden = !win.hasNewer;
    if (options && options.scrollToHighlight && highlighted) {
      if (typeof highlighted.scrollIntoView === 'function') highlighted.scrollIntoView({ block: 'center' });
    } else if (options && options.stickToBottom && (nearBottom || win.messages.length <= HISTORY_PAGE)) {
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
    win.typingSentAt = 0;
    win.messages.push(message);
    this.renderMessages(win, { stickToBottom: true });
    win.body.scrollTop = win.body.scrollHeight;
    this.deliver(win, message);
  };

  ChatApp.prototype.deliver = async function (win, message) {
    if (win.hasNewer) {
      // Sending or retrying from an old slice (or while one is still loading): jump to the
      // latest first, so the saved message lands in place instead of below a gap, where it
      // would also become the cursor for "Show newer". Unsent messages, this one included, stay.
      this.jumpToLatest(win);
    }
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
  // Messages page (/Chat): conversation list, message search and one large view.
  // ---------------------------------------------------------------------------

  function MessagesPage(app, node) {
    this.app = app;
    this.node = node;
    this.current = null; // the embedded window on screen
    this.views = new Map(); // peerId -> embedded window, kept when switching so drafts survive
    this.searchTimer = null;
    this.searchSeq = 0;
    this.build();
  }

  MessagesPage.prototype.build = function () {
    const layout = el('div', 'pm-chat-page__layout');

    const side = el('aside', 'pm-chat-page__side');
    side.setAttribute('aria-label', 'Conversations');
    const tools = el('div', 'pm-chat-page__tools');
    const search = el('input', 'form-control');
    search.type = 'search';
    search.placeholder = 'Search messages';
    search.setAttribute('aria-label', 'Search messages');
    search.maxLength = 200;
    search.addEventListener('input', () => this.queueSearch());
    const fresh = el('button', 'btn btn-outline-primary btn-sm pm-chat-page__new');
    fresh.type = 'button';
    fresh.append(icon('pencil-square'), ' New chat');
    fresh.addEventListener('click', () => this.app.toggleBuddy(true));
    tools.append(search, fresh);
    this.searchInput = search;

    this.results = el('div', 'pm-chat-page__results');
    this.results.hidden = true;
    this.results.setAttribute('aria-live', 'polite');

    this.list = el('ul', 'pm-chat-page__list');
    this.list.setAttribute('role', 'list');
    this.listEmpty = el('p', 'pm-chat-page__empty', 'Loading…');

    side.append(tools, this.results, this.list, this.listEmpty);

    const pane = el('div', 'pm-chat-page__pane');
    const back = el('button', 'btn btn-link btn-sm pm-chat-page__back');
    back.type = 'button';
    back.append(icon('arrow-left'), ' All conversations');
    back.addEventListener('click', () => this.layout.classList.remove('has-thread'));
    this.placeholder = el('p', 'pm-chat-page__empty pm-chat-page__placeholder', 'Choose a conversation, or search your messages.');
    pane.append(back, this.placeholder);
    this.pane = pane;

    layout.append(side, pane);
    this.layout = layout;
    this.node.append(layout);
  };

  MessagesPage.prototype.start = function () {
    const withId = this.node.dataset.openWith;
    const at = Number(this.node.dataset.openAt) || 0;
    this.renderConversations();
    if (withId) this.open(withId, at || 0);
  };

  MessagesPage.prototype.renderConversations = function () {
    const conversations = conversationList(Array.from(this.app.contacts.values()));
    const now = new Date();
    this.list.replaceChildren(...conversations.map((contact) => {
      const item = el('li');
      const button = el('button', 'pm-chat-page__item');
      button.type = 'button';
      if (this.current && this.current.peerId === contact.userId) button.setAttribute('aria-current', 'true');
      if (!contact.isActive) button.classList.add('is-inactive');
      const face = avatar(contact);
      setPresence(face, contact);
      const text = el('span', 'pm-chat__contact-text');
      text.append(el('span', 'pm-chat__contact-name', contact.displayName));
      text.append(el('span', 'pm-chat__contact-meta', contact.rank ? `${contact.rank} · ${statusText(contact)}` : statusText(contact)));
      const side = el('span', 'pm-chat-page__item-side');
      side.append(el('span', 'pm-chat-page__stamp', shortStamp(new Date(contact.lastMessageUtc), now)));
      if (contact.unreadCount > 0) {
        side.append(el('span', 'badge rounded-pill pm-chat__unread', contact.unreadCount > 99 ? '99+' : String(contact.unreadCount)));
      }
      button.append(face, text, side);
      button.addEventListener('click', () => this.open(contact.userId, 0));
      item.append(button);
      return item;
    }));
    this.listEmpty.hidden = conversations.length > 0;
    this.listEmpty.textContent = 'No conversations yet. Start one with New chat.';
  };

  /** Shows the conversation with peerId, at a given message or at the latest. */
  MessagesPage.prototype.open = function (peerId, atMessageId) {
    const app = this.app;
    let win = this.views.get(peerId);
    const already = !!win && this.current === win;

    if (this.current && !already) {
      // Set aside, not destroyed: it stays registered, so it keeps receiving messages, and its
      // draft and any unsent or failed messages are there when the person comes back.
      this.current.node.remove();
    }

    let created = false;
    if (!win) {
      // One view per conversation: a docked window for the same person gives way to the page,
      // handing over its draft and unsent messages.
      const docked = app.windows.get(peerId);
      const carry = docked && !docked.embedded
        ? { draft: docked.input.value, unsent: docked.messages.filter((m) => !m.id) }
        : null;
      if (docked) app.closeWindow(peerId);
      win = app.createWindow(peerId, { embedded: true });
      if (carry) {
        win.input.value = carry.draft;
        win.messages = carry.unsent;
      }
      app.windows.set(peerId, win);
      this.views.set(peerId, win);
      created = true;
    }

    this.current = win;
    this.placeholder.hidden = true;
    if (!win.node.isConnected) this.pane.append(win.node);
    this.layout.classList.add('has-thread');
    app.renderWindowState(win);
    this.renderConversations();
    this.updateAddress(peerId, atMessageId);

    if (atMessageId) {
      app.loadAround(win, atMessageId);
    } else if (win.hasNewer) {
      app.jumpToLatest(win); // back from a search slice: a plain open means the latest
    } else if (!already) {
      if (!created) app.renderMessages(win, { stickToBottom: true });
      app.loadLatest(win); // first load, or catch up after being set aside
    }
    win.input.focus();
  };

  MessagesPage.prototype.updateAddress = function (peerId, at) {
    try {
      const url = new URL(global.location.href);
      url.searchParams.set('with', peerId);
      if (at) url.searchParams.set('at', String(at)); else url.searchParams.delete('at');
      global.history.replaceState(null, '', url);
    } catch (_) { /* address bar is cosmetic */ }
  };

  MessagesPage.prototype.queueSearch = function () {
    if (this.searchTimer) global.clearTimeout(this.searchTimer);
    this.searchTimer = global.setTimeout(() => this.runSearch(), SEARCH_DELAY_MS);
  };

  MessagesPage.prototype.runSearch = async function () {
    const term = this.searchInput.value.trim();
    const seq = ++this.searchSeq;
    if (term.length < 2) {
      this.results.hidden = true;
      this.results.replaceChildren();
      this.list.hidden = false;
      this.listEmpty.hidden = this.list.children.length > 0;
      return;
    }

    this.list.hidden = true;
    this.listEmpty.hidden = true;
    this.results.hidden = false;
    this.results.replaceChildren(el('p', 'pm-chat-page__empty', 'Searching…'));
    try {
      const hits = await this.app.request('GET', `/search?q=${encodeURIComponent(term)}`);
      if (seq !== this.searchSeq) return; // a newer search has started
      this.renderResults(hits, term);
    } catch (_) {
      if (seq === this.searchSeq) this.results.replaceChildren(el('p', 'pm-chat-page__empty', 'Search failed. Try again.'));
    }
  };

  MessagesPage.prototype.renderResults = function (hits, term) {
    const now = new Date();
    if (!hits.length) {
      this.results.replaceChildren(el('p', 'pm-chat-page__empty', 'No messages match.'));
      return;
    }
    const list = el('ul', 'pm-chat-page__list');
    list.setAttribute('role', 'list');
    for (const hit of hits) {
      const item = el('li');
      const button = el('button', 'pm-chat-page__hit');
      button.type = 'button';
      const head = el('span', 'pm-chat-page__hit-head');
      head.append(el('span', 'pm-chat__contact-name', hit.peerDisplayName));
      head.append(el('span', 'pm-chat-page__stamp', shortStamp(new Date(hit.sentUtc), now)));
      const snippet = el('span', 'pm-chat-page__snippet');
      if (hit.senderUserId === this.app.me) snippet.append(el('span', 'pm-chat-page__you', 'You: '));
      appendHighlighted(snippet, hit.body, term);
      button.append(head, snippet);
      button.addEventListener('click', () => this.open(hit.peerUserId, hit.messageId));
      item.append(button);
      list.append(item);
    }
    const count = el('p', 'pm-chat-page__count', hits.length >= 50 ? 'Showing the 50 most recent matches.' : `${hits.length} ${hits.length === 1 ? 'match' : 'matches'}`);
    this.results.replaceChildren(count, list);
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

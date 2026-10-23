/*
 * PRISM chat: buddy list and docked one-to-one chat windows (bottom-right, Gmail-style), plus
 * the full Messages page (/Chat) when it is on screen.
 *
 * Server contract (Features/Chat/ChatApi.cs, Hubs/ChatHub.cs):
 *   GET  {api}/contacts                         buddy list with presence and unread counts
 *   GET  {api}/with/{peer}/messages?before=&after=&limit=
 *   GET  {api}/search?q=&groups=true            message search across the user's conversations
 *   POST {api}/messages                         { recipientUserId, clientMessageId, body }
 *   POST {api}/with/{peer}/read                 { upToMessageId }
 *   GET/POST {api}/status                       { message } (own status message)
 *   POST {api}/messages/{id}/unsend             own message, shortly after sending
 *   GET  {api}/unsent?since=                    ids unsent while this page was not listening
 *   GET/POST {api}/groups, GET {api}/groups/{id}, …/messages, …/read, …/members,
 *            …/members/{user}/remove, …/leave, …/rename      group chats (410 = not in the group)
 *   hub  in:  ReceiveMessage / ConversationRead / PeerRead / PresenceChanged / Typing /
 *             MessageUnsent / StatusChanged / ReceiveGroupMessage / GroupMessageUnsent /
 *             GroupChanged / GroupRemoved / GroupRead / GroupTyping
 *   hub  out: also TypingInGroup(groupId)
 *
 * Conversations are keyed by the other person's user id (direct) or "g:{groupId}" (group).
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
  const UNSENT_RETRY_MS = 15000;
  const GROUP_LIMIT = 50; // people in a group, the creator included (ChatConversation.MaxGroupMembers)
  const SERVER_TIMEOUT_MS = 90000; // how long a silent drop can go unnoticed
  // Catch-up looks back this far before the moment a drop was noticed: the whole detection
  // window, plus a minute for clock differences between browser and server.
  const UNSENT_LOOKBACK_MS = SERVER_TIMEOUT_MS + 60000;

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
      // An unsend is final: a stale copy (an older history page, a late send response) must
      // never bring the text back.
      const known = byId.get(m.id);
      byId.set(m.id, known && known.isUnsent && !m.isUnsent ? known : m);
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

  const GROUP_PREFIX = 'g:';
  let panelSerial = 0; // ids for aria-controls

  function groupKey(id) {
    return `${GROUP_PREFIX}${id}`;
  }

  /**
   * The group id in a conversation key, or 0 for a direct conversation or a malformed group key
   * (anything but a positive whole number, e.g. from a hand-edited ?with= link).
   */
  function groupIdOf(key) {
    if (typeof key !== 'string' || !key.startsWith(GROUP_PREFIX)) return 0;
    const text = key.slice(GROUP_PREFIX.length);
    const id = /^\d{1,16}$/.test(text) ? Number(text) : 0;
    return Number.isSafeInteger(id) && id > 0 ? id : 0;
  }

  /** One spelling per conversation ("g:05" and "g:5" are the same group); '' when malformed. */
  function conversationKey(key) {
    if (typeof key !== 'string' || !key) return '';
    if (!key.startsWith(GROUP_PREFIX)) return key;
    const id = groupIdOf(key);
    return id ? groupKey(id) : '';
  }

  /**
   * The other members who could see a group message: those who had joined by the time it was
   * sent. Someone added later never sees it, however far their read marker has moved.
   */
  function couldSee(members, me, message) {
    const sent = Date.parse(message.sentUtc) || 0;
    return (members || []).filter((m) => m.userId !== me && !(m.joinedUtc && Date.parse(m.joinedUtc) > sent));
  }

  /** The other members who have read a group message ("Seen by"). */
  function seenBy(members, me, message) {
    return couldSee(members, me, message).filter((m) => (m.lastReadMessageId || 0) >= message.id);
  }

  /**
   * Whether a group message counts as seen (two ticks): every other member who could see it has
   * read it. Members with inactive accounts never will, so they do not hold the ticks back.
   */
  function seenByEveryone(members, me, message) {
    const expected = couldSee(members, me, message).filter((m) => m.isActive !== false);
    return expected.length > 0 && expected.every((m) => (m.lastReadMessageId || 0) >= message.id);
  }

  function peopleCount(n) {
    return `${n} ${n === 1 ? 'person' : 'people'}`;
  }

  /** "Ravi is typing…", "Ravi and Sita are typing…", "3 people are typing…". */
  function typingLine(names) {
    if (!names.length) return '';
    if (names.length === 1) return `${names[0]} is typing…`;
    if (names.length === 2) return `${names[0]} and ${names[1]} are typing…`;
    return `${names.length} people are typing…`;
  }

  global.PMChatInternals = {
    groupKey, groupIdOf, conversationKey, seenBy, seenByEveryone, typingLine, peopleCount,
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

  /**
   * Lists and logs are redrawn whole on every update (presence, unread counts, read ticks), which
   * would drop keyboard focus. Controls that can hold focus carry data-focus-key; this is the key
   * of the one that has focus inside container, or '' when focus is elsewhere.
   */
  function focusKeyIn(container) {
    const active = document.activeElement;
    return active && active !== container && container.contains(active) && active.dataset
      ? active.dataset.focusKey || ''
      : '';
  }

  /** After a redraw, focus the control with that key again; false when it is gone. */
  function refocus(container, key) {
    if (!key) return false;
    const again = Array.from(container.querySelectorAll('[data-focus-key]')).find((n) => n.dataset.focusKey === key);
    if (!again) return false;
    again.focus({ preventScroll: true });
    return true;
  }

  /** Writes text only when it changed, so a live region does not announce the same words again. */
  function setText(node, text) {
    if (node.textContent !== text) node.textContent = text;
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

  function groupAvatar(title) {
    const wrap = el('span', 'pm-chat__avatar is-group');
    wrap.append(icon('people-fill'));
    wrap.title = title || '';
    return wrap;
  }

  function setPresence(avatarNode, contact) {
    if (avatarNode.classList.contains('is-group')) return;
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
    this.unsendMs = (Number(root.dataset.unsendMinutes) || 0) * 60000;
    this.unsentIds = new Set(); // every unsend seen by this page, applied to anything loaded later
    // When live events stopped arriving (or, until the first connection, when this page started
    // loading history), so unsends in between can be caught up on once connected.
    this.offlineSince = new Date();
    this.offlineGeneration = 0; // bumped on every drop, so a catch-up can tell it was overtaken
    this.myStatus = null;
    this.storageKey = `prism.chat.dock.${this.me}`;
    this.soundKey = `prism.chat.sound.${this.me}`;
    this.dingKey = `prism.chat.ding.${this.me}`;
    this.contacts = new Map();
    this.groups = new Map(); // groupId -> { id, title, managerUserId, memberCount, unreadCount, lastMessageUtc }
    this.windows = new Map(); // conversation key -> window state (docked, or embedded in the Messages page)
    this.order = []; // docked peer ids, most recently opened first
    // Drafts and failed messages of closed windows, until the conversation is opened again. In
    // memory only: like the rest of chat, message text is never written to browser storage.
    this.kept = new Map();
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

    this.attachPersonButtons(document);
    if (typeof global.MutationObserver === 'function' && document.body) {
      let pending = null;
      new global.MutationObserver(() => {
        if (pending) return;
        pending = global.setTimeout(() => { pending = null; this.attachPersonButtons(document); }, 200);
      }).observe(document.body, { childList: true, subtree: true });
    }
    this.loadMyStatus();

    const pageNode = document.querySelector('[data-chat-page]');
    if (pageNode) this.page = new MessagesPage(this, pageNode);

    const saved = this.readState();
    this.buddyOpen = !!saved.buddyOpen;
    this.renderBuddyVisibility();

    Promise.all([this.refreshContacts(), this.refreshGroups()]).then(() => {
      (saved.open || []).slice().reverse().forEach((entry) => {
        const gid = groupIdOf(entry.id);
        const known = gid ? this.groups.has(gid) : this.contacts.has(entry.id);
        if (known) this.openWindow(entry.id, { minimized: !!entry.minimized, focus: false, restore: true });
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

    if (response.status === 401 || (response.status === 404 && response.headers && response.headers.get('X-Chat-Disabled'))) {
      // Signed out, or chat switched off on the server: stand down quietly. Any other 404 (a
      // conversation that no longer exists, a bad link) is an ordinary error for this request.
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
    if (this.unsentRetry) global.clearTimeout(this.unsentRetry);
    if (this.connection) this.connection.stop().catch(() => {});
    this.title.stop();
    document.querySelectorAll('.pm-chat-person').forEach((button) => button.remove());
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
    connection.serverTimeoutInMilliseconds = SERVER_TIMEOUT_MS;
    connection.on('ReceiveMessage', (message) => this.onMessage(message));
    connection.on('ConversationRead', (read) => this.onRead(read));
    connection.on('PeerRead', (read) => this.onPeerRead(read));
    connection.on('PresenceChanged', (presence) => this.onPresence(presence));
    connection.on('Typing', (typing) => this.onTyping(typing));
    connection.on('MessageUnsent', (message) => this.onUnsent(message));
    connection.on('StatusChanged', (status) => this.onStatus(status));
    // Group messages come on their own events, so pages from before group chats never see them.
    connection.on('ReceiveGroupMessage', (message) => this.onMessage(message));
    connection.on('GroupMessageUnsent', (message) => this.onUnsent(message));
    connection.on('GroupChanged', (group) => this.onGroupChanged(group));
    connection.on('GroupRemoved', (groupId) => this.onGroupRemoved(groupId));
    connection.on('GroupRead', (read) => this.onGroupRead(read));
    connection.on('GroupTyping', (typing) => this.onGroupTyping(typing));
    connection.onreconnecting(() => {
      this.markOffline();
      this.startPolling();
    });
    connection.onreconnected(() => this.resync());
    connection.onclose(() => {
      if (this.stopped) return;
      this.markOffline();
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

  ChatApp.prototype.markOffline = function () {
    this.offlineGeneration += 1;
    if (!this.offlineSince) this.offlineSince = new Date();
  };

  /** While the live connection is down, keep badges and open windows roughly current. */
  ChatApp.prototype.startPolling = function () {
    if (this.pollTimer || this.stopped) return;
    this.markOffline();
    this.pollTimer = global.setInterval(() => {
      this.refreshContacts();
      this.refreshGroups();
      this.windows.forEach((w) => this.loadLatest(w));
      this.syncUnsent();
    }, POLL_MS);
  };

  /**
   * Unsends that happened while no live event could arrive: history catch-up only fetches newer
   * messages, so an already-loaded message that was unsent meanwhile would keep its text.
   */
  ChatApp.prototype.syncUnsent = async function (done) {
    if (!this.offlineSince || this.stopped) return;
    if (this.unsentRetry) {
      global.clearTimeout(this.unsentRetry);
      this.unsentRetry = null;
    }
    const generation = this.offlineGeneration;
    const since = new Date(this.offlineSince.getTime() - UNSENT_LOOKBACK_MS);
    const base = `/unsent?since=${encodeURIComponent(since.toISOString())}`;
    try {
      // Read every page: the watermark is only cleared once nothing was skipped.
      const ids = [];
      let before = null;
      for (let guard = 0; guard < 200; guard += 1) {
        const page = await this.request('GET', before ? `${base}&before=${before}` : base);
        ids.push(...page.ids);
        if (!page.hasMore || !page.ids.length) break;
        before = Math.min(...page.ids);
      }
      ids.forEach((id) => this.unsentIds.add(id));
      if (ids.length) {
        this.windows.forEach((w) => this.renderMessages(w, { stickToBottom: false }));
        if (this.page) this.page.refreshResults();
      }
      // Live again: events cover the rest, unless the connection dropped again while this ran
      // (that outage still needs its own catch-up).
      const stillLive = !this.connection || this.isConnected();
      if (done && generation === this.offlineGeneration && stillLive) this.offlineSince = null;
    } catch (_) {
      // While polling, the next poll tries again. Once live again nothing else would, so keep
      // retrying until the catch-up succeeds (one timer at a time).
      if (done && !this.stopped) {
        this.unsentRetry = global.setTimeout(() => this.syncUnsent(true), UNSENT_RETRY_MS);
      }
    }
  };

  ChatApp.prototype.resync = function () {
    if (this.pollTimer) {
      global.clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
    // A new connection starts as active on the server; repeat this tab's idle state.
    if (this.away) this.signal('SetAway', true);
    this.syncUnsent(true);
    this.refreshContacts();
    this.refreshGroups();
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

  /** The conversation a message belongs to: "g:{id}" for a group, else the other person's id. */
  ChatApp.prototype.keyOfMessage = function (message) {
    if (message.groupId) return groupKey(message.groupId);
    return message.senderUserId === this.me ? message.recipientUserId : message.senderUserId;
  };

  ChatApp.prototype.onMessage = function (message) {
    const key = this.keyOfMessage(message);
    const incoming = message.senderUserId !== this.me;
    // Group notices ("Ravi added Sita") are shown but neither counted nor announced.
    const countable = incoming && !message.eventKind;
    const group = message.groupId ? this.groups.get(message.groupId) : null;
    const contact = message.groupId ? null : this.contacts.get(key);
    if (message.groupId) {
      if (group) group.lastMessageUtc = message.sentUtc;
      else this.refreshGroups(); // a group this tab does not know yet (just added to it)
    } else {
      if (contact) {
        contact.lastMessageUtc = message.sentUtc;
      }
      if (!contact || (incoming && !contact.isActive)) {
        // Someone this tab has not seen yet, or still holds as inactive, is writing: the account
        // was created or reactivated since. Fetch their details so the window shows their name
        // and lets the user reply. The refresh replaces unread counts with the server's.
        this.refreshContacts();
      }
    }
    const counter = group || contact;

    let win = this.windows.get(key);
    if (!win && countable) {
      // Classic behaviour: a new message pops its conversation open.
      win = this.openWindow(key, { focus: false, pop: true });
    }

    let fresh = countable;
    if (win) {
      const known = win.messages.some((m) => m.id === message.id);
      fresh = countable && !known;
      if (incoming) {
        // The message they were typing has arrived.
        win.typingUntil = 0;
        win.typers.delete(message.senderUserId);
      }
      // A conversation opened at an old message (from search) shows a slice of history; the
      // newest message is reached with "Show newer messages", so don't paste it below a gap.
      this.acceptSaved(win, [message]);
      if (fresh && !this.isAttended(win)) {
        win.attention = true;
      }
      this.renderWindowState(win);
      if (fresh && counter) counter.unreadCount += 1;
      this.maybeMarkRead(win);
    } else if (countable && counter) {
      counter.unreadCount += 1;
    }

    if (fresh && !(win && this.isAttended(win))) {
      this.alert(message, contact, group);
    }
    if (fresh) this.announceMessage(message, win, group);

    this.renderContacts();
  };

  /**
   * Tells a screen reader about a new message once: the text when the person is in that
   * conversation, otherwise who it is from (the conversation logs are not live regions).
   */
  ChatApp.prototype.announceMessage = function (message, win, group) {
    const who = this.nameOf(message.senderUserId, win);
    const from = group ? `${who} in ${group.title}` : who;
    if (win && this.isAttended(win)) {
      const text = message.body.length > 200 ? `${message.body.slice(0, 200)}…` : message.body;
      this.announce(`${from}: ${text}`);
    } else {
      this.announce(`New message from ${from}`);
    }
  };

  /** Announces messages that came in through a fetch: one is read like a pushed message. */
  ChatApp.prototype.announceFetched = function (win, messages) {
    const incoming = messages.filter((m) => m.senderUserId !== this.me && !m.eventKind && !m.isUnsent);
    if (!incoming.length) return;
    const group = win.groupId ? this.groups.get(win.groupId) : null;
    if (incoming.length === 1) {
      this.announceMessage(incoming[0], win, group);
      return;
    }
    const where = group ? `in ${group.title}` : `from ${this.nameOf(win.peerId, win)}`;
    this.announce(`${incoming.length} new messages ${where}`);
  };

  /** Sound and title flash for a message the person is not looking at. */
  ChatApp.prototype.alert = function (message, contact, group) {
    const name = group
      ? `${this.nameOf(message.senderUserId)} in ${group.title}`
      : (contact ? contact.displayName : 'Someone');
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
    if (this.unreadTotal() === 0) this.title.stop();
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

  /** A message was unsent (by this user in another tab, or by the other person). */
  ChatApp.prototype.onUnsent = function (message) {
    this.unsentIds.add(message.id); // also for a window opened later, or one still loading
    const win = this.windows.get(this.keyOfMessage(message));
    if (win) this.applyUnsent(win, message);
    if (this.page) this.page.refreshResults();
    // An unread message that was unsent no longer counts; the server has the exact number.
    if (message.senderUserId !== this.me) {
      if (message.groupId) this.refreshGroups(); else this.refreshContacts();
    }
  };

  ChatApp.prototype.applyUnsent = function (win, message) {
    this.unsentIds.add(message.id);
    this.renderMessages(win, { stickToBottom: false });
  };

  /**
   * Turns any message this page knows was unsent into its placeholder. Runs before every render,
   * so a history page or send response that was already in flight when the unsend arrived
   * cannot show the withdrawn text.
   */
  ChatApp.prototype.applyTombstones = function (win) {
    if (!this.unsentIds.size) return;
    const mark = (list) => list.map((m) => (m.id && this.unsentIds.has(m.id) && !m.isUnsent ? { ...m, body: '', isUnsent: true } : m));
    win.messages = mark(win.messages);
    win.heldLive = mark(win.heldLive);
  };

  ChatApp.prototype.onStatus = function (status) {
    if (status.userId === this.me) {
      this.myStatus = status.message || null;
      this.renderMyStatus();
      return;
    }
    const contact = this.contacts.get(status.userId);
    if (!contact) return;
    contact.statusMessage = status.message || null;
    this.renderContacts();
    const win = this.windows.get(status.userId);
    if (win) this.renderWindowState(win);
  };

  // ----- groups ------------------------------------------------------------------------

  /** Overlapping calls share one request, plus at most one follow-up (like refreshContacts). */
  ChatApp.prototype.refreshGroups = function () {
    if (this.groupsRequest) {
      this.groupsAgain = true;
      return this.groupsRequest;
    }
    this.groupsRequest = this.loadGroups().finally(() => {
      this.groupsRequest = null;
      if (this.groupsAgain) {
        this.groupsAgain = false;
        this.refreshGroups();
      }
    });
    return this.groupsRequest;
  };

  ChatApp.prototype.loadGroups = async function () {
    if (this.stopped) return;
    try {
      const groups = await this.request('GET', '/groups');
      this.groups = new Map(groups.map((g) => [g.id, g]));
      this.windows.forEach((w) => {
        if (!w.groupId) return;
        const g = this.groups.get(w.groupId);
        w.removed = !g;
        if (g && g.unreadCount > 0 && !this.isAttended(w)) w.attention = true;
        this.renderWindowState(w);
      });
      this.renderContacts();
    } catch (_) { /* the list keeps what it had */ }
  };

  /** A group this user is in was created or changed: name, manager, members. */
  ChatApp.prototype.onGroupChanged = function (group) {
    const summary = this.groups.get(group.id) || { id: group.id, unreadCount: 0, lastMessageUtc: null };
    summary.title = group.title;
    summary.managerUserId = group.managerUserId;
    summary.memberCount = group.members.length;
    this.groups.set(group.id, summary);
    const win = this.windows.get(groupKey(group.id));
    if (win) {
      const readded = win.removed;
      win.members = group.members;
      win.removed = false;
      this.renderWindowState(win);
      if (readded) {
        // Back in the group: it now starts from this new membership, so reload it from there,
        // resetting all paging (the window may have been showing an older search slice).
        this.jumpToLatest(win);
      } else {
        this.renderMessages(win, { stickToBottom: false });
      }
      if (win.panelOpen) this.renderMembersPanel(win);
    }
    this.renderContacts();
  };

  /** Left, or removed: the group is gone for this user, its window keeps what it showed. */
  ChatApp.prototype.onGroupRemoved = function (groupId) {
    this.groups.delete(groupId);
    const win = this.windows.get(groupKey(groupId));
    if (win) {
      win.removed = true;
      win.panelOpen = false;
      win.panelState = null;
      this.renderWindowState(win);
    }
    this.renderContacts();
  };

  ChatApp.prototype.onGroupRead = function (read) {
    const win = this.windows.get(groupKey(read.groupId));
    const group = this.groups.get(read.groupId);
    if (win) {
      const member = (win.members || []).find((m) => m.userId === read.userId);
      if (member) member.lastReadMessageId = Math.max(member.lastReadMessageId || 0, read.upToMessageId);
    }
    if (read.userId === this.me) {
      // Read in another tab of this user.
      if (win) {
        win.lastReadId = Math.max(win.lastReadId || 0, read.upToMessageId);
        win.attention = false;
        this.renderWindowState(win);
      }
      if (group && win && win.hasNewer) {
        this.refreshGroups();
      } else if (group) {
        group.unreadCount = win
          ? win.messages.filter((m) => m.id && m.senderUserId !== this.me && !m.eventKind && m.id > win.lastReadId).length
          : 0;
      }
      this.renderContacts();
      if (this.unreadTotal() === 0) this.title.stop();
    }
    if (win) this.renderMessages(win, { stickToBottom: false });
  };

  ChatApp.prototype.onGroupTyping = function (typing) {
    const win = this.windows.get(groupKey(typing.groupId));
    if (!win) return;
    win.typers.set(typing.userId, Date.now() + TYPING_SHOW_MS);
    this.renderWindowState(win);
    if (win.typingTimer) global.clearTimeout(win.typingTimer);
    win.typingTimer = global.setTimeout(() => this.renderWindowState(win), TYPING_SHOW_MS + 50);
  };

  /** A person's name as this page knows it: "You", a contact, or a member of the window's group. */
  ChatApp.prototype.nameOf = function (userId, win) {
    if (userId === this.me) return 'You';
    const contact = this.contacts.get(userId);
    if (contact) return contact.displayName;
    const member = win && (win.members || []).find((m) => m.userId === userId);
    return member ? member.displayName : 'Someone';
  };

  ChatApp.prototype.describeEvent = function (win, message) {
    const actor = this.nameOf(message.senderUserId, win);
    switch (message.eventKind) {
      case 'Created': return `${actor} created the group “${message.eventData}”`;
      case 'Added': return `${actor} added ${this.nameOf(message.eventData, win)}`;
      case 'Removed': return `${actor} removed ${this.nameOf(message.eventData, win)}`;
      case 'Left': return `${actor} left the group`;
      case 'Renamed': return `${actor} renamed the group to “${message.eventData}”`;
      default: return '';
    }
  };

  ChatApp.prototype.unreadTotal = function () {
    let total = 0;
    this.contacts.forEach((c) => { total += c.unreadCount; });
    this.groups.forEach((g) => { total += g.unreadCount || 0; });
    return total;
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
    close.addEventListener('click', () => {
      this.toggleBuddy(false);
      this.focusLauncher();
    });
    header.append(close);

    const statusWrap = el('form', 'pm-chat__mystatus');
    const statusInput = el('input', 'form-control form-control-sm');
    statusInput.type = 'text';
    statusInput.maxLength = 140;
    statusInput.placeholder = 'Set a status message';
    statusInput.setAttribute('aria-label', 'Your status message, shown to everyone');
    statusWrap.append(statusInput);
    statusWrap.addEventListener('submit', (event) => {
      event.preventDefault();
      this.saveMyStatus();
      statusInput.blur();
    });
    statusInput.addEventListener('blur', () => this.saveMyStatus());
    statusInput.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        event.stopPropagation(); // cancels the edit; a second Esc closes the contact list
        statusInput.value = this.myStatus || '';
        statusInput.blur();
        search.focus();
      }
    });
    this.statusInput = statusInput;

    const searchWrap = el('div', 'pm-chat__search');
    const search = el('input', 'form-control form-control-sm');
    search.type = 'search';
    search.placeholder = 'Search people';
    search.setAttribute('aria-label', 'Search people');
    search.addEventListener('input', () => this.renderContacts());
    searchWrap.append(search);
    this.search = search;

    const newGroup = el('button', 'btn btn-link btn-sm pm-chat__new-group');
    newGroup.type = 'button';
    newGroup.append(icon('people'), ' New group');
    newGroup.addEventListener('click', () => this.openGroupForm());
    searchWrap.append(newGroup);

    this.groupForm = el('form', 'pm-chat__group-form');
    this.groupForm.hidden = true;

    const scroller = el('div', 'pm-chat__scroller');
    this.groupHeading = el('h3', 'pm-chat__section', 'Groups');
    this.groupList = el('ul', 'pm-chat__contacts pm-chat__groups');
    this.groupList.setAttribute('role', 'list');
    this.peopleHeading = el('h3', 'pm-chat__section', 'People');
    this.list = el('ul', 'pm-chat__contacts');
    this.list.setAttribute('role', 'list');
    this.emptyNote = el('p', 'pm-chat__empty', 'Loading…');
    scroller.append(this.groupHeading, this.groupList, this.peopleHeading, this.list, this.emptyNote);

    buddy.append(header, statusWrap, searchWrap, this.groupForm, scroller);
    this.buddy = buddy;
    this.newGroupButton = newGroup;

    // Esc steps back one level: clears the search, closes the New group form, then the list.
    buddy.addEventListener('keydown', (event) => {
      if (event.key !== 'Escape') return;
      if (event.target === search && search.value) {
        event.preventDefault();
        search.value = '';
        this.renderContacts();
        return;
      }
      if (!this.groupForm.hidden && this.groupForm.contains(event.target)) {
        this.groupForm.hidden = true;
        newGroup.focus();
        return;
      }
      this.toggleBuddy(false);
      this.focusLauncher();
    });

    // One polite live region for things a screen reader user would otherwise miss: a message
    // arriving, or an error in a form. The conversation logs themselves are not live regions,
    // because they are redrawn whole and would be read out again each time.
    this.announcer = el('div', 'visually-hidden');
    this.announcer.setAttribute('aria-live', 'polite');
    this.announcer.setAttribute('aria-atomic', 'true');

    this.windowRow = el('div', 'pm-chat__windows');
    dock.append(this.windowRow, buddy);
    this.root.append(dock, this.announcer);
  };

  /** Says text through the live region (cleared first, so the same words are read again). */
  ChatApp.prototype.announce = function (text) {
    if (!this.announcer || !text) return;
    this.announcer.textContent = '';
    global.setTimeout(() => { this.announcer.textContent = text; }, 30);
  };

  /** Back to the chat button in the top bar, when focus would otherwise fall to the page. */
  ChatApp.prototype.focusLauncher = function () {
    const launcher = this.launchers.find((b) => !b.hidden && b.offsetParent !== null) || this.launchers[0];
    if (launcher) launcher.focus();
  };

  ChatApp.prototype.loadMyStatus = async function () {
    try {
      const status = await this.request('GET', '/status');
      this.myStatus = (status && status.message) || null;
      this.renderMyStatus();
    } catch (_) { /* the box just stays empty */ }
  };

  ChatApp.prototype.renderMyStatus = function () {
    if (this.statusInput && document.activeElement !== this.statusInput) {
      this.statusInput.value = this.myStatus || '';
    }
  };

  ChatApp.prototype.saveMyStatus = async function () {
    const text = this.statusInput.value.replace(/\s+/g, ' ').trim();
    if (text === (this.myStatus || '')) return;
    try {
      const status = await this.request('POST', '/status', { message: text });
      this.myStatus = (status && status.message) || null;
    } catch (_) {
      // Leave what was typed; the next blur or Enter tries again.
      return;
    }
    this.renderMyStatus();
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

    const focused = focusKeyIn(this.list);
    this.list.replaceChildren(...shown.map((contact) => {
      const item = el('li');
      const button = el('button', 'pm-chat__contact');
      button.type = 'button';
      button.dataset.focusKey = `contact:${contact.userId}`;
      if (!contact.isActive) button.classList.add('is-inactive');
      const face = avatar(contact);
      setPresence(face, contact);
      const text = el('span', 'pm-chat__contact-text');
      text.append(el('span', 'pm-chat__contact-name', contact.displayName));
      text.append(el('span', 'pm-chat__contact-meta', contact.rank ? `${contact.rank} · ${statusText(contact)}` : statusText(contact)));
      if (contact.statusMessage) text.append(el('span', 'pm-chat__contact-note', contact.statusMessage));
      button.append(face, text);
      if (contact.unreadCount > 0) {
        button.append(el('span', 'badge rounded-pill pm-chat__unread', contact.unreadCount > 99 ? '99+' : String(contact.unreadCount)));
      }
      button.setAttribute('aria-label', `${contact.displayName}, ${statusText(contact)}${contact.statusMessage ? `, ${contact.statusMessage}` : ''}${contact.unreadCount ? `, ${contact.unreadCount} unread` : ''}`);
      button.addEventListener('click', () => this.openWindow(contact.userId, { focus: true }));
      item.append(button);
      return item;
    }));
    refocus(this.list, focused);

    this.emptyNote.hidden = shown.length > 0;
    this.emptyNote.textContent = all.length ? 'No one matches that search.' : 'No other users yet.';
    this.renderGroupList();
    this.renderLauncherBadge();
    if (this.page) this.page.renderConversations();
  };

  ChatApp.prototype.sortedGroups = function () {
    return Array.from(this.groups.values()).sort((a, b) =>
      (Number((b.unreadCount || 0) > 0) - Number((a.unreadCount || 0) > 0))
      || ((Date.parse(b.lastMessageUtc) || 0) - (Date.parse(a.lastMessageUtc) || 0))
      || a.title.localeCompare(b.title, undefined, { sensitivity: 'base' }));
  };

  ChatApp.prototype.renderGroupList = function () {
    const needle = (this.search.value || '').trim().toLowerCase();
    const groups = this.sortedGroups().filter((g) => !needle || g.title.toLowerCase().includes(needle));
    const focused = focusKeyIn(this.groupList);
    this.groupList.replaceChildren(...groups.map((group) => {
      const item = el('li');
      const button = el('button', 'pm-chat__contact');
      button.type = 'button';
      button.dataset.focusKey = `group:${group.id}`;
      const text = el('span', 'pm-chat__contact-text');
      text.append(el('span', 'pm-chat__contact-name', group.title));
      text.append(el('span', 'pm-chat__contact-meta', peopleCount(group.memberCount)));
      button.append(groupAvatar(group.title), text);
      if (group.unreadCount > 0) {
        button.append(el('span', 'badge rounded-pill pm-chat__unread', group.unreadCount > 99 ? '99+' : String(group.unreadCount)));
      }
      button.setAttribute('aria-label', `Group ${group.title}, ${peopleCount(group.memberCount)}${group.unreadCount ? `, ${group.unreadCount} unread` : ''}`);
      button.addEventListener('click', () => this.openWindow(groupKey(group.id), { focus: true }));
      item.append(button);
      return item;
    }));
    refocus(this.groupList, focused);
    this.groupHeading.hidden = groups.length === 0;
    this.peopleHeading.hidden = groups.length === 0;
  };

  /**
   * People picker shared by "New group" and "Add people": active contacts, filtered by a search
   * box, as checkboxes. Returns { node, selected(), focus() }. state ({ chosen: Set, filter:
   * string }), when given, holds the ticks and the search text, so a redraw of the surrounding
   * panel does not lose them.
   */
  ChatApp.prototype.peoplePicker = function (exclude, limit, state) {
    const node = el('div', 'pm-chat__picker');
    const filter = el('input', 'form-control form-control-sm');
    filter.type = 'search';
    filter.placeholder = 'Find people';
    filter.setAttribute('aria-label', 'Find people to add');
    filter.dataset.focusKey = 'filter';
    const list = el('ul', 'pm-chat__picker-list');
    list.setAttribute('role', 'group');
    list.setAttribute('aria-label', 'People');
    const chosen = state ? state.chosen : new Set();
    // People who joined meanwhile (someone else added them) are no longer choices.
    exclude.forEach((id) => chosen.delete(id));
    if (state) filter.value = state.filter || '';
    const note = el('p', 'pm-chat__picker-note');
    note.setAttribute('aria-live', 'polite'); // "3 selected", and the 50-people limit
    const draw = () => {
      const needle = filter.value.trim().toLowerCase();
      const people = sortContacts(Array.from(this.contacts.values()))
        .filter((c) => c.isActive && !exclude.has(c.userId))
        .filter((c) => !needle || c.displayName.toLowerCase().includes(needle) || (c.rank || '').toLowerCase().includes(needle))
        .slice(0, 200);
      list.replaceChildren(...people.map((c) => {
        const item = el('li');
        const label = el('label', 'pm-chat__picker-item');
        const box = el('input', 'form-check-input');
        box.type = 'checkbox';
        box.dataset.focusKey = `pick:${c.userId}`;
        box.checked = chosen.has(c.userId);
        box.addEventListener('change', () => {
          if (box.checked && chosen.size >= limit) {
            box.checked = false;
            note.textContent = `A group can have at most ${limit + exclude.size} people.`;
            return;
          }
          if (box.checked) chosen.add(c.userId); else chosen.delete(c.userId);
          note.textContent = chosen.size ? `${chosen.size} selected` : '';
        });
        label.append(box, el('span', null, c.rank ? `${c.displayName} · ${c.rank}` : c.displayName));
        item.append(label);
        return item;
      }));
    };
    filter.addEventListener('input', () => {
      if (state) state.filter = filter.value;
      draw();
    });
    draw();
    if (chosen.size) note.textContent = `${chosen.size} selected`;
    node.append(filter, list, note);
    return { node, selected: () => Array.from(chosen), focus: () => filter.focus() };
  };

  ChatApp.prototype.openGroupForm = function () {
    const form = this.groupForm;
    if (!form.hidden) {
      form.hidden = true;
      return;
    }
    const title = el('input', 'form-control form-control-sm');
    title.type = 'text';
    title.maxLength = 100;
    title.placeholder = 'Group name';
    title.setAttribute('aria-label', 'Group name');
    const picker = this.peoplePicker(new Set([this.me]), GROUP_LIMIT - 1);
    const error = el('p', 'pm-chat__form-error');
    error.setAttribute('aria-live', 'polite');
    const actions = el('div', 'pm-chat__form-actions');
    const create = el('button', 'btn btn-primary btn-sm', 'Create group');
    create.type = 'submit';
    const cancel = el('button', 'btn btn-link btn-sm', 'Cancel');
    cancel.type = 'button';
    cancel.addEventListener('click', () => {
      form.hidden = true;
      this.newGroupButton.focus();
    });
    actions.append(create, cancel);
    form.replaceChildren(el('h3', 'pm-chat__section', 'New group'), title, picker.node, error, actions);
    form.onsubmit = async (event) => {
      event.preventDefault();
      create.disabled = true;
      error.textContent = '';
      try {
        const group = await this.request('POST', '/groups', { title: title.value, memberUserIds: picker.selected() });
        form.hidden = true;
        this.onGroupChanged(group);
        this.openWindow(groupKey(group.id), { focus: true });
      } catch (failure) {
        error.textContent = failure.message;
      } finally {
        create.disabled = false;
      }
    };
    form.hidden = false;
    title.focus();
  };

  /**
   * Adds a small chat button to every element marked data-chat-user="{userId}" (people's names
   * elsewhere in PRISM), except the signed-in user. Safe to call repeatedly.
   */
  ChatApp.prototype.attachPersonButtons = function (scope) {
    if (this.stopped) return;
    scope.querySelectorAll('[data-chat-user]').forEach((node) => {
      const userId = node.dataset.chatUser;
      if (!userId || userId === this.me || node.querySelector(':scope > .pm-chat-person')) return;
      const name = node.dataset.chatName || node.textContent.trim();
      const button = el('button', 'pm-chat-person');
      button.type = 'button';
      button.title = `Chat with ${name}`;
      button.setAttribute('aria-label', `Chat with ${name}`);
      button.append(icon('chat-dots'));
      button.addEventListener('click', (event) => {
        event.preventDefault();
        event.stopPropagation();
        this.openWindow(userId, { focus: true });
      });
      node.append(button);
    });
  };

  ChatApp.prototype.renderLauncherBadge = function () {
    const total = this.unreadTotal();
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

    if (!win && opts.pop && !this.hasRoom()) {
      // Every window on screen is in use (typing, sending, or the one being read): the new
      // message waits in the unread count and the alert instead of pushing one of them aside.
      return null;
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
    if (win.unsendTimer) global.clearTimeout(win.unsendTimer);
    // Failed messages are kept with the draft; ones still sending are stored on the server and
    // come back with the history.
    const unsent = win.messages.filter((m) => !m.id && m.failed);
    if (win.input.value.trim() || unsent.length) {
      this.kept.set(peerId, { draft: win.input.value, unsent });
    }
    this.windows.delete(peerId);
    this.order = this.order.filter((id) => id !== peerId);
    this.saveState();
  };

  /** The person is in the middle of something in this window: typing, sending, or in it. */
  ChatApp.prototype.isBusy = function (win) {
    return !!win.input.value.trim()
      || win.messages.some((m) => !m.id)
      || win.node.contains(document.activeElement);
  };

  /** Whether another docked window fits, or one can make way without interrupting the person. */
  ChatApp.prototype.hasRoom = function () {
    const capacity = windowCapacity(global.innerWidth || 1280);
    return this.order.length < capacity || this.order.some((id) => !this.isBusy(this.windows.get(id)));
  };

  /**
   * Too many windows for the screen: the least recently opened ones close (history is kept).
   * Windows the person is busy in go last, and even then their draft is kept for reopening.
   */
  ChatApp.prototype.enforceCapacity = function () {
    const capacity = windowCapacity(global.innerWidth || 1280);
    while (this.order.length > capacity) {
      const candidates = this.order.slice(1).reverse(); // never the one just opened
      const idle = candidates.find((id) => !this.isBusy(this.windows.get(id)));
      this.closeWindow(idle || candidates[0]);
    }
  };

  /**
   * One conversation view. Docked windows sit in the dock; an embedded one (options.embedded)
   * fills the Messages page and has no minimise/close buttons.
   */
  ChatApp.prototype.createWindow = function (peerId, options) {
    const embedded = !!(options && options.embedded);
    const groupId = groupIdOf(peerId);
    const group = groupId ? this.groups.get(groupId) : null;
    const contact = groupId ? null : this.contacts.get(peerId);
    const node = el('section', `pm-chat__window${embedded ? ' is-embedded' : ''}`);
    node.setAttribute('role', embedded ? 'region' : 'dialog');
    if (!embedded) node.setAttribute('aria-modal', 'false');

    const head = el('header', 'pm-chat__window-head');
    const toggle = el(embedded ? 'div' : 'button', 'pm-chat__window-title');
    if (!embedded) toggle.type = 'button';
    const face = groupId ? groupAvatar(group && group.title) : avatar(contact);
    const names = el('span', 'pm-chat__window-names');
    const name = el('span', 'pm-chat__window-name', group ? group.title : (contact ? contact.displayName : 'Chat'));
    const status = el('span', 'pm-chat__window-status');
    status.setAttribute('aria-live', 'polite');
    names.append(name, status);
    toggle.append(face, names);
    const minimize = iconButton('dash-lg', 'Minimise');
    const close = iconButton('x-lg', 'Close chat');
    const people = iconButton('people', 'People in this group');
    head.append(toggle);
    if (groupId) head.append(people);
    if (!embedded) head.append(minimize, close);

    const body = el('div', 'pm-chat__window-body');
    const earlier = el('button', 'btn btn-link btn-sm pm-chat__earlier', 'Show earlier messages');
    earlier.type = 'button';
    earlier.hidden = true;
    const log = el('ol', 'pm-chat__log');
    log.setAttribute('role', 'log');
    // Not live: the log is redrawn whole on every update and would be read out again. New
    // messages are announced once through the app's live region instead.
    log.setAttribute('aria-live', 'off');
    const newer = el('button', 'btn btn-link btn-sm pm-chat__earlier', 'Show newer messages');
    newer.type = 'button';
    newer.hidden = true;
    body.append(earlier, log, newer);

    const notice = el('p', 'pm-chat__notice');
    notice.hidden = true;

    const panel = el('div', 'pm-chat__members');
    panel.hidden = true;
    panel.id = `pm-chat-people-${++panelSerial}`;
    panel.setAttribute('role', 'region');
    panel.setAttribute('aria-label', 'People in this group');
    panel.tabIndex = -1;
    people.setAttribute('aria-controls', panel.id);
    people.setAttribute('aria-expanded', 'false');
    const stage = el('div', 'pm-chat__window-stage');
    stage.append(body, panel);

    const form = el('form', 'pm-chat__compose');
    const input = el('textarea', 'form-control form-control-sm pm-chat__input');
    input.rows = embedded ? 2 : 1;
    input.maxLength = this.maxLength;
    input.placeholder = 'Type a message';
    input.setAttribute('aria-label', `Message ${group ? group.title : (contact ? contact.displayName : '')}`.trim());
    const send = iconButton('send', 'Send', 'pm-chat__send');
    send.type = 'submit';
    form.append(input, send);

    node.append(head, stage, notice, form);

    const win = {
      peerId, node, face, name, status, body, log, earlier, newer, notice, input, form, minimize, embedded, toggle,
      messages: [], hasMore: false, hasNewer: false, historyLoaded: false, syncing: false, syncAgain: false,
      loading: false, minimized: false, attention: false, lastReadId: 0, readSending: 0, peerReadId: 0,
      typingUntil: 0, typingTimer: null, typingSentAt: 0, highlightId: 0, heldLive: [], loadSeq: 0, unsendTimer: null,
      groupId, members: [], removed: false, typers: new Map(), panel, panelOpen: false, people,
    };

    people.addEventListener('click', () => {
      if (win.panelOpen) {
        this.closeMembersPanel(win);
        return;
      }
      win.panelOpen = true;
      this.renderMembersPanel(win);
      panel.focus(); // into the panel, so the keyboard user is where the content appeared
    });

    // Esc steps back one level: closes the people panel, then minimises the window (docked).
    node.addEventListener('keydown', (event) => {
      if (event.key !== 'Escape') return;
      if (win.panelOpen) {
        event.stopPropagation();
        this.closeMembersPanel(win);
        return;
      }
      if (embedded) return;
      win.minimized = true;
      this.renderWindowState(win);
      this.saveState();
      toggle.focus(); // the title bar stays visible and reopens the window
    });

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
        toggle.focus();
      });
      close.addEventListener('click', () => {
        this.closeWindow(peerId);
        this.focusLauncher();
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

    // Reopened after it was closed (or closed to make room): what the person had typed, and any
    // message that failed to send, are back where they left them.
    const kept = this.kept.get(peerId);
    if (kept) {
      this.kept.delete(peerId);
      input.value = kept.draft;
      win.messages = kept.unsent;
    }

    return win;
  };

  ChatApp.prototype.sendTyping = function (win) {
    if (!win.input.value.trim()) return;
    const now = Date.now();
    if (now - win.typingSentAt < TYPING_SEND_MS) return;
    win.typingSentAt = now;
    if (win.groupId) this.signal('TypingInGroup', win.groupId);
    else this.signal('Typing', win.peerId);
  };

  ChatApp.prototype.renderWindowState = function (win) {
    win.node.classList.toggle('is-minimized', win.minimized);
    win.node.classList.toggle('needs-attention', win.attention);
    win.minimize.hidden = win.minimized;
    if (win.groupId) {
      this.renderGroupWindowState(win);
      return;
    }
    const contact = this.contacts.get(win.peerId);
    if (contact) {
      win.name.textContent = contact.displayName;
      win.face.querySelector('.pm-chat__initials').textContent = contact.initials;
      win.node.setAttribute('aria-label', `Chat with ${contact.displayName}`);
      win.input.setAttribute('aria-label', `Message ${contact.displayName}`);
    }
    setPresence(win.face, contact);
    const typing = win.typingUntil > Date.now();
    win.node.classList.toggle('is-typing', typing);
    const note = contact && contact.statusMessage ? ` · ${contact.statusMessage}` : '';
    setText(win.status, typing ? 'typing…' : (win.attention ? 'New message' : statusText(contact) + note));
    win.status.title = contact && contact.statusMessage ? contact.statusMessage : '';

    const canSend = !!contact && contact.isActive;
    win.form.hidden = !canSend;
    win.notice.hidden = canSend;
    win.notice.textContent = canSend ? '' : 'This account is inactive. You can read past messages but cannot send new ones.';
  };

  ChatApp.prototype.renderGroupWindowState = function (win) {
    const group = this.groups.get(win.groupId);
    const title = group ? group.title : (win.name.textContent || 'Group');
    win.name.textContent = title;
    win.face.title = title;
    win.node.setAttribute('aria-label', `Group chat ${title}`);
    win.input.setAttribute('aria-label', `Message ${title}`); // follows a rename
    const now = Date.now();
    const typers = Array.from(win.typers.entries())
      .filter(([, until]) => until > now)
      .map(([userId]) => this.nameOf(userId, win));
    win.node.classList.toggle('is-typing', typers.length > 0);
    const count = group ? group.memberCount : (win.members || []).length;
    setText(win.status, typers.length
      ? typingLine(typers)
      : (win.attention ? 'New message' : (win.removed ? 'You are not in this group' : peopleCount(count))));
    win.status.title = '';
    win.people.hidden = win.removed;

    const canSend = !!group && !win.removed;
    win.form.hidden = !canSend;
    win.notice.hidden = canSend;
    win.notice.textContent = canSend ? '' : 'You are no longer in this group. Earlier messages stay here until you close it.';
  };

  /** Members, add people, rename, leave: shown over the conversation, from the people button. */
  /**
   * Redrawn whenever the group changes (someone else may add or remove people while it is open),
   * so what the person is in the middle of lives in win.panelState and survives the redraw: ticked
   * people, search text, the open Add section, a half-typed name, an armed Leave, the last error,
   * and which control had focus.
   */
  ChatApp.prototype.renderMembersPanel = function (win) {
    const panel = win.panel;
    panel.hidden = !win.panelOpen || win.removed;
    win.people.setAttribute('aria-expanded', String(!panel.hidden));
    if (panel.hidden) return;
    const state = win.panelState || (win.panelState = {
      chosen: new Set(), filter: '', addOpen: false, rename: null, leaveArmed: false, error: '',
    });
    const focusKey = focusKeyIn(panel);
    const group = this.groups.get(win.groupId);
    const manager = group ? group.managerUserId : '';
    const iManage = manager === this.me;

    const head = el('div', 'pm-chat__members-head');
    head.append(el('strong', null, 'People in this group'));
    const close = iconButton('x-lg', 'Close people');
    close.addEventListener('click', () => this.closeMembersPanel(win));
    head.append(close);

    // Members arrive with the first history page; until then there is nothing to list or exclude.
    if (!(win.members || []).length) {
      panel.replaceChildren(head, el('p', 'pm-chat__picker-note', 'Loading…'));
      return;
    }

    const list = el('ul', 'pm-chat__members-list');
    for (const member of win.members || []) {
      const item = el('li');
      const label = member.userId === this.me ? `${member.displayName} (you)` : member.displayName;
      item.append(el('span', member.isActive ? null : 'is-inactive', label));
      if (member.userId === manager) item.append(el('span', 'pm-chat__badge-soft', 'manages'));
      if (iManage && member.userId !== this.me) {
        const remove = el('button', 'btn btn-link btn-sm p-0', 'Remove');
        remove.type = 'button';
        remove.setAttribute('aria-label', `Remove ${member.displayName}`);
        remove.dataset.focusKey = `remove:${member.userId}`;
        remove.addEventListener('click', () => this.groupAction(win, `/members/${encodeURIComponent(member.userId)}/remove`, {}));
        item.append(remove);
      }
      list.append(item);
    }

    const error = el('p', 'pm-chat__form-error', state.error);
    win.panelError = error;
    const parts = [head, list];

    const add = el('details', 'pm-chat__members-add');
    add.open = state.addOpen;
    add.addEventListener('toggle', () => { state.addOpen = add.open; });
    const summary = el('summary', null, 'Add people');
    summary.dataset.focusKey = 'add-toggle';
    add.append(summary);
    const existing = new Set((win.members || []).map((m) => m.userId));
    const picker = this.peoplePicker(existing, Math.max(0, GROUP_LIMIT - existing.size), state);
    const addButton = el('button', 'btn btn-primary btn-sm', 'Add');
    addButton.type = 'button';
    addButton.dataset.focusKey = 'add';
    addButton.addEventListener('click', () => {
      const ids = picker.selected();
      if (ids.length) this.groupAction(win, '/members', { userIds: ids });
    });
    add.append(picker.node, addButton);
    parts.push(add);

    if (iManage) {
      const rename = el('form', 'pm-chat__members-rename');
      const input = el('input', 'form-control form-control-sm');
      input.type = 'text';
      input.maxLength = 100;
      input.value = state.rename !== null ? state.rename : (group ? group.title : '');
      input.setAttribute('aria-label', 'Group name');
      input.dataset.focusKey = 'rename';
      input.addEventListener('input', () => { state.rename = input.value; });
      const save = el('button', 'btn btn-outline-secondary btn-sm', 'Rename');
      save.type = 'submit';
      save.dataset.focusKey = 'rename-save';
      rename.append(input, save);
      rename.addEventListener('submit', (event) => {
        event.preventDefault();
        this.groupAction(win, '/rename', { title: input.value });
      });
      parts.push(rename);
    }

    const armedText = iManage ? 'Leave? Someone else will manage it' : 'Click again to leave';
    const leave = el('button', 'btn btn-outline-danger btn-sm pm-chat__leave', state.leaveArmed ? armedText : 'Leave group');
    leave.type = 'button';
    leave.dataset.focusKey = 'leave';
    leave.addEventListener('click', () => {
      // A second click confirms, so a stray click cannot remove you from the group.
      if (!state.leaveArmed) {
        state.leaveArmed = true;
        leave.textContent = armedText;
        return;
      }
      this.groupAction(win, '/leave', {});
    });
    parts.push(error, leave);
    panel.replaceChildren(...parts);
    refocus(panel, focusKey);
  };

  /** Closes the people panel, forgets what was in progress there, and hands focus back. */
  ChatApp.prototype.closeMembersPanel = function (win) {
    win.panelOpen = false;
    win.panelState = null;
    this.renderMembersPanel(win);
    win.people.focus();
  };

  /** Runs a group change; the pushed GroupChanged/GroupRemoved events update every view. */
  ChatApp.prototype.groupAction = async function (win, path, body) {
    const state = win.panelState;
    try {
      const group = await this.request('POST', `/groups/${win.groupId}${path}`, body);
      if (state) {
        // Done: what was being added or typed is spent.
        state.error = '';
        if (path === '/members') {
          state.chosen.clear();
          state.filter = '';
          state.addOpen = false;
        }
        if (path === '/rename') state.rename = null;
      }
      if (path === '/leave') {
        this.onGroupRemoved(win.groupId);
      } else if (group) {
        this.onGroupChanged(group);
      }
    } catch (error) {
      if (error.status === 410) {
        this.onGroupRemoved(win.groupId);
      } else {
        if (state) state.error = error.message;
        if (win.panelError) win.panelError.textContent = error.message;
        this.announce(error.message);
      }
    }
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
    const readPath = win.groupId ? `/groups/${win.groupId}/read` : `/with/${encodeURIComponent(win.peerId)}/read`;
    this.request('POST', readPath, { upToMessageId: newest })
      .then(() => { win.lastReadId = Math.max(win.lastReadId || 0, newest); })
      .catch(() => {})
      .finally(() => { if (win.readSending === newest) win.readSending = 0; });
    const counter = win.groupId ? this.groups.get(win.groupId) : this.contacts.get(win.peerId);
    if (counter && !win.hasNewer) {
      counter.unreadCount = 0;
      this.renderContacts();
    }
  };

  ChatApp.prototype.historyPath = function (win, query) {
    const base = win.groupId ? `/groups/${win.groupId}` : `/with/${encodeURIComponent(win.peerId)}`;
    return `${base}/messages?limit=${HISTORY_PAGE}${query || ''}`;
  };

  ChatApp.prototype.applyPeerRead = function (win, history) {
    if (history && history.peerLastReadMessageId) {
      win.peerReadId = Math.max(win.peerReadId || 0, history.peerLastReadMessageId);
    }
    if (history && history.members) {
      // Group: who is in it and how far each has read ("Seen by"); markers only move forward.
      const known = new Map((win.members || []).map((m) => [m.userId, m.lastReadMessageId || 0]));
      win.members = history.members.map((m) => ({
        ...m,
        lastReadMessageId: Math.max(m.lastReadMessageId || 0, known.get(m.userId) || 0),
      }));
      // A panel opened before the members arrived is still showing "Loading…".
      if (win.panelOpen) this.renderMembersPanel(win);
    }
  };

  /** 410 from a group endpoint: this user is no longer in the group. */
  ChatApp.prototype.checkGone = function (win, error) {
    if (win.groupId && error && error.status === 410) this.onGroupRemoved(win.groupId);
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
    const firstLoad = !win.historyLoaded;
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
        // Fetched rather than pushed (polling while the live connection is down, or catching
        // up after it returns): the log is not a live region, so say what arrived.
        this.announceFetched(win, result.messages.filter((m) => m.id > knownMax));
      }
      this.renderMessages(win, { stickToBottom: true, force: firstLoad });
      this.maybeMarkRead(win);
    } catch (error) {
      this.checkGone(win, error);
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
    this.applyTombstones(win);
    const body = win.body;
    const nearBottom = body.scrollHeight - body.scrollTop - body.clientHeight < 48;
    const focused = focusKeyIn(win.log);
    const now = new Date();
    const items = [];
    let lastDay = null;
    let highlighted = null;
    let nextExpiry = Infinity;

    if (!win.messages.length) {
      items.push(el('li', 'pm-chat__system', 'No messages yet. Say hello.'));
    }

    let lastSender = null;
    for (const message of win.messages) {
      const sent = new Date(message.sentUtc || Date.now());
      const key = dayKey(sent);
      if (key !== lastDay) {
        items.push(el('li', 'pm-chat__day', dayLabel(sent, now)));
        lastDay = key;
        lastSender = null;
      }
      if (message.eventKind) {
        items.push(el('li', 'pm-chat__system pm-chat__event', this.describeEvent(win, message)));
        lastSender = null;
        continue;
      }
      const mine = message.senderUserId === this.me;
      const item = el('li', `pm-chat__msg ${mine ? 'is-mine' : 'is-theirs'}`);
      const who = this.nameOf(message.senderUserId, win);
      if (win.groupId && !mine && message.senderUserId !== lastSender) {
        // In a group, say who is talking at the start of each run of their messages.
        item.append(el('span', 'pm-chat__sender', who));
      } else {
        // Sides and colours show who wrote it; a screen reader hears it instead.
        item.append(el('span', 'visually-hidden', `${who}: `));
      }
      lastSender = message.senderUserId;
      if (message.id && message.id === win.highlightId) {
        item.classList.add('is-highlight');
        highlighted = item;
      }
      const bubble = el('div', 'pm-chat__bubble', message.isUnsent ? (mine ? 'You unsent a message' : 'Message unsent') : message.body);
      if (message.isUnsent) item.classList.add('is-unsent');
      const meta = el('span', 'pm-chat__time');
      if (message.failed) {
        item.classList.add('is-failed');
        meta.textContent = message.error || 'Not sent.';
        const retry = el('button', 'btn btn-link btn-sm p-0 pm-chat__retry', 'Retry');
        retry.type = 'button';
        retry.dataset.focusKey = `retry:${message.clientMessageId}`;
        retry.addEventListener('click', () => this.deliver(win, message));
        meta.append(' ', retry);
      } else if (!message.id) {
        item.classList.add('is-pending');
        meta.textContent = 'Sending…';
      } else {
        meta.textContent = timeFormat.format(sent);
        if (mine) {
          // Read ticks: one tick once stored, two (coloured) once read. In a group, two ticks
          // once everyone has read it; the label says who has.
          let seen;
          let label;
          if (win.groupId) {
            const readers = seenBy(win.members, this.me, message);
            seen = seenByEveryone(win.members, this.me, message);
            label = readers.length ? `Seen by ${readers.map((m) => m.displayName).join(', ')}` : 'Sent';
          } else {
            seen = message.id <= (win.peerReadId || 0);
            label = seen ? 'Seen' : 'Sent';
          }
          const tick = icon(seen ? 'check2-all' : 'check2');
          tick.classList.add('pm-chat__tick');
          if (seen) tick.classList.add('is-seen');
          tick.removeAttribute('aria-hidden');
          tick.setAttribute('role', 'img');
          tick.setAttribute('aria-label', label);
          tick.title = label;
          meta.append(' ', tick);
          const left = this.unsendMs - (Date.now() - Date.parse(message.sentUtc));
          if (!message.isUnsent && left > 0) {
            const undo = el('button', 'btn btn-link btn-sm p-0 pm-chat__unsend', 'Unsend');
            undo.type = 'button';
            undo.setAttribute('aria-label', 'Unsend this message');
            undo.dataset.focusKey = `unsend:${message.id}`;
            undo.addEventListener('click', () => this.unsend(win, message));
            meta.append(' · ', undo);
            nextExpiry = Math.min(nextExpiry, left);
          }
          if (message.unsendError && !message.isUnsent) {
            meta.append(' · ', el('span', 'pm-chat__unsend-error', message.unsendError));
          }
        }
      }
      item.append(bubble, meta);
      items.push(item);
    }

    win.log.replaceChildren(...items);
    if (focused && !refocus(win.log, focused) && !win.form.hidden) {
      // The control is gone (sent after Retry, unsent, or its time ran out): stay in the window.
      win.input.focus({ preventScroll: true });
    }
    // Take the Unsend link away when its time runs out.
    if (win.unsendTimer) global.clearTimeout(win.unsendTimer);
    win.unsendTimer = Number.isFinite(nextExpiry)
      ? global.setTimeout(() => this.renderMessages(win, { stickToBottom: false }), nextExpiry + 250)
      : null;
    win.earlier.hidden = !win.hasMore;
    win.newer.hidden = !win.hasNewer;
    if (options && options.scrollToHighlight && highlighted) {
      if (typeof highlighted.scrollIntoView === 'function') highlighted.scrollIntoView({ block: 'center' });
    } else if (options && options.stickToBottom && (nearBottom || options.force)) {
      // Only while the person is at the bottom, or on the first load: someone reading back
      // through older messages is not pulled down by a new message or the 30-second refresh.
      body.scrollTop = body.scrollHeight;
    }
  };

  ChatApp.prototype.unsend = async function (win, message) {
    try {
      const result = await this.request('POST', `/messages/${message.id}/unsend`, {});
      this.applyUnsent(this.viewFor(win), result);
    } catch (error) {
      message.unsendError = error.message;
      this.renderMessages(this.viewFor(win), { stickToBottom: false });
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
      recipientUserId: win.groupId ? '' : win.peerId,
      groupId: win.groupId || null,
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
      const saved = win.groupId
        ? await this.request('POST', `/groups/${win.groupId}/messages`, {
          clientMessageId: message.clientMessageId,
          body: message.body,
        })
        : await this.request('POST', '/messages', {
          recipientUserId: win.peerId,
          clientMessageId: message.clientMessageId,
          body: message.body,
        });
      // Checked now, not when sending: the person may have opened an older slice meanwhile,
      // or the conversation may have moved to another view (dock to Messages page).
      this.acceptSaved(this.viewFor(win), [saved]);
      return;
    } catch (error) {
      message.failed = true;
      message.error = error.message;
      this.checkGone(win, error);
      this.announce(`Message not sent. ${error.message}`);
    }
    this.renderMessages(this.viewFor(win), { stickToBottom: true });
  };

  /**
   * The view now showing a conversation. A send outlives the window it started in when the
   * conversation moves from the dock to the Messages page; the message object moves with it,
   * so its result belongs to the new view.
   */
  ChatApp.prototype.viewFor = function (win) {
    return this.windows.get(win.peerId) || win;
  };

  /**
   * The one way a newly saved message (pushed, or the response to a send) enters a view.
   * Normally it is merged in place. While the view shows an older slice (hasNewer), it would
   * sit below a gap and become the cursor for "Show newer", silently skipping the messages in
   * between; so it is held until "Show newer" reaches the latest page, and its unsent copy
   * leaves the slice.
   */
  ChatApp.prototype.acceptSaved = function (win, messages) {
    if (win.hasNewer) {
      const ids = new Set(messages.map((m) => m.clientMessageId));
      win.messages = win.messages.filter((m) => m.id || !ids.has(m.clientMessageId));
      win.heldLive.push(...messages);
      this.renderMessages(win, { stickToBottom: false });
      return;
    }
    win.messages = mergeMessages(win.messages, messages);
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
    // Not live: the results are a long list; the outcome is announced once instead.
    this.results.setAttribute('aria-live', 'off');

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
    const withId = conversationKey(this.node.dataset.openWith);
    const at = Number(this.node.dataset.openAt) || 0;
    this.renderConversations();
    if (withId) this.open(withId, at || 0);
  };

  MessagesPage.prototype.renderConversations = function () {
    const direct = conversationList(Array.from(this.app.contacts.values())).map((contact) => ({
      key: contact.userId, contact, last: contact.lastMessageUtc, unread: contact.unreadCount,
    }));
    const groups = Array.from(this.app.groups.values()).map((group) => ({
      key: groupKey(group.id), group, last: group.lastMessageUtc, unread: group.unreadCount || 0,
    }));
    const conversations = direct.concat(groups)
      .sort((a, b) => (Date.parse(b.last) || 0) - (Date.parse(a.last) || 0));
    const now = new Date();
    const focused = focusKeyIn(this.list);
    this.list.replaceChildren(...conversations.map((entry) => {
      const item = el('li');
      const button = el('button', 'pm-chat-page__item');
      button.type = 'button';
      button.dataset.focusKey = `conv:${entry.key}`;
      if (this.current && this.current.peerId === entry.key) button.setAttribute('aria-current', 'true');
      const text = el('span', 'pm-chat__contact-text');
      let face;
      if (entry.group) {
        face = groupAvatar(entry.group.title);
        text.append(el('span', 'pm-chat__contact-name', entry.group.title));
        text.append(el('span', 'pm-chat__contact-meta', `Group · ${peopleCount(entry.group.memberCount)}`));
      } else {
        const contact = entry.contact;
        if (!contact.isActive) button.classList.add('is-inactive');
        face = avatar(contact);
        setPresence(face, contact);
        text.append(el('span', 'pm-chat__contact-name', contact.displayName));
        text.append(el('span', 'pm-chat__contact-meta', contact.rank ? `${contact.rank} · ${statusText(contact)}` : statusText(contact)));
      }
      const side = el('span', 'pm-chat-page__item-side');
      if (entry.last) side.append(el('span', 'pm-chat-page__stamp', shortStamp(new Date(entry.last), now)));
      if (entry.unread > 0) {
        side.append(el('span', 'badge rounded-pill pm-chat__unread', entry.unread > 99 ? '99+' : String(entry.unread)));
      }
      button.append(face, text, side);
      button.addEventListener('click', () => this.open(entry.key, 0));
      item.append(button);
      return item;
    }));
    refocus(this.list, focused);
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
      if (!created) app.renderMessages(win, { stickToBottom: true, force: true }); // reattached: scroll was reset
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
      this.lastHits = null;
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
      // groups=true: this page can open group hits (older pages get direct hits only). The
      // endpoint binds a bool, so it must be true/false, not 1/0.
      const hits = await this.app.request('GET', `/search?q=${encodeURIComponent(term)}&groups=true`);
      if (seq !== this.searchSeq) return; // a newer search has started
      this.lastHits = hits;
      this.lastTerm = term;
      this.renderResults(hits, term);
      this.app.announce(hits.length
        ? `${hits.length} ${hits.length === 1 ? 'message' : 'messages'} found${hits.length >= 50 ? ', newest 50 shown' : ''}`
        : 'No messages match.');
    } catch (_) {
      if (seq === this.searchSeq) {
        this.results.replaceChildren(el('p', 'pm-chat-page__empty', 'Search failed. Try again.'));
        this.app.announce('Search failed. Try again.');
      }
    }
  };

  /** Re-renders the visible results, e.g. after an unsend removed one of them. */
  MessagesPage.prototype.refreshResults = function () {
    if (this.results.hidden || !this.lastHits) return;
    this.renderResults(this.lastHits, this.lastTerm);
  };

  MessagesPage.prototype.renderResults = function (allHits, term) {
    const now = new Date();
    // An unsent message's text is gone; it must not linger here either (including in a
    // response that was already on its way when the unsend arrived).
    const hits = allHits.filter((hit) => !this.app.unsentIds.has(hit.messageId));
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
      head.append(el('span', 'pm-chat__contact-name', hit.groupId ? `${hit.peerDisplayName} (group)` : hit.peerDisplayName));
      head.append(el('span', 'pm-chat-page__stamp', shortStamp(new Date(hit.sentUtc), now)));
      const snippet = el('span', 'pm-chat-page__snippet');
      if (hit.senderUserId === this.app.me) snippet.append(el('span', 'pm-chat-page__you', 'You: '));
      else if (hit.groupId) snippet.append(el('span', 'pm-chat-page__you', `${this.app.nameOf(hit.senderUserId)}: `));
      appendHighlighted(snippet, hit.body, term);
      button.append(head, snippet);
      button.addEventListener('click', () => this.open(hit.groupId ? groupKey(hit.groupId) : hit.peerUserId, hit.messageId));
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

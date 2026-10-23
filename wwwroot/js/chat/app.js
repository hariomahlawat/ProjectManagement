/*
 * PRISM chat, the controller: state, browser storage, HTTP, the live connection, polling, away.
 * One of the chat scripts loaded in order by _ChatScripts.cshtml (see chat/core.js for the list and
 * the server contract). Shared helpers come from PMChatParts, set up by chat/core.js.
 */
(function (global) {
  'use strict';

  const document = global.document;
  const {
    POLL_MS, IDLE_MS, IDLE_CHECK_MS, UNSENT_RETRY_MS, SERVER_TIMEOUT_MS, UNSENT_LOOKBACK_MS,
    TitleFlasher, groupIdOf, Chime,
  } = global.PMChatParts;

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
    // File attachments (chat/files.js), when switched on for this server.
    this.attachments = {
      enabled: root.dataset.attachments === 'on',
      maxBytes: (Number(root.dataset.attachMaxMb) || 100) * 1024 * 1024,
      maxFiles: Number(root.dataset.attachMaxFiles) || 5,
    };
    this.unsentIds = new Set(); // every unsend seen by this page, applied to anything loaded later
    // When live events stopped arriving (or, until the first connection, when this page started
    // loading history), so unsends in between can be caught up on once connected.
    this.offlineSince = new Date();
    this.offlineGeneration = 0; // bumped on every drop, so a catch-up can tell it was overtaken
    this.myStatus = null;
    this.storageKey = `prism.chat.dock.${this.me}`;
    this.soundKey = `prism.chat.sound.${this.me}`;
    this.dingKey = `prism.chat.ding.${this.me}`;
    this.muteKey = `prism.chat.muted.${this.me}`;
    this.contacts = new Map();
    this.groups = new Map(); // groupId -> { id, title, managerUserId, memberCount, unreadCount, lastMessageUtc }
    this.windows = new Map(); // conversation key -> window state (docked, or embedded in the Messages page)
    this.order = []; // docked peer ids, most recently opened first
    // Drafts and failed messages of closed windows, until the conversation is opened again. In
    // memory only: like the rest of chat, message text is never written to browser storage.
    this.kept = new Map();
    // Preview cards for links to PRISM records, by link (path + query): the card, or null for
    // "no card" (not a record, or not one this user may open). Asked for in batches, once per page.
    this.previews = new Map();
    this.previewQueue = new Set();
    this.previewTimer = null;
    this.buddyOpen = false;
    this.connection = null;
    this.pollTimer = null;
    this.stopped = false;
    this.away = false;
    this.lastActivity = Date.now();
    this.chime = new Chime();
    this.soundOn = this.readSoundPreference();
    this.muted = this.readMuted();
    this.title = new TitleFlasher(document, global.setInterval.bind(global), global.clearInterval.bind(global));
    this.page = null;
    this.launchers = Array.from(document.querySelectorAll('[data-chat-launcher]'));
  }

  ChatApp.prototype.start = function () {
    this.buildDock();
    this.launchers.forEach((button) => button.addEventListener('click', () => this.toggleBuddy()));
    global.addEventListener('resize', () => this.enforceCapacity());
    // Muting a group in another tab of this user applies here too.
    global.addEventListener('storage', (event) => {
      if (event.key === this.muteKey) this.applyMutedChange(this.readMuted());
    });
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
    if (pageNode) this.page = new global.PMChatParts.MessagesPage(this, pageNode);

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

  /**
   * Groups muted in this browser (like the sound setting): their messages still count as unread,
   * but do not chime, flash the tab title or pop the group's window open. Group ids only.
   */
  ChatApp.prototype.readMuted = function () {
    try {
      const ids = JSON.parse(global.localStorage.getItem(this.muteKey) || '[]');
      return new Set(Array.isArray(ids) ? ids.filter((id) => Number.isSafeInteger(id) && id > 0) : []);
    } catch (_) {
      return new Set();
    }
  };

  ChatApp.prototype.isMuted = function (groupId) {
    return !!groupId && this.muted.has(groupId);
  };

  ChatApp.prototype.setMuted = function (groupId, muted) {
    // Start from what is stored, not this tab's copy: another tab may have changed it since.
    const next = this.readMuted();
    if (muted) next.add(groupId); else next.delete(groupId);
    try {
      global.localStorage.setItem(this.muteKey, JSON.stringify(Array.from(next)));
    } catch (_) { /* this page only */ }
    this.applyMutedChange(next);
  };

  /** Takes a new set of muted groups and shows it: contact list, group windows and panels. */
  ChatApp.prototype.applyMutedChange = function (muted) {
    this.muted = muted;
    this.windows.forEach((win) => {
      if (!win.groupId) return;
      this.renderWindowState(win);
      if (win.panelOpen) this.renderMembersPanel(win);
    });
    this.renderContacts();
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
    connection.on('AttachmentRemoved', (removed) => this.onAttachmentRemoved(removed));
    connection.on('AttachmentRestored', (restored) => this.onAttachmentRestored(restored));
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

  global.PMChatParts.ChatApp = ChatApp;
})(window);

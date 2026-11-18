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
    TitleFlasher, groupIdOf, Chime, icon, mergeMessages,
  } = global.PMChatParts;

  const OFFLINE_NOTICE_MS = 4000; // a drop shorter than this shows no "Reconnecting…"
  const dndTimeFormat = new Intl.DateTimeFormat('en-IN', { hour: '2-digit', minute: '2-digit', hour12: false });

  // ---------------------------------------------------------------------------
  // Controller
  // ---------------------------------------------------------------------------

  function ChatApp(root) {
    this.root = root;
    this.me = root.dataset.currentUserId;
    this.api = root.dataset.apiBase.replace(/\/$/, '');
    this.hubUrl = root.dataset.hubUrl;
    this.messagesUrl = root.dataset.messagesUrl || '';
    this.guideUrl = root.dataset.guideUrl || '';
    this.maxLength = Number(root.dataset.maxLength) || 4000;
    this.unsendMs = (Number(root.dataset.unsendMinutes) || 0) * 60000;
    // Missing (a page from an older server): no Edit offered.
    this.editMs = (Number(root.dataset.editMinutes) || 0) * 60000;
    // PRISM's search suggestions, for #records in the message box (absent: not offered).
    this.recordSearchUrl = root.dataset.recordSearchUrl || '';
    // The person's own Notebook, for "Remind me" (absent: not offered).
    this.notebookApi = root.dataset.notebookApi || '';
    this.notebookUrl = root.dataset.notebookUrl || '';
    // File attachments (chat/files.js), when switched on for this server.
    this.attachments = {
      enabled: root.dataset.attachments === 'on',
      maxBytes: (Number(root.dataset.attachMaxMb) || 100) * 1024 * 1024,
      maxFiles: Number(root.dataset.attachMaxFiles) || 5,
    };
    this.unsentIds = new Set(); // every unsend seen by this page, applied to anything loaded later
    this.removedFiles = new Map(); // attachment id -> its removed card, the same for file removals
    this.restoredFiles = new Map(); // attachment id -> the file, for restores (undo) of removals
    // Live removals and restores in arrival order, so a catch-up answer read before one of them
    // never overrides it (attachment id -> the event's number).
    this.fileEventSeq = 0;
    this.fileEventAt = new Map();
    // Live edits and reaction changes, numbered by message id, so a state check (refreshLoaded)
    // whose answer was read before one of them does not put the older state back.
    this.messageEventSeq = 0;
    this.messageEventAt = new Map();
    // When live events stopped arriving (or, until the first connection, when this page started
    // loading history), so unsends in between can be caught up on once connected.
    this.offlineSince = new Date();
    this.offlineGeneration = 0; // bumped on every drop, so a catch-up can tell it was overtaken
    this.myStatus = null;
    this.storageKey = `prism.chat.dock.${this.me}`;
    this.soundKey = `prism.chat.sound.${this.me}`;
    this.dingKey = `prism.chat.ding.${this.me}`;
    this.muteKey = `prism.chat.muted.${this.me}`;
    this.dndKey = `prism.chat.dnd.${this.me}`;
    this.contacts = new Map();
    this.groups = new Map(); // groupId -> { id, title, managerUserId, memberCount, unreadCount, lastMessageUtc }
    // Live unread changes (a message pushed, a conversation read) by conversation key, numbered.
    // A contacts or groups list requested before such a change answers with an older count, so
    // the refresh keeps this tab's count for that conversation instead (touchUnread).
    this.unreadSeq = 0;
    this.unsending = new Set(); // message ids with an unsend request in flight
    this.unreadTouched = new Map();
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
    this.dnd = this.readDnd();
    this.title = new TitleFlasher(document, global.setInterval.bind(global), global.clearInterval.bind(global));
    this.page = null;
    this.launchers = Array.from(document.querySelectorAll('[data-chat-launcher]'));
  }

  ChatApp.prototype.start = function () {
    this.buildDock();
    this.applyDnd(this.dnd); // set on an earlier page: shown, and ended when its time comes
    this.launchers.forEach((button) => {
      button.addEventListener('click', () => this.toggleBuddy());
      button.setAttribute('aria-keyshortcuts', 'Alt+C');
    });
    // Alt+C from anywhere: the contact list, with the cursor in its search (a quick switcher:
    // type a name, Enter). Ctrl+K is PRISM's search palette, so chat does not take it.
    document.addEventListener('keydown', (event) => {
      if (!event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
      // The key must say "c": Option+C on a Mac types "ç", which must still type.
      if (String(event.key).toLowerCase() !== 'c') return;
      if (this.stopped) return;
      event.preventDefault();
      if (this.buddyOpen) this.search.focus();
      else this.toggleBuddy(true);
    });
    global.addEventListener('resize', () => this.enforceCapacity());
    // Muting a group in another tab of this user applies here too.
    global.addEventListener('storage', (event) => {
      if (event.key === this.muteKey) this.applyMutedChange(this.readMuted());
      if (event.key === this.dndKey) this.applyDnd(this.readDnd());
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
      new global.MutationObserver((mutations) => {
        // Chat's own redraws (contacts, messages, presence) never add names to mark.
        if (pending || mutations.every((m) => this.root.contains(m.target))) return;
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

  // ----- do not disturb -------------------------------------------------------------

  /**
   * Do not disturb, in this browser (like sound and mute) and shared by its tabs: until a time,
   * no chime, title flash, pop-up window or announcement for arriving messages; they still count
   * as unread. { until: ms, status: the status message it set, or null }.
   */
  ChatApp.prototype.readDnd = function () {
    try {
      const stored = JSON.parse(global.localStorage.getItem(this.dndKey) || 'null');
      if (stored && Number.isFinite(stored.until) && stored.until > Date.now()) {
        return { until: stored.until, status: typeof stored.status === 'string' ? stored.status : null };
      }
    } catch (_) { /* nothing stored */ }
    return null;
  };

  /**
   * Do not disturb that ended while no PRISM tab was open: nothing was there to clear the
   * status message it set. Once the person's status has loaded, that status goes if it is
   * still the one do not disturb set (one they changed meanwhile stays), and the record goes.
   */
  ChatApp.prototype.clearExpiredDndStatus = function () {
    let stored = null;
    try {
      stored = JSON.parse(global.localStorage.getItem(this.dndKey) || 'null');
    } catch (_) {
      return;
    }
    if (!stored || !Number.isFinite(stored.until) || stored.until > Date.now()) return;
    try {
      global.localStorage.removeItem(this.dndKey);
    } catch (_) { /* nothing more to do */ }
    if (typeof stored.status === 'string' && stored.status && this.myStatus === stored.status) {
      this.setStatusText('');
    }
  };

  ChatApp.prototype.isQuiet = function () {
    return !!this.dnd && this.dnd.until > Date.now();
  };

  /** Starts (until: a Date) or ends (null) do not disturb, in every tab of this browser. */
  ChatApp.prototype.setDnd = async function (until) {
    const previous = this.readDnd();
    let next = null;
    if (until) {
      // Others learn of it through the status message, unless the person has one of their own.
      const label = `Do not disturb until ${dndTimeFormat.format(until)}`;
      const ownStatus = this.myStatus && (!previous || this.myStatus !== previous.status);
      next = { until: until.getTime(), status: ownStatus ? null : label };
    }
    try {
      if (next) global.localStorage.setItem(this.dndKey, JSON.stringify(next));
      else global.localStorage.removeItem(this.dndKey);
    } catch (_) { /* this page only */ }
    this.applyDnd(next);
    if (next && next.status) {
      await this.setStatusText(next.status);
    } else if (previous && previous.status && this.myStatus === previous.status) {
      await this.setStatusText('');
    }
    this.announce(next ? `Do not disturb until ${dndTimeFormat.format(until)}. Messages still arrive, quietly.` : 'Do not disturb is off.');
  };

  /** Shows a do-not-disturb state, and ends it when its time comes (in each tab). */
  ChatApp.prototype.applyDnd = function (dnd) {
    this.dnd = dnd;
    if (this.dndTimer) global.clearTimeout(this.dndTimer);
    this.dndTimer = null;
    if (dnd) {
      // Long waits are re-armed: a browser timer cannot wait much beyond 24 days.
      const wait = Math.min(dnd.until - Date.now(), 6 * 3600000);
      this.dndTimer = global.setTimeout(() => {
        if (this.dnd && this.dnd.until <= Date.now()) this.endDnd();
        else this.applyDnd(this.readDnd());
      }, Math.max(0, wait) + 500);
    }
    this.renderDnd();
  };

  /** Its time came: off, and the status message it set goes too (if still the one shown). */
  ChatApp.prototype.endDnd = function () {
    const ended = this.dnd;
    try {
      const stored = JSON.parse(global.localStorage.getItem(this.dndKey) || 'null');
      if (stored && stored.until === (ended && ended.until)) global.localStorage.removeItem(this.dndKey);
    } catch (_) { /* nothing stored */ }
    this.applyDnd(null);
    if (ended && ended.status && this.myStatus === ended.status) this.setStatusText('');
  };

  // ----- HTTP ------------------------------------------------------------------

  ChatApp.prototype.request = async function (method, path, body) {
    const headers = { Accept: 'application/json' };
    if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
      const token = document.querySelector('meta[name="csrf-token"]')?.getAttribute('content');
      if (token) headers['X-CSRF-TOKEN'] = token;
    }

    let response;
    try {
      response = await fetch(this.api + path, {
        method,
        headers,
        credentials: 'same-origin',
        body: body === undefined ? undefined : JSON.stringify(body),
      });
    } catch (_) {
      // The browser's own text ("Failed to fetch") means nothing to people.
      throw new Error('PRISM could not be reached. Check the connection and try again.');
    }

    if (response.status === 401 || (response.status === 404 && response.headers && response.headers.get('X-Chat-Disabled'))) {
      // Signed out, or chat switched off on the server: stand down quietly. Any other 404 (a
      // conversation that no longer exists, a bad link) is an ordinary error for this request.
      this.shutdown();
      throw new Error('Chat is not available.');
    }

    if (!response.ok) {
      let detail = response.status === 429
        ? 'You are sending messages too quickly. Wait a moment and retry.'
        : (method === 'GET' ? 'This could not be loaded. Try again.' : 'The message could not be sent.');
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
    if (this.closeViewer) this.closeViewer();
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
    // Phase 3 (chat/actions.js): edits and reactions.
    connection.on('MessageEdited', (message) => this.onEdited(message));
    connection.on('GroupMessageEdited', (message) => this.onEdited(message));
    connection.on('ReactionsChanged', (reactions) => this.onReactions(reactions));
    connection.onreconnecting(() => {
      this.markOffline();
      this.startPolling();
      this.setLive(false);
    });
    connection.onreconnected(() => this.resync());
    connection.onclose(() => {
      if (this.stopped) return;
      this.markOffline();
      this.startPolling();
      this.setLive(false);
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
        this.setLive(false);
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
   * Unsends (and file removals) that happened while no live event could arrive: history catch-up
   * only fetches newer messages, so an already-loaded message that was unsent meanwhile would keep
   * its text, and a removed file its card.
   */
  ChatApp.prototype.syncUnsent = async function (done) {
    if (!this.offlineSince || this.stopped) return;
    if (this.unsentRetry) {
      global.clearTimeout(this.unsentRetry);
      this.unsentRetry = null;
    }
    const generation = this.offlineGeneration;
    const fileEventsBefore = this.fileEventSeq;
    const changedSince = (id) => (this.fileEventAt.get(id) || 0) > fileEventsBefore;
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
      // A file removed or restored live while these answers were on their way keeps that state.
      const removed = (this.attachments.enabled ? await this.removedFilesSince(since) : [])
        .filter((file) => !changedSince(file.attachmentId));
      removed.forEach((file) => {
        this.restoredFiles.delete(file.attachmentId);
        this.removedFiles.set(file.attachmentId, { id: file.attachmentId, isRemoved: true, removalReason: file.removalReason || null });
      });
      // A file this page remembers as removed may have been restored meanwhile (undo window):
      // ask after the rest, and bring back any that are kept again.
      const fresh = new Set(removed.map((file) => file.attachmentId));
      const remembered = Array.from(this.removedFiles.keys()).filter((id) => !fresh.has(id));
      for (let start = 0; start < remembered.length; start += 500) {
        const states = await this.request('GET', `/attachments/current?ids=${remembered.slice(start, start + 500).join(',')}`);
        states
          .filter((state) => !state.attachment.isRemoved && !changedSince(state.attachment.id))
          .forEach((state) => this.onAttachmentRestored(state));
      }
      if (ids.length || removed.length) {
        this.windows.forEach((w) => this.renderMessages(w, { stickToBottom: false }));
        if (this.page) this.page.refreshResults();
      }
      // Back live: edits and reactions missed meanwhile on messages already on screen (catch-up
      // only fetches newer messages). A failure retries with the rest of this catch-up.
      if (done) await this.refreshLoaded();
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

  /**
   * Asks the server for the current state of every message this page has loaded and shows any
   * that changed (edited, reactions, mentions, unsent). An older server without the endpoint
   * answers 404: nothing to do.
   */
  ChatApp.prototype.refreshLoaded = async function () {
    const ids = new Set();
    this.windows.forEach((win) => {
      win.messages.concat(win.heldLive || []).forEach((m) => {
        if (m.id && !m.eventKind && !m.isUnsent) ids.add(m.id);
      });
    });
    const all = Array.from(ids);
    for (let start = 0; start < all.length; start += 500) {
      let current;
      const eventsBefore = this.messageEventSeq;
      try {
        current = await this.request('GET', `/messages/current?ids=${all.slice(start, start + 500).join(',')}`);
      } catch (error) {
        if (error && (error.status === 404 || error.status === 405)) return;
        throw error;
      }
      const fresh = new Map();
      for (const message of Array.isArray(current) ? current : []) {
        // Changed live while this answer was on its way: the live state is newer.
        if ((this.messageEventAt.get(message.id) || 0) > eventsBefore) continue;
        if (message.isUnsent) this.unsentIds.add(message.id); // applied on the redraw below
        else if (!this.unsentIds.has(message.id)) fresh.set(message.id, message);
      }
      // One redraw per window that holds any of them.
      this.windows.forEach((win) => {
        const held = win.messages.filter((m) => fresh.has(m.id)).map((m) => fresh.get(m.id));
        const live = (win.heldLive || []).some((m) => fresh.has(m.id));
        const unsent = win.messages.some((m) => m.id && this.unsentIds.has(m.id) && !m.isUnsent);
        if (!held.length && !live && !unsent) return;
        if (held.length) win.messages = mergeMessages(win.messages, held);
        if (live) win.heldLive = win.heldLive.map((m) => fresh.get(m.id) || m);
        this.renderMessages(win, { stickToBottom: false });
      });
    }
  };

  /** Every page of files removed since a time (see syncUnsent). */
  ChatApp.prototype.removedFilesSince = async function (since) {
    const base = `/attachments/removed?since=${encodeURIComponent(since.toISOString())}`;
    const files = [];
    let before = null;
    for (let guard = 0; guard < 200; guard += 1) {
      const page = await this.request('GET', before ? `${base}&before=${before}` : base);
      files.push(...page.files);
      if (!page.hasMore || !page.files.length) break;
      before = Math.min(...page.files.map((f) => f.attachmentId));
    }
    return files;
  };

  /**
   * The live connection dropped (false) or is back (true). A drop shows "Reconnecting…" in the
   * contact list and every window once it has lasted a few seconds (a blip shows nothing);
   * messages keep coming every 30 seconds meanwhile.
   */
  ChatApp.prototype.setLive = function (live) {
    if (live) {
      if (this.offlineTimer) global.clearTimeout(this.offlineTimer);
      this.offlineTimer = null;
      const wasShown = this.offlineShown;
      this.offlineShown = false;
      this.renderConnection();
      if (wasShown) this.announce('Chat is connected again.');
      return;
    }
    if (this.offlineShown || this.offlineTimer) return;
    this.offlineTimer = global.setTimeout(() => {
      this.offlineTimer = null;
      if (this.stopped || this.isConnected()) return;
      this.offlineShown = true;
      this.renderConnection();
      this.announce('Chat lost its connection. Trying again; new messages will still arrive.');
    }, OFFLINE_NOTICE_MS);
  };

  /** `extra`: a window being built, not yet among this.windows. */
  ChatApp.prototype.renderConnection = function (extra) {
    const text = 'Reconnecting… New messages may take up to 30 seconds to appear.';
    const notes = [this.offlineNote].concat(Array.from(this.windows.values(), (w) => w.offline), extra ? [extra.offline] : []);
    notes.filter(Boolean).forEach((note) => {
      note.hidden = !this.offlineShown;
      if (this.offlineShown && note.textContent !== text) {
        note.replaceChildren(icon('wifi-off'), global.document.createTextNode(` ${text}`));
      }
    });
  };

  ChatApp.prototype.resync = function () {
    this.setLive(true);
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

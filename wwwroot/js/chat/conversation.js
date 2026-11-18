/*
 * PRISM chat, one conversation: reading, history paging, drawing messages, sending.
 * One of the chat scripts loaded in order by _ChatScripts.cshtml (see chat/core.js for the list and
 * the server contract). Shared helpers come from PMChatParts, set up by chat/core.js.
 */
(function (global) {
  'use strict';

  const document = global.document;
  const {
    ChatApp, HISTORY_PAGE, CATCH_UP_PAGES, TIME_ZONE, timeFormat, newClientId, dayKey,
    dayLabel, mergeMessages, fetchSince, seenBy, seenByEveryone, el,
    focusKeyIn, refocus, icon,
    newestId, appendLinked, previewLinks, previewCard,
  } = global.PMChatParts;

  const PREVIEW_BATCH = 10; // links per GET {api}/previews (the server's limit)
  const RUN_GAP_MS = 5 * 60000; // messages from one person closer than this read as one run
  const fullTimeParts = new Intl.DateTimeFormat('en-IN', {
    weekday: 'short', day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false, timeZone: TIME_ZONE,
  });

  /** "Wed, 18 Nov 2026, 10:30" (India time), for the title of a message's time. */
  function fullTime(date) {
    const part = {};
    fullTimeParts.formatToParts(date).forEach((p) => { part[p.type] = p.value; });
    return `${part.weekday}, ${part.day} ${part.month} ${part.year}, ${part.hour}:${part.minute}`;
  }

  /**
   * The first of the last `count` messages from other people: where "New messages" goes when a
   * conversation opens with unread messages (the server counts them; the page doesn't know the
   * person's own read marker). 0 when there are none, or when they are not all on this page.
   */
  function firstUnreadId(messages, me, count) {
    if (!count) return 0;
    let seen = 0;
    for (let i = messages.length - 1; i >= 0; i -= 1) {
      const m = messages[i];
      if (!m.id || m.senderUserId === me || m.eventKind || m.isUnsent) continue;
      seen += 1;
      if (seen === count) return m.id;
    }
    return 0;
  }
  const PREVIEW_DELAY_MS = 40; // gather the links of one redraw (or several) into one request

  /** The cards for a message's PRISM links that are known; unknown links are asked for. */
  ChatApp.prototype.cardsFor = function (message) {
    const cards = [];
    for (const link of previewLinks(message.body || '', global.location.origin)) {
      const preview = this.previews.get(link);
      if (preview === undefined) this.queuePreview(link);
      else if (preview) cards.push(previewCard(preview));
    }
    return cards;
  };

  ChatApp.prototype.queuePreview = function (link) {
    this.previewQueue.add(link);
    if (!this.previewTimer) this.previewTimer = global.setTimeout(() => this.flushPreviews(), PREVIEW_DELAY_MS);
  };

  /**
   * Asks the server for the queued links' cards. Every link asked for gets an answer in the cache
   * (a card, or null), also when the request fails, so a link is never asked for twice on a page.
   */
  ChatApp.prototype.flushPreviews = async function () {
    this.previewTimer = null;
    const batch = Array.from(this.previewQueue).slice(0, PREVIEW_BATCH);
    batch.forEach((link) => {
      this.previewQueue.delete(link);
      this.previews.set(link, null);
    });
    if (this.previewQueue.size) this.previewTimer = global.setTimeout(() => this.flushPreviews(), PREVIEW_DELAY_MS);
    if (!batch.length || this.stopped) return;

    let found = [];
    try {
      found = await this.request('GET', `/previews?${batch.map((link) => `link=${encodeURIComponent(link)}`).join('&')}`) || [];
    } catch (_) {
      return; // no cards (an older server without previews, or a refusal): the links still work
    }
    let any = false;
    for (const preview of found) {
      if (preview && batch.includes(preview.link)) {
        this.previews.set(preview.link, preview);
        any = true;
      }
    }
    if (any) this.windows.forEach((win) => this.renderMessages(win, { stickToBottom: true }));
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

  /** A live change to a conversation's unread count (see unreadTouched in chat/app.js). */
  ChatApp.prototype.touchUnread = function (key) {
    this.unreadSeq += 1;
    this.unreadTouched.set(key, this.unreadSeq);
  };

  /**
   * A contact or group from a list requested when unreadSeq was `since`: if this tab changed
   * that conversation's unread count after the request went out, the list's count is older than
   * what is on screen, so the tab's own count stays.
   */
  ChatApp.prototype.keepLiveUnread = function (fresh, key, previous, since) {
    if (previous && (this.unreadTouched.get(key) || 0) > since) {
      fresh.unreadCount = previous.unreadCount;
    }
    return fresh;
  };

  ChatApp.prototype.maybeMarkRead = function (win) {
    if (!this.isAttended(win)) return;
    const newest = newestId(win.messages, (m) => m.senderUserId !== this.me);
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
      .catch(() => {
        // The badge was cleared before the server stored the read: ask for the real count.
        if (win.groupId) this.refreshGroups(); else this.refreshContacts();
      })
      .finally(() => { if (win.readSending === newest) win.readSending = 0; });
    const counter = win.groupId ? this.groups.get(win.groupId) : this.contacts.get(win.peerId);
    if (counter && !win.hasNewer) {
      counter.unreadCount = 0;
      this.touchUnread(win.peerId);
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
      // Asked for now: a live edit or reaction arriving before the answer stays (settleFetched).
      const eventsBefore = this.messageEventSeq;
      if (!win.historyLoaded) {
        const history = await this.request('GET', path);
        if (seq !== win.loadSeq) return;
        history.messages = this.settleFetched(history.messages, eventsBefore);
        win.messages = mergeMessages(win.messages, history.messages);
        win.hasMore = history.hasMore;
        win.historyLoaded = true;
        win.contiguousId = newestId(history.messages);
        // Opening with unread messages: "New messages" marks where they start.
        const counter = win.groupId ? this.groups.get(win.groupId) : this.contacts.get(win.peerId);
        // (A message that popped the window open may have marked itself already; an earlier
        // unread one wins.)
        const fromCount = counter ? firstUnreadId(win.messages, this.me, counter.unreadCount) : 0;
        if (fromCount && (!win.unreadFromId || fromCount < win.unreadFromId)) win.unreadFromId = fromCount;
        this.applyPeerRead(win, history);
      } else {
        // Catch up from the newest message known to follow on without a gap: one fetched as
        // history, not one pushed or returned by a send, which can be newer than messages
        // missed while the connection was down.
        const shownMax = newestId(win.messages);
        const knownMax = win.contiguousId === undefined ? shownMax : Math.min(win.contiguousId, shownMax);
        const shown = new Set(win.messages.filter((m) => m.id).map((m) => m.id));
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
        result.messages = this.settleFetched(result.messages, eventsBefore);
        if (!result.overlaps) {
          // Too much was missed to stitch together: show the recent stretch and let
          // "Show earlier messages" page back from there instead of leaving a silent gap.
          win.messages = win.messages.filter((m) => !m.id);
          win.hasMore = true;
        }
        win.messages = mergeMessages(win.messages, result.messages);
        win.contiguousId = Math.max(knownMax, newestId(result.messages));
        this.applyPeerRead(win, peerRead);
        // Fetched rather than pushed (polling while the live connection is down, or catching
        // up after it returns): the log is not a live region, so say what arrived.
        this.announceFetched(win, result.messages.filter((m) => m.id > knownMax && !shown.has(m.id)));
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
    win.contiguousId = undefined;
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
      const eventsBefore = this.messageEventSeq;
      const history = await this.request('GET', this.historyPath(win, `&before=${messageId + 1}`));
      if (seq !== win.loadSeq) return; // the person moved on (sent a reply, opened another hit)
      history.messages = this.settleFetched(history.messages, eventsBefore);
      win.messages = mergeMessages(win.messages.filter((m) => !m.id), history.messages);
      win.hasMore = history.hasMore;
      win.hasNewer = history.hasNewer;
      win.contiguousId = win.hasNewer ? undefined : newestId(history.messages);
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
      const eventsBefore = this.messageEventSeq;
      const history = await this.request('GET', this.historyPath(win, `&before=${oldest.id}`));
      // Another search hit (or a jump to the latest) replaced the view meanwhile: this page
      // belongs to the old one and would leave a gap in the new one.
      if (seq !== win.loadSeq) return;
      history.messages = this.settleFetched(history.messages, eventsBefore);
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
    const newest = newestId(win.messages);
    if (!newest || win.loading) return;
    win.loading = true;
    const seq = win.loadSeq;
    const hadFocus = win.node.contains(document.activeElement);
    const keep = win.body.scrollTop;
    try {
      const eventsBefore = this.messageEventSeq;
      const history = await this.request('GET', this.historyPath(win, `&after=${newest}`));
      if (seq !== win.loadSeq) return;
      history.messages = this.settleFetched(history.messages, eventsBefore);
      win.messages = mergeMessages(win.messages, history.messages);
      win.hasNewer = history.hasNewer;
      if (!win.hasNewer) win.contiguousId = newestId(history.messages) || newest;
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
    let lastSentAt = 0;
    let previousItem = null;
    let divider = null;
    for (const message of win.messages) {
      const sent = new Date(message.sentUtc || Date.now());
      const key = dayKey(sent);
      if (key !== lastDay) {
        items.push(el('li', 'pm-chat__day', dayLabel(sent, now)));
        lastDay = key;
        lastSender = null;
      }
      if (message.id && message.id === win.unreadFromId) {
        divider = el('li', 'pm-chat__unread-divider');
        divider.append(el('span', null, 'New messages'));
        items.push(divider);
        lastSender = null;
      }
      if (message.eventKind) {
        items.push(el('li', 'pm-chat__system pm-chat__event', this.describeEvent(win, message)));
        lastSender = null;
        continue;
      }
      const mine = message.senderUserId === this.me;
      const item = el('li', `pm-chat__msg ${mine ? 'is-mine' : 'is-theirs'}`);
      if (message.id) item.dataset.messageId = String(message.id);
      // A run: the same person again within a few minutes sits closer to the message before.
      const continues = message.senderUserId === lastSender && sent.getTime() - lastSentAt < RUN_GAP_MS;
      if (continues) {
        item.classList.add('is-continued');
        // Their earlier message in the run carries the same time: it shows once, at the end.
        // (Mine keep theirs: each has its own ticks.)
        if (previousItem && previousItem.classList.contains('is-theirs')) previousItem.classList.add('has-next');
      }
      lastSentAt = sent.getTime();
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
      const bubble = el('div', 'pm-chat__bubble');
      let cards = [];
      if (message.isUnsent) {
        bubble.textContent = mine ? 'You unsent a message' : 'Message unsent';
        item.classList.add('is-unsent');
      } else {
        // Links are anchors (built from text, never parsed as HTML); links to PRISM records get
        // a card when the server says this user may open the record.
        appendLinked(bubble, message.body || '', global.location.origin);
        // chat/actions.js: "@Name" marked for the people mentioned; a mention of me stands out.
        if (this.decorateMentions(bubble, message, win)) item.classList.add('is-mentioned');
        cards = this.cardsFor(message);
      }
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
        meta.title = fullTime(sent);
        if (message.editedUtc && !message.isUnsent) {
          const edited = el('span', 'pm-chat__edited', 'Edited');
          edited.title = `Edited ${fullTime(new Date(message.editedUtc))}`;
          meta.append(' · ', edited);
        }
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
      const files = this.attachmentNodes(win, message, mine);
      // A message of files only has no text bubble.
      if (!message.isUnsent && !message.body && files.length) bubble.hidden = true;
      item.append(bubble, ...files, ...cards, meta);
      bubble.title = meta.title || '';
      previousItem = message.failed || !message.id ? null : item;
      // chat/actions.js: the quote of what this replies to, its reactions, and its toolbar.
      const quote = this.replyQuote(win, message);
      if (quote) item.insertBefore(quote, bubble);
      const chips = this.reactionChips(win, message);
      if (chips) item.insertBefore(chips, meta);
      const actions = this.actionBar(win, message, mine, who);
      if (actions) {
        item.append(actions.node);
        if (actions.expiresIn) nextExpiry = Math.min(nextExpiry, actions.expiresIn);
      }
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
    } else if (options && options.force && divider) {
      // Opening with unread messages: start where they start, not below them.
      if (typeof divider.scrollIntoView === 'function') divider.scrollIntoView({ block: 'start' });
    } else if (options && options.stickToBottom && (nearBottom || options.force)) {
      // Only while the person is at the bottom, or on the first load: someone reading back
      // through older messages is not pulled down by a new message or the 30-second refresh.
      body.scrollTop = body.scrollHeight;
    }
  };

  ChatApp.prototype.unsend = async function (win, message) {
    // One request per message: a double click must not send a second, rate-limited one.
    if (this.unsending.has(message.id)) return;
    this.unsending.add(message.id);
    try {
      const result = await this.request('POST', `/messages/${message.id}/unsend`, {});
      this.applyUnsent(this.viewFor(win), result);
    } catch (error) {
      // The message object may have been replaced by a refresh meanwhile: mark the current one.
      const view = this.viewFor(win);
      const current = view.messages.find((m) => m.id === message.id) || message;
      current.unsendError = error.message;
      this.renderMessages(view, { stickToBottom: false });
    } finally {
      this.unsending.delete(message.id);
    }
  };

  ChatApp.prototype.send = function (win) {
    if (win.editing) {
      this.submitEdit(win); // chat/actions.js
      return;
    }
    const text = win.input.value.replace(/\r\n?/g, '\n').trim();
    const files = this.readyFiles(win);
    if (files.busy) {
      this.composeNote(win, 'Wait for the files to finish uploading, then send.');
      return;
    }
    if (!text && !files.attachments.length) return;
    if (text.length > this.maxLength) return;

    const message = {
      id: 0,
      clientMessageId: newClientId(),
      senderUserId: this.me,
      recipientUserId: win.groupId ? '' : win.peerId,
      groupId: win.groupId || null,
      body: text,
      sentUtc: new Date().toISOString(),
      attachments: files.attachments,
      attachmentIds: files.attachments.map((a) => a.id),
      replyToMessageId: win.replyTo || null,
      mentionedUserIds: win.groupId ? this.mentionsIn(win, text) : [],
    };
    win.replyTo = 0;
    win.mentionIds = new Map();
    this.renderComposing(win);
    this.clearSentFiles(win, files.attachments);
    // Replying: the person has caught up, so "New messages" and "↓ new" go.
    win.unreadFromId = 0;
    win.newBelow = 0;
    this.renderJump(win);
    win.input.value = '';
    win.input.style.height = 'auto';
    this.renderCounter(win);
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
          attachmentIds: message.attachmentIds || [],
          replyToMessageId: message.replyToMessageId || null,
          mentionedUserIds: message.mentionedUserIds || [],
        })
        : await this.request('POST', '/messages', {
          recipientUserId: win.peerId,
          clientMessageId: message.clientMessageId,
          body: message.body,
          attachmentIds: message.attachmentIds || [],
          replyToMessageId: message.replyToMessageId || null,
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
})(window);

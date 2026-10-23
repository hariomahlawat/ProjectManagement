/*
 * PRISM chat, one conversation: reading, history paging, drawing messages, sending.
 * One of the chat scripts loaded in order by _ChatScripts.cshtml (see chat/core.js for the list and
 * the server contract). Shared helpers come from PMChatParts, set up by chat/core.js.
 */
(function (global) {
  'use strict';

  const document = global.document;
  const {
    ChatApp, HISTORY_PAGE, CATCH_UP_PAGES, timeFormat, newClientId, dayKey,
    dayLabel, mergeMessages, fetchSince, seenBy, seenByEveryone, el,
    focusKeyIn, refocus, icon,
    newestId, appendLinked, previewLinks, previewCard,
  } = global.PMChatParts;

  const PREVIEW_BATCH = 10; // links per GET {api}/previews (the server's limit)
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
        const knownMax = newestId(win.messages);
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
    const newest = newestId(win.messages);
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
      const bubble = el('div', 'pm-chat__bubble');
      let cards = [];
      if (message.isUnsent) {
        bubble.textContent = mine ? 'You unsent a message' : 'Message unsent';
        item.classList.add('is-unsent');
      } else {
        // Links are anchors (built from text, never parsed as HTML); links to PRISM records get
        // a card when the server says this user may open the record.
        appendLinked(bubble, message.body || '', global.location.origin);
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
      item.append(bubble, ...cards, meta);
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
})(window);

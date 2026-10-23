/*
 * PRISM chat, what arrives: messages, reads, presence, unsends, status, groups.
 * One of the chat scripts loaded in order by _ChatScripts.cshtml (see chat/core.js for the list and
 * the server contract). Shared helpers come from PMChatParts, set up by chat/core.js.
 */
(function (global) {
  'use strict';

  const document = global.document;
  const {
    ChatApp, TYPING_SHOW_MS, groupKey,
  } = global.PMChatParts;

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

    const muted = this.isMuted(message.groupId);
    let win = this.windows.get(key);
    if (!win && countable && !muted) {
      // Classic behaviour: a new message pops its conversation open (not for a muted group).
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

    if (fresh && !muted && !(win && this.isAttended(win))) {
      this.alert(message, contact, group);
    }
    // A muted group is announced only while the person is in it.
    if (fresh && (!muted || (win && this.isAttended(win)))) this.announceMessage(message, win, group);

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
    if (!incoming.length || (this.isMuted(win.groupId) && !this.isAttended(win))) return;
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
    this.refreshMemberPanels(presence.userId);
  };

  /** Open people panels show presence and status: redraw those that list userId (or all). */
  ChatApp.prototype.refreshMemberPanels = function (userId) {
    this.windows.forEach((win) => {
      if (!win.groupId || !win.panelOpen) return;
      if (userId && !(win.members || []).some((m) => m.userId === userId)) return;
      this.renderMembersPanel(win);
    });
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
})(window);

/*
 * PRISM chat, conversation windows and the group people panel.
 * One of the chat scripts loaded in order by _ChatScripts.cshtml (see chat/core.js for the list and
 * the server contract). Shared helpers come from PMChatParts, set up by chat/core.js.
 */
(function (global) {
  'use strict';

  const document = global.document;
  const {
    ChatApp, TYPING_SEND_MS, GROUP_LIMIT, ARM_MS, windowCapacity, groupIdOf,
    peopleCount, typingLine, el, focusKeyIn, refocus, setText,
    iconButton, avatar, groupAvatar, setPresence, statusText,
  } = global.PMChatParts;

  let panelSerial = 0; // ids for aria-controls

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
      : (win.attention ? 'New message' : (win.removed ? 'You are not in this group' : peopleCount(count) + (this.isMuted(win.groupId) ? ' · Muted' : ''))));
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
      chosen: new Set(), filter: '', addOpen: false, rename: null, leaveArmed: false, armedRemove: '',
      armTimer: null, error: '', notice: '',
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
      const me = member.userId === this.me;
      const contact = this.contacts.get(member.userId);
      const item = el('li');
      const face = avatar(member);
      setPresence(face, me ? { isOnline: true, isActive: true } : (contact || { isActive: member.isActive }));
      const text = el('span', 'pm-chat__member-text');
      text.append(el('span', member.isActive ? 'pm-chat__member-name' : 'pm-chat__member-name is-inactive', me ? `${member.displayName} (you)` : member.displayName));
      const meta = member.isActive ? (me ? '' : statusText(contact)) : 'Inactive account';
      if (meta) text.append(el('span', 'pm-chat__member-meta', meta));
      item.append(face, text);
      if (member.userId === manager) item.append(el('span', 'pm-chat__badge-soft', 'manages'));
      if (!me && member.isActive) {
        const chat = iconButton('chat-dots', `Chat with ${member.displayName}`);
        chat.dataset.focusKey = `chat:${member.userId}`;
        chat.addEventListener('click', () => this.openWindow(member.userId, { focus: true }));
        item.append(chat);
      }
      if (iManage && !me) {
        // Two steps, like Leave: the first click asks, the second removes; it disarms itself.
        const armed = state.armedRemove === member.userId;
        const remove = el('button', `btn btn-link btn-sm p-0${armed ? ' text-danger' : ''}`, armed ? 'Confirm remove' : 'Remove');
        remove.type = 'button';
        remove.setAttribute('aria-label', armed ? `Confirm removing ${member.displayName}` : `Remove ${member.displayName}`);
        remove.dataset.focusKey = `remove:${member.userId}`;
        remove.addEventListener('click', () => {
          if (!armed) {
            this.armPanel(win, { armedRemove: member.userId });
            return;
          }
          this.groupAction(win, `/members/${encodeURIComponent(member.userId)}/remove`, {}, `Removed ${member.displayName}.`);
        });
        item.append(remove);
      }
      list.append(item);
    }

    const error = el('p', 'pm-chat__form-error', state.error);
    win.panelError = error;
    const notice = el('p', 'pm-chat__form-notice', state.notice);
    notice.setAttribute('aria-live', 'polite');

    const mute = el('label', 'form-check form-switch pm-chat__mute');
    const muteBox = el('input', 'form-check-input');
    muteBox.type = 'checkbox';
    muteBox.setAttribute('role', 'switch');
    muteBox.checked = this.isMuted(win.groupId);
    muteBox.dataset.focusKey = 'mute';
    muteBox.addEventListener('change', () => this.setMuted(win.groupId, muteBox.checked));
    mute.append(muteBox, el('span', 'form-check-label', 'Mute this group on this computer'));

    const parts = [head, notice, list, mute];

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
      if (!ids.length) {
        state.notice = 'Tick the people to add first.';
        this.renderMembersPanel(win);
        return;
      }
      const names = ids.map((id) => this.nameOf(id, win));
      this.groupAction(win, '/members', { userIds: ids }, `Added ${names.join(', ')}.`);
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
        const title = input.value.replace(/\s+/g, ' ').trim();
        this.groupAction(win, '/rename', { title: input.value }, `Renamed to “${title}”.`);
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
        this.armPanel(win, { leaveArmed: true });
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
    if (win.panelState && win.panelState.armTimer) global.clearTimeout(win.panelState.armTimer);
    win.panelOpen = false;
    win.panelState = null;
    this.renderMembersPanel(win);
    win.people.focus();
  };

  /**
   * Arms one two-step button (Leave, or Remove for one person) and disarms it again after a few
   * seconds, so a confirmation left showing cannot be clicked by accident much later.
   */
  ChatApp.prototype.armPanel = function (win, armed) {
    const state = win.panelState;
    if (!state) return;
    if (state.armTimer) global.clearTimeout(state.armTimer);
    state.leaveArmed = !!armed.leaveArmed;
    state.armedRemove = armed.armedRemove || '';
    state.armTimer = global.setTimeout(() => {
      if (win.panelState !== state) return;
      state.leaveArmed = false;
      state.armedRemove = '';
      state.armTimer = null;
      this.renderMembersPanel(win);
    }, ARM_MS);
    this.renderMembersPanel(win);
  };

  /**
   * Runs a group change; the pushed GroupChanged/GroupRemoved events update every view.
   * doneText, when given, is shown in the panel and announced once the server has accepted it.
   */
  ChatApp.prototype.groupAction = async function (win, path, body, doneText) {
    const state = win.panelState;
    try {
      const group = await this.request('POST', `/groups/${win.groupId}${path}`, body);
      if (state) {
        // Done: what was being added or typed is spent.
        if (state.armTimer) global.clearTimeout(state.armTimer);
        state.armTimer = null;
        state.armedRemove = '';
        state.leaveArmed = false;
        state.error = '';
        state.notice = doneText || '';
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
      if (doneText) this.announce(doneText);
    } catch (error) {
      if (error.status === 410) {
        this.onGroupRemoved(win.groupId);
      } else {
        if (state) {
          state.error = error.message;
          state.notice = '';
        }
        if (win.panelError) win.panelError.textContent = error.message;
        this.announce(error.message);
      }
    }
  };
})(window);

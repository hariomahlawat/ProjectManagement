/*
 * PRISM chat, the contact list: people, groups, status message, New group, chat buttons next to names.
 * One of the chat scripts loaded in order by _ChatScripts.cshtml (see chat/core.js for the list and
 * the server contract). Shared helpers come from PMChatParts, set up by chat/core.js.
 */
(function (global) {
  'use strict';

  const document = global.document;
  const {
    ChatApp, GROUP_LIMIT, PICKER_MAX, sortContacts, filterContacts, groupKey,
    peopleCount, el, focusKeyIn, refocus, icon, iconButton,
    avatar, groupAvatar, setPresence, statusText,
    countLabel, unreadBadge,
  } = global.PMChatParts;

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

    // Do not disturb: a choice of how long, or off.
    this.dndToggle = iconButton('bell', 'Do not disturb');
    this.dndToggle.setAttribute('aria-expanded', 'false');
    this.dndMenu = el('div', 'pm-chat__dnd');
    this.dndMenu.hidden = true;
    this.dndMenu.id = 'pm-chat-dnd-menu';
    this.dndMenu.setAttribute('role', 'group');
    this.dndMenu.setAttribute('aria-label', 'Do not disturb');
    this.dndToggle.setAttribute('aria-controls', this.dndMenu.id);
    this.dndToggle.addEventListener('click', () => this.toggleDndMenu());
    this.dndMenu.addEventListener('keydown', (event) => {
      if (event.key !== 'Escape') return;
      event.stopPropagation(); // not also the contact list's Esc
      this.toggleDndMenu(false);
      this.dndToggle.focus();
    });
    this.dndNote = el('p', 'pm-chat__dnd-note');
    this.dndNote.hidden = true;
    header.append(this.dndToggle);

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
    this.tips = this.buildTips();
    if (this.tips) scroller.append(this.tips);
    scroller.append(this.groupHeading, this.groupList, this.peopleHeading, this.list, this.emptyNote);

    this.offlineNote = el('p', 'pm-chat__offline');
    this.offlineNote.hidden = true;
    buddy.append(header, this.dndMenu, this.dndNote, this.offlineNote, statusWrap, searchWrap, this.groupForm, scroller);
    this.renderDnd();
    search.setAttribute('aria-keyshortcuts', 'Alt+C');
    search.title = 'Search people (Alt+C from anywhere in PRISM)';
    // From the search: Enter opens the first match, ↓ moves into the list.
    search.addEventListener('keydown', (event) => {
      if (event.key !== 'Enter' && event.key !== 'ArrowDown') return;
      const first = scroller.querySelector('.pm-chat__contact');
      if (!first) return;
      event.preventDefault();
      if (event.key === 'Enter') first.click();
      else first.focus();
    });
    // In the list: ↑ ↓ move between people and groups; ↑ from the first goes back to the search.
    scroller.addEventListener('keydown', (event) => {
      if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp') return;
      const buttons = Array.from(scroller.querySelectorAll('.pm-chat__contact'));
      const at = buttons.indexOf(event.target);
      if (at < 0) return;
      event.preventDefault();
      const to = at + (event.key === 'ArrowDown' ? 1 : -1);
      if (to < 0) search.focus();
      else if (to < buttons.length) buttons[to].focus();
    });
    this.buddy = buddy;
    this.newGroupButton = newGroup;
    this.bindContactDrop(scroller); // chat/files.js: files dropped on a person or group (when attachments are on)

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

  /**
   * First-open tips at the top of the contact list: what people would not find on their own.
   * Shown until "Got it", remembered per browser and user (prism.chat.tips.{userId}); never shown
   * when the browser cannot remember that, so it does not come back on every page.
   */
  ChatApp.prototype.buildTips = function () {
    const key = `prism.chat.tips.${this.me}`;
    try {
      if (global.localStorage.getItem(key)) return null;
    } catch (_) {
      return null;
    }

    const card = el('section', 'pm-chat__tips');
    card.setAttribute('aria-labelledby', 'pmChatTipsTitle');
    const title = el('h3', 'pm-chat__tips-title', 'Chat away! A few things worth knowing');
    title.id = 'pmChatTipsTitle';
    const list = el('ul', 'pm-chat__tips-list');
    const tip = (glyph, text) => {
      const item = el('li');
      item.append(icon(glyph), el('span', null, text));
      list.append(item);
    };
    tip('person', 'Click a name to say hello.');
    tip('chat-dots', 'See the chat icon next to a name anywhere in PRISM? It starts a chat with that person.');
    if (this.unsendMs > 0) {
      const minutes = Math.round(this.unsendMs / 60000);
      tip('arrow-counterclockwise', `Sent too soon? You can unsend for ${minutes} ${minutes === 1 ? 'minute' : 'minutes'}.`);
    }
    tip('pencil', 'Set a status above so people know where you are.');
    if (this.attachments.enabled) tip('paperclip', 'Share files with the paperclip in any chat.');
    tip('search', 'Your conversations are saved, so you can always scroll back or search them.');

    const actions = el('div', 'pm-chat__tips-actions');
    const gotIt = el('button', 'btn btn-primary btn-sm', 'Got it');
    gotIt.type = 'button';
    gotIt.addEventListener('click', () => {
      try { global.localStorage.setItem(key, 'done'); } catch (_) { /* gone for this page anyway */ }
      card.remove();
      this.tips = null;
      if (this.search) this.search.focus();
    });
    actions.append(gotIt);
    if (this.guideUrl) {
      const guide = el('a', 'btn btn-link btn-sm', 'Quick guide');
      guide.href = this.guideUrl;
      actions.append(guide);
    }
    card.append(title, list, actions);
    return card;
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

  /** Sets (or, with '', clears) the person's status message from code (do not disturb). */
  ChatApp.prototype.setStatusText = async function (text) {
    try {
      const status = await this.request('POST', '/status', { message: text });
      this.myStatus = (status && status.message) || null;
    } catch (_) {
      return; // the status stays as it was; do not disturb works without it
    }
    this.renderMyStatus();
  };

  /** The choices: for an hour, until 17:00 (earlier in the day), until tomorrow 09:00; or off. */
  ChatApp.prototype.toggleDndMenu = function (force) {
    const open = typeof force === 'boolean' ? force : this.dndMenu.hidden;
    this.dndMenu.hidden = !open;
    this.dndToggle.setAttribute('aria-expanded', String(open));
    if (!open) return;
    const now = new Date();
    const at = (days, hours) => {
      const d = new Date(now);
      d.setDate(d.getDate() + days);
      d.setHours(hours, 0, 0, 0);
      return d;
    };
    const choices = [{ key: 'hour', label: 'For 1 hour', until: new Date(now.getTime() + 3600000) }];
    if (now.getHours() < 16) choices.push({ key: 'evening', label: 'Until 17:00', until: at(0, 17) });
    choices.push({ key: 'tomorrow', label: 'Until tomorrow, 09:00', until: at(1, 9) });
    const buttons = choices.map((choice) => {
      const button = el('button', 'btn btn-sm btn-outline-secondary', choice.label);
      button.type = 'button';
      button.dataset.dnd = choice.key;
      button.addEventListener('click', () => {
        this.toggleDndMenu(false);
        this.setDnd(choice.until);
        this.dndToggle.focus();
      });
      return button;
    });
    if (this.isQuiet()) {
      const off = el('button', 'btn btn-sm btn-outline-primary', 'Turn off');
      off.type = 'button';
      off.dataset.dnd = 'off';
      off.addEventListener('click', () => {
        this.toggleDndMenu(false);
        this.setDnd(null);
        this.dndToggle.focus();
      });
      buttons.unshift(off);
    }
    const intro = el('p', 'pm-chat__dnd-intro', 'Quiet chat: no sound, pop-ups or tab flashing. Messages still arrive and count as unread.');
    this.dndMenu.replaceChildren(intro, ...buttons);
    buttons[0].focus();
  };

  /** The bell, the "Do not disturb until …" line and the chat button show the state. */
  ChatApp.prototype.renderDnd = function () {
    if (!this.dndToggle) return;
    const quiet = this.isQuiet();
    this.dndToggle.replaceChildren(icon(quiet ? 'bell-slash' : 'bell'));
    this.dndToggle.classList.toggle('is-on', quiet);
    const label = quiet ? 'Do not disturb is on: change or turn off' : 'Do not disturb';
    this.dndToggle.setAttribute('aria-label', label);
    this.dndToggle.title = label;
    this.dndNote.hidden = !quiet;
    if (quiet) {
      const until = new Date(this.dnd.until);
      const sameDay = until.toDateString() === new Date().toDateString();
      const when = new Intl.DateTimeFormat('en-IN', sameDay
        ? { hour: '2-digit', minute: '2-digit', hour12: false }
        : { weekday: 'short', hour: '2-digit', minute: '2-digit', hour12: false }).format(until);
      this.dndNote.replaceChildren(icon('bell-slash'), document.createTextNode(` Do not disturb until ${when}`));
    }
    this.renderLauncherBadge();
  };

  ChatApp.prototype.saveMyStatus = async function () {
    const text = this.statusInput.value.replace(/\s+/g, ' ').trim();
    // Enter saves and then leaves the box, whose blur saves too: one request for the same text.
    if (text === (this.myStatus || '') || text === this.statusSaving) return;
    this.statusSaving = text;
    try {
      const status = await this.request('POST', '/status', { message: text });
      this.myStatus = (status && status.message) || null;
    } catch (_) {
      // Leave what was typed; the next blur or Enter tries again.
      return;
    } finally {
      if (this.statusSaving === text) this.statusSaving = null;
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
      const since = this.unreadSeq;
      const contacts = await this.request('GET', '/contacts');
      const previous = this.contacts;
      const fresh = new Map(contacts.map((c) => [c.userId, this.keepLiveUnread(c, c.userId, previous.get(c.userId), since)]));
      this.contacts = fresh;
      this.renderContacts();
      this.refreshMemberPanels();
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
        button.append(unreadBadge(contact.unreadCount));
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
        button.append(unreadBadge(group.unreadCount));
      }
      const muted = this.isMuted(group.id);
      if (muted) {
        const bell = icon('bell-slash');
        bell.classList.add('pm-chat__muted');
        bell.title = 'Muted';
        button.append(bell);
      }
      button.setAttribute('aria-label', `Group ${group.title}, ${peopleCount(group.memberCount)}${muted ? ', muted' : ''}${group.unreadCount ? `, ${group.unreadCount} unread` : ''}`);
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
  ChatApp.prototype.peoplePicker = function (exclude, limit, state, onChange) {
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
    const more = el('p', 'pm-chat__picker-note');
    if (limit <= 0) {
      // Full: nothing to tick. Say so instead of offering boxes that each refuse.
      note.textContent = `This group is full (${GROUP_LIMIT} people).`;
      node.append(note);
      return { node, selected: () => [], focus: () => {} };
    }
    const draw = () => {
      const needle = filter.value.trim().toLowerCase();
      const matches = sortContacts(Array.from(this.contacts.values()))
        .filter((c) => c.isActive && !exclude.has(c.userId))
        .filter((c) => !needle || c.displayName.toLowerCase().includes(needle) || (c.rank || '').toLowerCase().includes(needle));
      const people = matches.slice(0, PICKER_MAX);
      more.textContent = matches.length > PICKER_MAX ? `Showing the first ${PICKER_MAX} of ${matches.length}. Search to find others.` : '';
      more.hidden = !more.textContent;
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
          if (onChange) onChange();
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
    node.append(filter, list, more, note);
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
      // The server checks these too; checking here first puts focus where the fix is.
      if (!title.value.trim()) {
        error.textContent = 'Give the group a name.';
        title.focus();
        return;
      }
      if (!picker.selected().length) {
        error.textContent = 'Tick at least one other person.';
        picker.focus();
        return;
      }
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
      if (count) count.textContent = countLabel(total);
    });
    const quiet = this.isQuiet() ? ', do not disturb is on' : '';
    this.launchers.forEach((b) => {
      b.setAttribute('aria-label', `${total ? `Open chat, ${total} unread` : 'Open chat'}${quiet}`);
      b.classList.toggle('is-quiet', !!quiet);
    });
    return total;
  };
})(window);

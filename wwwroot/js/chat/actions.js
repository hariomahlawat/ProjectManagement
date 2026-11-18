/*
 * PRISM chat, what can be done to a message (docs/chat.md, Phase 3): the message toolbar
 * (reactions, Reply, Copy, Edit), reply quotes, reaction chips, editing, and @mentions in groups.
 * One of the chat scripts loaded in order by _ChatScripts.cshtml (see chat/core.js for the list and
 * the server contract). Shared helpers come from PMChatParts, set up by chat/core.js.
 *
 * Server contract:
 *   POST {api}/messages/{id}/react   { kind, on }               -> { messageId, groupId, reactions }
 *   POST {api}/messages/{id}/edit    { body, mentionedUserIds } -> the message
 *   POST {api}/messages, {api}/groups/{id}/messages  also take replyToMessageId (and, in a
 *        group, mentionedUserIds)
 *   hub in: MessageEdited / GroupMessageEdited (message), ReactionsChanged ({ messageId, … })
 */
(function (global) {
  'use strict';

  const document = global.document;
  const { ChatApp, el, icon, mergeMessages } = global.PMChatParts;

  /** The reactions offered, in order (ChatReactionKind on the server). */
  const REACTIONS = [
    { kind: 'noted', symbol: '✓', label: 'Noted' },
    { kind: 'like', symbol: '👍', label: 'Like' },
    { kind: 'looking', symbol: '👀', label: 'Looking into it' },
  ];
  const QUOTE_LENGTH = 90;
  const MENTION_LIMIT = 6; // suggestions shown at once

  function reactionInfo(kind) {
    return REACTIONS.find((r) => r.kind === kind) || { kind, symbol: kind, label: kind };
  }

  function snippet(text) {
    const flat = (text || '').replace(/\s+/g, ' ').trim();
    return flat.length > QUOTE_LENGTH ? `${flat.slice(0, QUOTE_LENGTH)}…` : flat;
  }

  /** "Ravi", "Ravi and you", "Ravi, Sita and 2 others". */
  function listNames(names) {
    if (names.length <= 1) return names.join('');
    if (names.length <= 3) return `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`;
    return `${names.slice(0, 2).join(', ')} and ${names.length - 2} others`;
  }

  /** Copies text to the clipboard: the Clipboard API where the page may use it, else a fallback. */
  async function copyText(text) {
    if (global.isSecureContext && global.navigator && global.navigator.clipboard) {
      try {
        await global.navigator.clipboard.writeText(text);
        return true;
      } catch (_) { /* fall back */ }
    }
    // Plain http on the LAN has no Clipboard API: copy through a hidden text box.
    const area = document.createElement('textarea');
    area.value = text;
    area.setAttribute('readonly', '');
    area.style.position = 'fixed';
    area.style.opacity = '0';
    const back = document.activeElement;
    document.body.append(area);
    area.focus({ preventScroll: true });
    area.select();
    let done = false;
    try {
      done = document.execCommand('copy');
    } catch (_) {
      done = false;
    }
    area.remove();
    if (back && typeof back.focus === 'function') back.focus({ preventScroll: true });
    return done;
  }

  /** The "@fragment" being typed just before the caret, or null. */
  function mentionFragment(input) {
    const before = input.value.slice(0, input.selectionStart == null ? input.value.length : input.selectionStart);
    const match = /(^|\s)@([^\s@]{0,30})$/.exec(before);
    return match ? { query: match[2], start: before.length - match[2].length - 1, end: before.length } : null;
  }

  global.PMChatParts.mentionFragment = mentionFragment;
  global.PMChatParts.listNames = listNames;

  // ----- the message toolbar ----------------------------------------------------------------

  ChatApp.prototype.myReaction = function (message, kind) {
    const entry = (message.reactions || []).find((r) => r.kind === kind);
    return !!entry && entry.userIds.includes(this.me);
  };

  /**
   * The toolbar shown over a message on hover or keyboard focus: reactions, Reply, Copy and (own
   * message, in time) Edit. One tab stop (role=toolbar); ← → move between its buttons. Returns
   * { node, expiresIn } (ms until Edit runs out, to redraw then) or null.
   */
  ChatApp.prototype.actionBar = function (win, message, mine, who) {
    if (!message.id || message.isUnsent || message.eventKind) return null;
    const canWrite = !win.form.hidden;
    const bar = el('div', 'pm-chat__actions');
    bar.setAttribute('role', 'toolbar');
    bar.setAttribute('aria-label', `Actions for the message from ${mine ? 'you' : who}`);
    const buttons = [];
    const add = (text, label, key, handler, pressed) => {
      const button = el('button', 'pm-chat__action', text);
      button.type = 'button';
      button.setAttribute('aria-label', label);
      button.title = label;
      button.dataset.focusKey = key;
      if (pressed !== undefined) button.setAttribute('aria-pressed', String(pressed));
      button.addEventListener('click', handler);
      buttons.push(button);
      return button;
    };

    if (canWrite) {
      for (const reaction of REACTIONS) {
        const on = this.myReaction(message, reaction.kind);
        add(reaction.kind === 'noted' ? `${reaction.symbol} ${reaction.label}` : reaction.symbol,
          on ? `Take back your ${reaction.label} reaction` : `React: ${reaction.label}`,
          `react-${reaction.kind}:${message.id}`,
          () => this.toggleReaction(win, message.id, reaction.kind),
          on);
      }
      add('Reply', `Reply to the message from ${mine ? 'you' : who}`, `reply:${message.id}`, () => this.startReply(win, message.id));
    }
    let copy = null;
    if (message.body) {
      copy = add('Copy', `Copy message from ${mine ? 'you' : who}`, `copy:${message.id}`, async () => {
        const ok = await copyText(message.body);
        this.announce(ok ? 'Message copied.' : 'The message could not be copied. Select the text and press Ctrl+C.');
        copy.textContent = ok ? 'Copied' : 'Copy';
        if (ok) global.setTimeout(() => { if (copy.isConnected) copy.textContent = 'Copy'; }, 1500);
      });
    }
    let expiresIn = 0;
    if (mine && canWrite && this.editMs > 0 && message.body) {
      const left = this.editMs - (Date.now() - Date.parse(message.sentUtc));
      if (left > 0) {
        add('Edit', 'Edit your message', `edit:${message.id}`, () => this.startEdit(win, message.id));
        expiresIn = left;
      }
    }
    if (!buttons.length) return null;

    // Roving tab stop: Tab reaches the toolbar once; arrows move inside it.
    buttons.forEach((button, index) => { button.tabIndex = index === 0 ? 0 : -1; });
    bar.addEventListener('keydown', (event) => {
      if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight' && event.key !== 'Home' && event.key !== 'End') return;
      const at = buttons.indexOf(document.activeElement);
      if (at < 0) return;
      event.preventDefault();
      let to = at;
      if (event.key === 'ArrowRight') to = (at + 1) % buttons.length;
      else if (event.key === 'ArrowLeft') to = (at - 1 + buttons.length) % buttons.length;
      else if (event.key === 'Home') to = 0;
      else to = buttons.length - 1;
      buttons[at].tabIndex = -1;
      buttons[to].tabIndex = 0;
      buttons[to].focus();
    });
    bar.append(...buttons);
    return { node: bar, expiresIn };
  };

  // ----- reply quotes and reaction chips ------------------------------------------------------

  /**
   * The quote above a reply: who wrote the original and how it began, from the messages this
   * window holds (the server sends only the id, so nothing from before a member joined arrives
   * this way). An original not loaded says "An earlier message" and opens the conversation there.
   */
  ChatApp.prototype.replyQuote = function (win, message) {
    if (!message.replyToMessageId || message.isUnsent) return null;
    const original = win.messages.find((m) => m.id === message.replyToMessageId);
    const quote = el('button', 'pm-chat__quote');
    quote.type = 'button';
    quote.dataset.focusKey = `quote:${message.id}`;
    let who = '';
    let text;
    if (!original) {
      text = 'An earlier message';
    } else {
      who = this.nameOf(original.senderUserId, win);
      if (original.isUnsent) text = 'Message unsent';
      else if (original.body) text = snippet(original.body);
      else text = (original.attachments || []).length ? 'A file' : 'A message';
    }
    if (who) quote.append(el('span', 'pm-chat__quote-who', who));
    quote.append(el('span', 'pm-chat__quote-text', text));
    quote.setAttribute('aria-label', `In reply to ${who ? `${who}: ` : ''}${text}. Show it.`);
    quote.addEventListener('click', () => this.showMessage(win, message.replyToMessageId));
    return quote;
  };

  /** Scrolls to a message and highlights it; one not loaded is opened around (like a search hit). */
  ChatApp.prototype.showMessage = function (win, messageId) {
    const node = win.log.querySelector(`[data-message-id="${messageId}"]`);
    if (!node) {
      this.loadAround(win, messageId);
      return;
    }
    win.highlightId = messageId;
    this.renderMessages(win, { stickToBottom: false });
    const fresh = win.log.querySelector(`[data-message-id="${messageId}"]`);
    if (fresh && typeof fresh.scrollIntoView === 'function') fresh.scrollIntoView({ block: 'center' });
  };

  /** "✓ Noted 2  👍 1" under a message; each chip toggles the person's own reaction. */
  ChatApp.prototype.reactionChips = function (win, message) {
    const given = (message.reactions || []).filter((r) => r.userIds && r.userIds.length);
    if (!given.length || message.isUnsent) return null;
    const box = el('div', 'pm-chat__reactions');
    for (const reaction of given) {
      const info = reactionInfo(reaction.kind);
      const mine = reaction.userIds.includes(this.me);
      const names = reaction.userIds.map((id) => (id === this.me ? 'you' : this.nameOf(id, win)));
      const chip = el('button', `pm-chat__reaction${mine ? ' is-mine' : ''}`);
      chip.type = 'button';
      chip.dataset.focusKey = `chip-${reaction.kind}:${message.id}`;
      chip.append(
        el('span', 'pm-chat__reaction-symbol', reaction.kind === 'noted' ? `${info.symbol} ${info.label}` : info.symbol),
        el('span', 'pm-chat__reaction-count', String(reaction.userIds.length)));
      const who = listNames(names);
      chip.title = `${info.label}: ${who}`;
      chip.setAttribute('aria-pressed', String(mine));
      chip.setAttribute('aria-label', `${info.label} by ${who}.${win.form.hidden ? '' : mine ? ' Press to take yours back.' : ' Press to add yours.'}`);
      if (win.form.hidden) chip.disabled = true;
      else chip.addEventListener('click', () => this.toggleReaction(win, message.id, reaction.kind));
      box.append(chip);
    }
    return box;
  };

  /** The name a person is @mentioned by: their display name (never "You"), as the list offered it. */
  ChatApp.prototype.mentionName = function (userId, win) {
    const member = win && (win.members || []).find((m) => m.userId === userId);
    if (member) return member.displayName;
    const contact = this.contacts.get(userId);
    return contact ? contact.displayName : '';
  };

  /** Marks "@Name" for the people a group message mentions (the text itself is plain). */
  ChatApp.prototype.decorateMentions = function (bubble, message, win) {
    const ids = message.mentionedUserIds || [];
    if (!ids.length || message.isUnsent) return false;
    const names = ids
      .map((id) => ({ id, name: this.mentionName(id, win) }))
      .filter((m) => m.name)
      .sort((a, b) => b.name.length - a.name.length);
    const walker = document.createTreeWalker(bubble, global.NodeFilter.SHOW_TEXT);
    const texts = [];
    while (walker.nextNode()) texts.push(walker.currentNode);
    for (const node of texts) {
      if (node.parentNode && node.parentNode.closest && node.parentNode.closest('a')) continue;
      const text = node.nodeValue;
      const parts = [];
      let from = 0;
      for (let at = text.indexOf('@'); at >= 0; at = text.indexOf('@', at + 1)) {
        if (at < from) continue;
        const hit = names.find((m) => text.startsWith(`@${m.name}`, at));
        if (!hit) continue;
        if (at > from) parts.push(document.createTextNode(text.slice(from, at)));
        const span = el('span', `pm-chat__mention${hit.id === this.me ? ' is-me' : ''}`, `@${hit.name}`);
        parts.push(span);
        from = at + hit.name.length + 1;
      }
      if (!parts.length) continue;
      if (from < text.length) parts.push(document.createTextNode(text.slice(from)));
      node.replaceWith(...parts);
    }
    return ids.includes(this.me);
  };

  // ----- reacting ---------------------------------------------------------------------------

  /** The message with this id wherever this page holds it (every window, held live messages). */
  ChatApp.prototype.eachCopy = function (messageId, change) {
    this.windows.forEach((win) => {
      let touched = false;
      const apply = (list) => list.map((m) => {
        if (m.id !== messageId) return m;
        touched = true;
        return change(m);
      });
      win.messages = apply(win.messages);
      win.heldLive = apply(win.heldLive || []);
      if (touched) this.renderMessages(win, { stickToBottom: false });
    });
  };

  ChatApp.prototype.toggleReaction = async function (win, messageId, kind) {
    const view = this.viewFor(win);
    const message = view.messages.find((m) => m.id === messageId);
    if (!message) return;
    const on = !this.myReaction(message, kind);
    const before = message.reactions || [];
    // Shown at once; the server's answer (or a failure) settles it.
    this.eachCopy(messageId, (m) => ({ ...m, reactions: toggled(m.reactions || [], kind, this.me, on) }));
    try {
      const result = await this.request('POST', `/messages/${messageId}/react`, { kind, on });
      this.onReactions(result);
      const info = reactionInfo(kind);
      this.announce(on ? `Reacted: ${info.label}.` : `${info.label} taken back.`);
    } catch (error) {
      this.eachCopy(messageId, (m) => ({ ...m, reactions: before }));
      this.composeNote(this.viewFor(win), error.message);
    }
  };

  function toggled(reactions, kind, me, on) {
    const list = reactions.map((r) => ({ kind: r.kind, userIds: r.userIds.filter((id) => id !== me || r.kind !== kind) }));
    if (on) {
      const entry = list.find((r) => r.kind === kind);
      if (entry) entry.userIds.push(me);
      else list.push({ kind, userIds: [me] });
    }
    const order = REACTIONS.map((r) => r.kind);
    return list.filter((r) => r.userIds.length).sort((a, b) => order.indexOf(a.kind) - order.indexOf(b.kind));
  }

  /** ReactionsChanged, or the answer to this page's own reaction. */
  ChatApp.prototype.onReactions = function (change) {
    if (!change || !change.messageId) return;
    this.eachCopy(change.messageId, (m) => ({ ...m, reactions: change.reactions || [] }));
  };

  /** MessageEdited / GroupMessageEdited, or the answer to this page's own edit. */
  ChatApp.prototype.onEdited = function (message) {
    if (!message || !message.id || this.unsentIds.has(message.id)) return;
    this.windows.forEach((win) => {
      const held = win.messages.some((m) => m.id === message.id);
      const heldLive = (win.heldLive || []).some((m) => m.id === message.id);
      if (!held && !heldLive) return; // not loaded here: the next load brings the new text
      if (held) win.messages = mergeMessages(win.messages, [message]);
      if (heldLive) win.heldLive = win.heldLive.map((m) => (m.id === message.id ? message : m));
      this.renderMessages(win, { stickToBottom: false });
    });
  };

  // ----- replying and editing ---------------------------------------------------------------

  /** The line above the message box: "Replying to Ravi: …" or "Editing your message", with ×. */
  ChatApp.prototype.renderComposing = function (win) {
    if (!win.composing) {
      win.composing = el('div', 'pm-chat__composing');
      win.composing.hidden = true;
      win.node.insertBefore(win.composing, win.form);
    }
    const bar = win.composing;
    const editing = win.editing ? win.messages.find((m) => m.id === win.editing.id) : null;
    const replying = win.replyTo ? win.messages.find((m) => m.id === win.replyTo) : null;
    if (win.editing && !editing) win.editing = null;
    if (win.replyTo && !replying) win.replyTo = 0;
    bar.hidden = !win.editing && !win.replyTo;
    win.node.classList.toggle('is-editing', !!win.editing);
    if (bar.hidden) {
      bar.replaceChildren();
      return;
    }
    const text = el('span', 'pm-chat__composing-text');
    if (win.editing) {
      text.append(icon('pencil'), el('strong', null, ' Editing your message'));
    } else {
      const who = replying.senderUserId === this.me ? 'yourself' : this.nameOf(replying.senderUserId, win);
      text.append(icon('reply'), el('strong', null, ` Replying to ${who}: `), document.createTextNode(replying.isUnsent ? 'Message unsent' : snippet(replying.body) || 'A file'));
    }
    const cancel = el('button', 'pm-chat__icon-btn pm-chat__composing-cancel');
    cancel.type = 'button';
    cancel.setAttribute('aria-label', win.editing ? 'Cancel editing' : 'Cancel the reply');
    cancel.title = win.editing ? 'Cancel editing (Esc)' : 'Cancel the reply (Esc)';
    cancel.append(icon('x-lg'));
    cancel.addEventListener('click', () => this.cancelComposing(win));
    bar.replaceChildren(text, cancel);
  };

  ChatApp.prototype.startReply = function (win, messageId) {
    if (win.editing) this.cancelComposing(win);
    win.replyTo = messageId;
    this.renderComposing(win);
    win.input.focus({ preventScroll: true });
  };

  ChatApp.prototype.startEdit = function (win, messageId) {
    const message = win.messages.find((m) => m.id === messageId);
    if (!message) return;
    if (!win.editing) win.draftBeforeEdit = { text: win.input.value, replyTo: win.replyTo, mentions: new Map(win.mentionIds || []) };
    win.replyTo = 0;
    win.editing = { id: messageId };
    win.mentionIds = new Map((message.mentionedUserIds || []).map((id) => [this.mentionName(id, win), id]));
    win.input.value = message.body;
    win.input.dispatchEvent(new global.Event('input'));
    this.renderComposing(win);
    win.input.focus({ preventScroll: true });
  };

  /** Esc or ×: stop replying, or stop editing and bring back what was being typed before. */
  ChatApp.prototype.cancelComposing = function (win) {
    if (win.editing) {
      const draft = win.draftBeforeEdit || { text: '', replyTo: 0, mentions: new Map() };
      win.editing = null;
      win.input.value = draft.text;
      win.replyTo = draft.replyTo || 0;
      win.mentionIds = draft.mentions;
      win.draftBeforeEdit = null;
      win.input.dispatchEvent(new global.Event('input'));
    } else {
      win.replyTo = 0;
    }
    this.renderComposing(win);
    if (!win.form.hidden) win.input.focus({ preventScroll: true });
  };

  /** The people @mentioned in the text about to be sent: chosen from the list and still in it. */
  ChatApp.prototype.mentionsIn = function (win, text) {
    const ids = [];
    (win.mentionIds || new Map()).forEach((id, name) => {
      if (name && text.includes(`@${name}`) && !ids.includes(id)) ids.push(id);
    });
    return ids;
  };

  /** Sends the edit being made (Enter or Send while editing). */
  ChatApp.prototype.submitEdit = async function (win) {
    const id = win.editing.id;
    const message = win.messages.find((m) => m.id === id);
    const text = win.input.value.replace(/\r\n?/g, '\n').trim();
    if (!message) {
      this.cancelComposing(win);
      return;
    }
    if (!text && !(message.attachments || []).length) {
      this.composeNote(win, 'A message needs some text. To take it back, use Unsend.');
      return;
    }
    const mentioned = win.groupId ? this.mentionsIn(win, text) : [];
    if (text === message.body && sameIds(mentioned, message.mentionedUserIds || [])) {
      this.cancelComposing(win);
      return;
    }
    try {
      const saved = await this.request('POST', `/messages/${id}/edit`, { body: text, mentionedUserIds: mentioned });
      const view = this.viewFor(win);
      if (view.editing && view.editing.id === id) this.cancelComposing(view);
      this.onEdited(saved);
      this.announce('Message edited.');
    } catch (error) {
      this.composeNote(this.viewFor(win), error.message);
    }
  };

  function sameIds(a, b) {
    return a.length === b.length && a.every((id) => b.includes(id));
  }

  // ----- the message box: keys, and @mention suggestions -------------------------------------

  /** Sets up a window's message box for replies, edits and (in a group) @mentions. */
  ChatApp.prototype.bindComposer = function (win) {
    win.replyTo = 0;
    win.editing = null;
    win.mentionIds = new Map();
    if (!win.groupId) return;
    const list = el('ul', 'pm-chat__mention-list');
    list.id = `pm-chat-mentions-${win.groupId}-${Math.random().toString(36).slice(2, 8)}`;
    list.setAttribute('role', 'listbox');
    list.setAttribute('aria-label', 'People to mention');
    list.hidden = true;
    win.mentionList = list;
    win.mentionState = null;
    win.form.insertBefore(list, win.form.firstChild);
    win.input.setAttribute('aria-autocomplete', 'list');
    win.input.addEventListener('input', () => this.suggestMentions(win));
    win.input.addEventListener('blur', () => global.setTimeout(() => this.closeMentions(win), 150));
  };

  ChatApp.prototype.suggestMentions = function (win) {
    const fragment = mentionFragment(win.input);
    if (!fragment) {
      this.closeMentions(win);
      return;
    }
    const query = fragment.query.toLowerCase();
    const people = (win.members || [])
      .filter((m) => m.userId !== this.me && m.isActive !== false)
      .filter((m) => !query || m.displayName.toLowerCase().split(/\s+/).some((part) => part.startsWith(query)) || m.displayName.toLowerCase().includes(query))
      .slice(0, MENTION_LIMIT);
    if (!people.length) {
      this.closeMentions(win);
      return;
    }
    const keep = win.mentionState && people.some((p) => p.userId === win.mentionState.active) ? win.mentionState.active : people[0].userId;
    win.mentionState = { fragment, people, active: keep };
    this.renderMentions(win);
  };

  ChatApp.prototype.renderMentions = function (win) {
    const state = win.mentionState;
    const list = win.mentionList;
    list.hidden = !state;
    win.input.setAttribute('aria-expanded', String(!!state));
    if (!state) {
      list.replaceChildren();
      win.input.removeAttribute('aria-activedescendant');
      win.input.removeAttribute('aria-controls');
      return;
    }
    win.input.setAttribute('aria-controls', list.id);
    list.replaceChildren(...state.people.map((person) => {
      const option = el('li', `pm-chat__mention-option${person.userId === state.active ? ' is-active' : ''}`, person.displayName);
      option.id = `${list.id}-${person.userId.replace(/[^\w-]/g, '_')}`;
      option.setAttribute('role', 'option');
      option.setAttribute('aria-selected', String(person.userId === state.active));
      // mousedown, not click: the box keeps focus (and its caret) while choosing.
      option.addEventListener('mousedown', (event) => {
        event.preventDefault();
        this.chooseMention(win, person);
      });
      return option;
    }));
    const active = list.querySelector('.is-active');
    if (active) win.input.setAttribute('aria-activedescendant', active.id);
  };

  ChatApp.prototype.closeMentions = function (win) {
    if (!win.mentionState) return;
    win.mentionState = null;
    this.renderMentions(win);
  };

  ChatApp.prototype.chooseMention = function (win, person) {
    const state = win.mentionState;
    if (!state) return;
    const value = win.input.value;
    const insert = `@${person.displayName} `;
    win.input.value = value.slice(0, state.fragment.start) + insert + value.slice(state.fragment.end);
    const caret = state.fragment.start + insert.length;
    if (typeof win.input.setSelectionRange === 'function') win.input.setSelectionRange(caret, caret);
    win.mentionIds.set(person.displayName, person.userId);
    this.closeMentions(win);
    win.input.dispatchEvent(new global.Event('input'));
    win.input.focus({ preventScroll: true });
  };

  /**
   * Keys in the message box before the window's own handling (chat/windows.js): the mention
   * list takes ↑ ↓ Enter Tab Esc while open; Esc then cancels a reply or an edit. Returns true
   * when the key was used here.
   */
  ChatApp.prototype.composerKey = function (win, event) {
    const state = win.mentionState;
    if (state) {
      const at = state.people.findIndex((p) => p.userId === state.active);
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        const to = (at + (event.key === 'ArrowDown' ? 1 : -1) + state.people.length) % state.people.length;
        state.active = state.people[to].userId;
        this.renderMentions(win);
        return true;
      }
      if ((event.key === 'Enter' && !event.shiftKey) || event.key === 'Tab') {
        event.preventDefault();
        this.chooseMention(win, state.people[Math.max(0, at)]);
        return true;
      }
      if (event.key === 'Escape') {
        event.preventDefault();
        event.stopPropagation();
        this.closeMentions(win);
        return true;
      }
    }
    if (event.key === 'Escape' && (win.editing || win.replyTo)) {
      event.preventDefault();
      event.stopPropagation(); // not also the window's Esc (minimise)
      this.cancelComposing(win);
      return true;
    }
    if (event.key === 'ArrowUp' && !win.input.value && !win.editing && !win.form.hidden && this.editMs > 0) {
      // ↑ in an empty box edits your last message, while it can still be edited.
      const last = win.messages.slice().reverse().find((m) => m.id && m.senderUserId === this.me && !m.isUnsent && !m.eventKind && m.body);
      if (last && this.editMs - (Date.now() - Date.parse(last.sentUtc)) > 0) {
        event.preventDefault();
        this.startEdit(win, last.id);
        return true;
      }
    }
    return false;
  };
})(window);

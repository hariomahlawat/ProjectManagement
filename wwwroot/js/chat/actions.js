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
  const RECORD_DELAY_MS = 250; // typing pause before #record suggestions are asked for

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
    // text: shown as is (reactions), or "icon:name" for an icon button (its label is its name).
    const add = (text, label, key, handler, pressed) => {
      const isIcon = text.startsWith('icon:');
      const button = el('button', `pm-chat__action${isIcon ? ' is-icon' : ''}`, isIcon ? null : text);
      if (isIcon) button.append(icon(text.slice('icon:'.length)));
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
      add('icon:reply', `Reply to the message from ${mine ? 'you' : who}`, `reply:${message.id}`, () => this.startReply(win, message.id));
    }
    let copy = null;
    if (message.body) {
      copy = add('icon:copy', `Copy message from ${mine ? 'you' : who}`, `copy:${message.id}`, async () => {
        const ok = await copyText(message.body);
        this.announce(ok ? 'Message copied.' : 'The message could not be copied. Select the text and press Ctrl+C.');
        if (!ok) return;
        copy.replaceChildren(icon('check2'));
        copy.classList.add('is-done');
        global.setTimeout(() => {
          if (!copy.isConnected) return;
          copy.replaceChildren(icon('copy'));
          copy.classList.remove('is-done');
        }, 1500);
      });
    }
    if (this.notebookApi && (message.body || (message.attachments || []).length)) {
      add('icon:alarm', 'Remind me about this message (saved in your Notebook)', `remind:${message.id}`, () => this.openReminder(win, message.id));
    }
    let expiresIn = 0;
    if (mine && canWrite && this.editMs > 0 && message.body) {
      const left = this.editMs - (Date.now() - Date.parse(message.sentUtc));
      if (left > 0) {
        add('icon:pencil', 'Edit your message', `edit:${message.id}`, () => this.startEdit(win, message.id));
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
    const shown = toggled(before, kind, this.me, on);
    this.noteMessageEvent(messageId, { reactions: shown });
    this.eachCopy(messageId, (m) => ({ ...m, reactions: shown }));
    // A ReactionsChanged arriving before this request's answer is newer than the answer (the
    // server pushes it after this change was stored, to this page too): it is kept.
    const mark = this.messageEventSeq;
    try {
      const result = await this.request('POST', `/messages/${messageId}/react`, { kind, on });
      if (!this.changedSince(messageId, 'reactions', mark)) this.onReactions(result);
      const info = reactionInfo(kind);
      this.announce(on ? `Reacted: ${info.label}.` : `${info.label} taken back.`);
    } catch (error) {
      // Not stored: back to how it was, unless the server has told this page the list since.
      if (!this.changedSince(messageId, 'reactions', mark)) {
        this.noteMessageEvent(messageId, { reactions: before });
        this.eachCopy(messageId, (m) => ({ ...m, reactions: before }));
      }
      this.composeNote(this.viewFor(win), error.message);
    }
  };

  /** Whether a part of a message ('text' or 'reactions') changed live after the numbering stood at mark. */
  ChatApp.prototype.changedSince = function (messageId, part, mark) {
    const at = this.messageEventAt.get(messageId);
    return !!(at && at[part] && at[part].seq > mark);
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

  /**
   * A live change to part of a message, with its new content: change.text ({ body, editedUtc,
   * mentionedUserIds }) and/or change.reactions (the list). Numbered (messageEventSeq), so any
   * answer from the server asked for before it cannot put the older part back (settleFetched),
   * whether or not the message was on screen when the change arrived.
   */
  ChatApp.prototype.noteMessageEvent = function (messageId, change) {
    this.messageEventSeq += 1;
    const at = { ...(this.messageEventAt.get(messageId) || {}) };
    if (change.text) at.text = { seq: this.messageEventSeq, ...change.text };
    if (change.reactions) at.reactions = { seq: this.messageEventSeq, list: change.reactions };
    this.messageEventAt.set(messageId, at);
  };

  /**
   * Messages from the server, asked for when the live numbering stood at eventsBefore (history,
   * catch-up, the state check, the answer to a send; 0 for a pushed copy): a part that changed
   * live since keeps its live content. A part taken from the answer is remembered as the newest
   * known, numbered by when it was asked for, so answers asked for earlier still give way to
   * it, later ones are not held back, and a pushed copy never brings older content back.
   */
  ChatApp.prototype.settleFetched = function (messages, eventsBefore) {
    if (!this.messageEventAt.size) return messages;
    return (messages || []).map((m) => {
      const at = m && m.id ? this.messageEventAt.get(m.id) : null;
      if (!at || m.isUnsent) return m;
      let next = m;
      const remembered = { ...at };
      if (at.text && at.text.seq > eventsBefore) {
        next = { ...next, body: at.text.body, editedUtc: at.text.editedUtc, mentionedUserIds: at.text.mentionedUserIds };
      } else if (at.text) {
        remembered.text = { seq: eventsBefore, body: m.body, editedUtc: m.editedUtc, mentionedUserIds: m.mentionedUserIds };
      }
      if (at.reactions && at.reactions.seq > eventsBefore) {
        next = { ...next, reactions: at.reactions.list };
      } else if (at.reactions) {
        remembered.reactions = { seq: eventsBefore, list: m.reactions || [] };
      }
      this.messageEventAt.set(m.id, remembered);
      return next;
    });
  };

  /** ReactionsChanged, or the answer to this page's own reaction. */
  ChatApp.prototype.onReactions = function (change) {
    if (!change || !change.messageId) return;
    this.noteMessageEvent(change.messageId, { reactions: change.reactions || [] });
    this.eachCopy(change.messageId, (m) => ({ ...m, reactions: change.reactions || [] }));
  };

  /** MessageEdited / GroupMessageEdited, or the answer to this page's own edit. */
  /**
   * A pushed edit (MessageEdited, GroupMessageEdited) is live for its text only: the reaction
   * list it carries was read when the edit was saved, and a reaction saved meanwhile may already
   * have been pushed (ReactionsChanged is the only source of live reactions). So a pushed edit
   * changes the text, edit time and mentions of the copies on screen and leaves their reactions.
   * `answeredSince` is set for the answer to this page's own edit, asked for when the live
   * numbering stood there: it is settled per part like any other answer (settleFetched), and
   * only the parts taken from it are recorded.
   */
  ChatApp.prototype.onEdited = function (incoming, answeredSince) {
    if (!incoming || !incoming.id || this.unsentIds.has(incoming.id)) return;
    const id = incoming.id;
    const isAnswer = typeof answeredSince === 'number';
    const message = isAnswer ? this.settleFetched([incoming], answeredSince)[0] : incoming;
    const text = { body: message.body, editedUtc: message.editedUtc, mentionedUserIds: message.mentionedUserIds };
    const change = {};
    if (!isAnswer || !this.changedSince(id, 'text', answeredSince)) change.text = text;
    if (isAnswer && !this.changedSince(id, 'reactions', answeredSince)) change.reactions = message.reactions || [];
    if (change.text || change.reactions) this.noteMessageEvent(id, change);
    // An answer replaces the copy (settled above); a push changes only the text of it.
    const update = (m) => (isAnswer ? message : { ...m, ...text });
    this.windows.forEach((win) => {
      const held = win.messages.some((m) => m.id === id);
      const heldLive = (win.heldLive || []).some((m) => m.id === id);
      if (!held && !heldLive) return; // not loaded here: the next load brings the new text
      if (held) {
        win.messages = isAnswer
          ? mergeMessages(win.messages, [message])
          : win.messages.map((m) => (m.id === id ? update(m) : m));
      }
      if (heldLive) win.heldLive = win.heldLive.map((m) => (m.id === id ? update(m) : m));
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
    // A newer edit of the same message (from another tab) arriving before this answer is kept.
    const mark = this.messageEventSeq;
    try {
      const saved = await this.request('POST', `/messages/${id}/edit`, { body: text, mentionedUserIds: mentioned });
      const view = this.viewFor(win);
      if (view.editing && view.editing.id === id) this.cancelComposing(view);
      // Settled per part: newer live text (another tab) or reactions (someone reacting) stay.
      this.onEdited(saved, mark);
      this.announce('Message edited.');
    } catch (error) {
      this.composeNote(this.viewFor(win), error.message);
    }
  };

  function sameIds(a, b) {
    return a.length === b.length && a.every((id) => b.includes(id));
  }

  // ----- the message box: @mention and #record suggestions ----------------------------------

  /**
   * Sets up a window's message box for replies, edits and suggestions: @mentions (groups) and
   * #records (PRISM's own search suggestions, which apply the person's permissions).
   */
  ChatApp.prototype.bindComposer = function (win) {
    win.replyTo = 0;
    win.editing = null;
    win.mentionIds = new Map();
    if (!win.groupId && !this.recordSearchUrl) return;
    const list = el('ul', 'pm-chat__mention-list');
    list.id = `pm-chat-suggest-${(win.peerId || '').replace(/[^\w-]/g, '_')}-${Math.random().toString(36).slice(2, 8)}`;
    list.setAttribute('role', 'listbox');
    list.hidden = true;
    win.mentionList = list;
    win.mentionState = null;
    win.form.insertBefore(list, win.form.firstChild);
    win.input.setAttribute('aria-autocomplete', 'list');
    win.input.addEventListener('input', () => this.suggestMentions(win));
    win.input.addEventListener('blur', () => global.setTimeout(() => this.closeMentions(win), 150));
  };

  /** What is being typed before the caret: an @mention (groups) first, else a #record. */
  ChatApp.prototype.suggestMentions = function (win) {
    const mention = win.groupId ? mentionFragment(win.input) : null;
    if (mention) {
      const query = mention.query.toLowerCase();
      const people = (win.members || [])
        .filter((m) => m.userId !== this.me && m.isActive !== false)
        .filter((m) => !query || m.displayName.toLowerCase().split(/\s+/).some((part) => part.startsWith(query)) || m.displayName.toLowerCase().includes(query))
        .slice(0, MENTION_LIMIT)
        .map((m) => ({ key: m.userId, label: m.displayName, person: m }));
      this.showSuggestions(win, 'mention', mention, people);
      return;
    }
    const record = this.recordSearchUrl ? recordFragment(win.input) : null;
    if (!record) {
      this.closeMentions(win);
      return;
    }
    this.searchRecords(win, record);
  };

  ChatApp.prototype.showSuggestions = function (win, type, fragment, items) {
    if (!items.length) {
      this.closeMentions(win);
      return;
    }
    const previous = win.mentionState;
    const keep = previous && previous.type === type && items.some((i) => i.key === previous.active) ? previous.active : items[0].key;
    win.mentionState = { type, fragment, items, active: keep };
    this.renderMentions(win);
  };

  /** #records: asks PRISM's search suggestions (debounced, remembered per page). */
  ChatApp.prototype.searchRecords = function (win, fragment) {
    const query = fragment.query.trim();
    if (win.recordTimer) global.clearTimeout(win.recordTimer);
    const cache = this.recordCache || (this.recordCache = new Map());
    const show = (records) => {
      // Only if the box still asks for the same thing.
      const now = recordFragment(win.input);
      if (!now || now.query.trim() !== query) return;
      this.showSuggestions(win, 'record', now, records.map((r) => ({
        key: r.url,
        label: r.title || 'Untitled',
        detail: [r.category || r.sourceModule, r.identifier || r.subtitle].filter(Boolean).join(' · '),
        record: r,
      })));
    };
    if (cache.has(query)) {
      show(cache.get(query));
      return;
    }
    win.recordTimer = global.setTimeout(async () => {
      win.recordTimer = null;
      let records = [];
      try {
        const separator = this.recordSearchUrl.includes('?') ? '&' : '?';
        const response = await global.fetch(`${this.recordSearchUrl}${separator}q=${encodeURIComponent(query)}`, {
          headers: { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' },
          credentials: 'same-origin',
        });
        if (!response.ok) throw new Error(String(response.status));
        const payload = await response.json();
        records = (Array.isArray(payload) ? payload : [])
          .map((r) => ({ ...r, url: sameOrigin(r.url) }))
          .filter((r) => r.url);
      } catch (_) {
        records = []; // search unavailable: the box simply offers nothing
      }
      cache.set(query, records);
      if (cache.size > 30) cache.delete(cache.keys().next().value);
      show(records);
    }, RECORD_DELAY_MS);
  };

  /** A suggestion's link as an absolute URL on this PRISM server, or '' for anything else. */
  function sameOrigin(url) {
    if (typeof url !== 'string' || !url) return '';
    try {
      const parsed = new URL(url, global.location.origin);
      return parsed.origin === global.location.origin && (parsed.protocol === 'http:' || parsed.protocol === 'https:') ? parsed.href : '';
    } catch (_) {
      return '';
    }
  }

  /** The "#words" being typed before the caret (at least two characters), or null. */
  function recordFragment(input) {
    const before = input.value.slice(0, input.selectionStart == null ? input.value.length : input.selectionStart);
    const match = /(^|\s)#([^\s#][^#\n]{1,39})$/.exec(before);
    return match ? { query: match[2], start: before.length - match[2].length - 1, end: before.length } : null;
  }

  global.PMChatParts.recordFragment = recordFragment;

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
    list.setAttribute('aria-label', state.type === 'mention' ? 'People to mention' : 'PRISM records to link');
    win.input.setAttribute('aria-controls', list.id);
    list.replaceChildren(...state.items.map((item, index) => {
      const option = el('li', `pm-chat__mention-option${item.key === state.active ? ' is-active' : ''}`);
      option.append(el('span', 'pm-chat__suggest-label', item.label));
      if (item.detail) option.append(el('span', 'pm-chat__suggest-detail', item.detail));
      option.id = `${list.id}-${index}`;
      option.setAttribute('role', 'option');
      option.setAttribute('aria-selected', String(item.key === state.active));
      // mousedown, not click: the box keeps focus (and its caret) while choosing.
      option.addEventListener('mousedown', (event) => {
        event.preventDefault();
        this.chooseSuggestion(win, item);
      });
      return option;
    }));
    const active = list.querySelector('.is-active');
    if (active) win.input.setAttribute('aria-activedescendant', active.id);
  };

  ChatApp.prototype.closeMentions = function (win) {
    if (win.recordTimer) {
      global.clearTimeout(win.recordTimer);
      win.recordTimer = null;
    }
    if (!win.mentionState) return;
    win.mentionState = null;
    this.renderMentions(win);
  };

  /** A person becomes "@Name "; a record becomes its link (which shows as a preview card). */
  ChatApp.prototype.chooseSuggestion = function (win, item) {
    const state = win.mentionState;
    if (!state) return;
    const value = win.input.value;
    const insert = state.type === 'mention' ? `@${item.label} ` : `${item.key} `;
    win.input.value = value.slice(0, state.fragment.start) + insert + value.slice(state.fragment.end);
    const caret = state.fragment.start + insert.length;
    if (typeof win.input.setSelectionRange === 'function') win.input.setSelectionRange(caret, caret);
    if (state.type === 'mention') win.mentionIds.set(item.label, item.key);
    else this.announce(`Linked ${item.label}.`);
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
      const at = state.items.findIndex((i) => i.key === state.active);
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        const to = (at + (event.key === 'ArrowDown' ? 1 : -1) + state.items.length) % state.items.length;
        state.active = state.items[to].key;
        this.renderMentions(win);
        return true;
      }
      if ((event.key === 'Enter' && !event.shiftKey) || event.key === 'Tab') {
        event.preventDefault();
        this.chooseSuggestion(win, state.items[Math.max(0, at)]);
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
  // ----- "Remind me": a reminder in the person's own Notebook ----------------------------------

  const reminderFormat = new Intl.DateTimeFormat('en-IN', { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit', hour12: false });

  /** The preset times, worked out from now (the browser's clock and time zone). */
  function reminderPresets(now) {
    const at = (days, hours, minutes) => {
      const d = new Date(now);
      d.setDate(d.getDate() + days);
      d.setHours(hours, minutes || 0, 0, 0);
      return d;
    };
    const presets = [{ key: 'hour', label: 'In 1 hour', when: new Date(now.getTime() + 3600000) }];
    if (now.getHours() < 16) presets.push({ key: 'evening', label: 'This evening, 17:00', when: at(0, 17) });
    presets.push({ key: 'tomorrow', label: 'Tomorrow, 09:00', when: at(1, 9) });
    const toMonday = ((8 - now.getDay()) % 7) || 7;
    if (toMonday > 1) presets.push({ key: 'monday', label: 'Monday, 09:00', when: at(toMonday, 9) });
    return presets;
  }

  global.PMChatParts.reminderPresets = reminderPresets;

  /** "2026-11-19T09:00" for a datetime-local box, in the browser's time. */
  function localInputValue(date) {
    const pad = (n) => String(n).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  }

  /** The panel above the message box: when to be reminded about one message. */
  ChatApp.prototype.openReminder = function (win, messageId) {
    const message = win.messages.find((m) => m.id === messageId);
    if (!message) return;
    this.closeReminder(win);
    const panel = el('div', 'pm-chat__reminder');
    panel.setAttribute('role', 'group');
    const heading = el('p', 'pm-chat__reminder-title');
    heading.id = `pm-chat-remind-${messageId}-${Math.random().toString(36).slice(2, 8)}`;
    heading.append(icon('alarm'), document.createTextNode(' Remind me about this message'));
    panel.setAttribute('aria-labelledby', heading.id);
    const presets = el('div', 'pm-chat__reminder-presets');
    for (const preset of reminderPresets(new Date())) {
      const button = el('button', 'btn btn-sm btn-outline-secondary', preset.label);
      button.type = 'button';
      button.dataset.reminderPreset = preset.key;
      button.addEventListener('click', () => this.saveReminder(win, message, preset.when));
      presets.append(button);
    }
    const custom = el('form', 'pm-chat__reminder-custom');
    const label = el('label', 'visually-hidden', 'Date and time');
    const when = el('input', 'form-control form-control-sm');
    when.type = 'datetime-local';
    when.id = `${heading.id}-when`;
    label.htmlFor = when.id;
    when.min = localInputValue(new Date());
    when.value = localInputValue(new Date(Date.now() + 2 * 3600000));
    const save = el('button', 'btn btn-sm btn-primary', 'Save');
    save.type = 'submit';
    custom.append(label, when, save);
    custom.addEventListener('submit', (event) => {
      event.preventDefault();
      const chosen = when.value ? new Date(when.value) : null;
      if (!chosen || Number.isNaN(chosen.getTime()) || chosen.getTime() <= Date.now()) {
        this.reminderNote(win, 'Choose a time later than now.', true);
        when.focus();
        return;
      }
      this.saveReminder(win, message, chosen);
    });
    const close = el('button', 'pm-chat__icon-btn pm-chat__reminder-close');
    close.type = 'button';
    close.setAttribute('aria-label', 'Close without a reminder');
    close.append(icon('x-lg'));
    close.addEventListener('click', () => this.closeReminder(win, true));
    const note = el('p', 'pm-chat__reminder-note');
    note.hidden = true;
    panel.append(close, heading, presets, custom, note);
    panel.addEventListener('keydown', (event) => {
      if (event.key !== 'Escape') return;
      event.stopPropagation(); // not also the window's Esc (minimise)
      this.closeReminder(win, true);
    });
    win.reminder = { panel, note, messageId };
    win.node.insertBefore(panel, win.form);
    const first = presets.querySelector('button');
    if (first) first.focus();
  };

  ChatApp.prototype.closeReminder = function (win, refocus) {
    if (!win.reminder) return;
    if (win.reminder.timer) global.clearTimeout(win.reminder.timer);
    win.reminder.panel.remove();
    const messageId = win.reminder.messageId;
    win.reminder = null;
    if (refocus) {
      const back = win.log.querySelector(`[data-focus-key="remind:${messageId}"]`);
      if (back) back.focus();
      else if (!win.form.hidden) win.input.focus({ preventScroll: true });
    }
  };

  ChatApp.prototype.reminderNote = function (win, text, isError, link) {
    if (!win.reminder) return;
    const note = win.reminder.note;
    note.hidden = false;
    note.classList.toggle('is-error', !!isError);
    note.replaceChildren(document.createTextNode(text));
    if (link) {
      const open = el('a', null, 'Open Notebook');
      open.href = link;
      note.append(' ', open);
    }
    this.announce(text);
  };

  /**
   * Saves the reminder in the Notebook (its own API, checks and storage): the message's words as
   * the title and body, and a link that opens the message in chat. Only the person sees it.
   */
  ChatApp.prototype.saveReminder = async function (win, message, when) {
    const who = message.senderUserId === this.me ? 'You' : this.nameOf(message.senderUserId, win);
    const where = win.groupId ? ` in ${(this.groups.get(win.groupId) || {}).title || 'a group'}` : '';
    const words = message.body ? snippet(message.body) : ((message.attachments || []).map((a) => a.fileName).filter(Boolean).join(', ') || 'a file');
    const title = `Chat: ${who}${where}: ${words}`.slice(0, 220);
    const files = (message.attachments || []).filter((a) => !a.isRemoved).map((a) => a.fileName);
    const back = this.messagesUrl
      ? `${global.location.origin}${this.messagesUrl}?with=${encodeURIComponent(win.peerId)}&at=${message.id}`
      : '';
    const body = [
      message.body || '',
      files.length ? `Files: ${files.join(', ')}` : '',
      back ? `Open the message in chat: ${back}` : '',
    ].filter(Boolean).join('\n\n').slice(0, 20000);
    const headers = { Accept: 'application/json', 'Content-Type': 'application/json' };
    const token = document.querySelector('meta[name="csrf-token"]')?.getAttribute('content');
    if (token) headers['X-CSRF-TOKEN'] = token;
    let failed = null;
    try {
      const response = await global.fetch(this.notebookApi, {
        method: 'POST',
        headers,
        credentials: 'same-origin',
        body: JSON.stringify({
          title,
          body,
          type: 'Reminder',
          priority: 'Normal',
          reminderAtUtc: when.toISOString(),
          isPinned: false,
          checklistRows: [],
          labels: [],
          clientRequestId: global.PMChatParts.newClientId(),
        }),
      });
      if (!response.ok) {
        let problem = null;
        try {
          problem = await response.json();
        } catch (_) { /* not JSON */ }
        const detail = problem && problem.errors ? Object.values(problem.errors).flat()[0] : null;
        failed = detail || (problem && problem.message) || 'The reminder could not be saved.';
      }
    } catch (_) {
      failed = 'PRISM could not be reached. Check the connection and try again.';
    }
    const view = this.viewFor(win);
    if (failed) {
      this.reminderNote(view, failed, true);
      return;
    }
    this.reminderNote(view, `Reminder saved in your Notebook for ${reminderFormat.format(when)}.`, false, this.notebookUrl);
    if (view.reminder) {
      view.reminder.panel.classList.add('is-done');
      view.reminder.timer = global.setTimeout(() => this.closeReminder(view, false), 6000);
    }
  };
})(window);

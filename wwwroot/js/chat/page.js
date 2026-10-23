/*
 * PRISM chat, the Messages page (/Chat), and start-up.
 * One of the chat scripts loaded in order by _ChatScripts.cshtml (see chat/core.js for the list and
 * the server contract). Shared helpers come from PMChatParts, set up by chat/core.js.
 */
(function (global) {
  'use strict';

  const document = global.document;
  const {
    ChatApp, SEARCH_DELAY_MS, shortStamp, conversationList, groupKey, conversationKey,
    peopleCount, el, focusKeyIn, refocus, icon, avatar,
    groupAvatar, setPresence, statusText, appendHighlighted,
    unreadBadge,
  } = global.PMChatParts;

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
        side.append(unreadBadge(entry.unread));
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

  global.PMChatParts.MessagesPage = MessagesPage;
})(window);

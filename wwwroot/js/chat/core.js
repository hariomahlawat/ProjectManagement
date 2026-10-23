/*
 * PRISM chat: buddy list and docked one-to-one chat windows (bottom-right, Gmail-style), plus
 * the full Messages page (/Chat) when it is on screen.
 *
 * Seven plain scripts, loaded in this order by Pages/Shared/_ChatScripts.cshtml (no build step):
 *   core.js          constants, pure helpers, DOM helpers, the chime; hands them on as PMChatParts
 *   app.js           ChatApp: state, browser storage, HTTP, the live connection, polling, away
 *   events.js        what arrives: messages, reads, presence, unsends, status, groups
 *   dock.js          the contact list, New group, chat buttons next to names, the launcher badge
 *   windows.js       conversation windows and the group people panel
 *   conversation.js  one conversation: reading, history paging, drawing messages, sending
 *   page.js          the Messages page, and start-up (window.PMChat)
 * Each later file adds methods to ChatApp.prototype; nothing runs until page.js starts it.
 *
 * Server contract (Features/Chat/ChatApi.cs, Hubs/ChatHub.cs):
 *   GET  {api}/contacts                         buddy list with presence and unread counts
 *   GET  {api}/with/{peer}/messages?before=&after=&limit=
 *   GET  {api}/search?q=&groups=true            message search across the user's conversations
 *   POST {api}/messages                         { recipientUserId, clientMessageId, body }
 *   POST {api}/with/{peer}/read                 { upToMessageId }
 *   GET/POST {api}/status                       { message } (own status message)
 *   POST {api}/messages/{id}/unsend             own message, shortly after sending
 *   GET  {api}/unsent?since=                    ids unsent while this page was not listening
 *   GET  {api}/previews?link=&link=             cards for links to PRISM records the user may open
 *   GET/POST {api}/groups, GET {api}/groups/{id}, …/messages, …/read, …/members,
 *            …/members/{user}/remove, …/leave, …/rename      group chats (410 = not in the group)
 *   hub  in:  ReceiveMessage / ConversationRead / PeerRead / PresenceChanged / Typing /
 *             MessageUnsent / StatusChanged / ReceiveGroupMessage / GroupMessageUnsent /
 *             GroupChanged / GroupRemoved / GroupRead / GroupTyping
 *   hub  out: also TypingInGroup(groupId)
 *
 * Conversations are keyed by the other person's user id (direct) or "g:{groupId}" (group).
 *   hub  out: Typing(peerUserId), SetAway(bool)  (ephemeral signals only; nothing is stored)
 *
 * Every piece of user text is written with textContent; nothing from the server is parsed as HTML.
 */
(function (global) {
  'use strict';

  const document = global.document;
  const TIME_ZONE = 'Asia/Kolkata';
  const HISTORY_PAGE = 40;
  const POLL_MS = 30000;
  const CATCH_UP_PAGES = 25; // up to 1,000 missed messages are stitched in before older ones are dropped
  const WINDOW_WIDTH = 300;
  const BUDDY_WIDTH = 280;
  const DOCK_GAP = 12;
  const TYPING_SEND_MS = 2500; // how often "typing" is re-sent while keys keep coming
  const TYPING_SHOW_MS = 6000; // how long "typing…" stays up after the last signal
  const IDLE_MS = 5 * 60 * 1000; // no input for this long in this tab = away
  const IDLE_CHECK_MS = 30000;
  const SEARCH_DELAY_MS = 300;
  const UNSENT_RETRY_MS = 15000;
  const GROUP_LIMIT = 50; // people in a group, the creator included (ChatConversation.MaxGroupMembers)
  const ARM_MS = 5000; // how long a "click again to confirm" button stays armed
  const PICKER_MAX = 200; // people listed in a picker at once; the search narrows the rest
  const SERVER_TIMEOUT_MS = 90000; // how long a silent drop can go unnoticed
  // Catch-up looks back this far before the moment a drop was noticed: the whole detection
  // window, plus a minute for clock differences between browser and server.
  const UNSENT_LOOKBACK_MS = SERVER_TIMEOUT_MS + 60000;

  const timeFormat = new Intl.DateTimeFormat('en-IN', { hour: '2-digit', minute: '2-digit', hour12: false, timeZone: TIME_ZONE });
  const dayKeyFormat = new Intl.DateTimeFormat('en-CA', { year: 'numeric', month: '2-digit', day: '2-digit', timeZone: TIME_ZONE });
  const dayLabelFormat = new Intl.DateTimeFormat('en-IN', { day: 'numeric', month: 'short', year: 'numeric', timeZone: TIME_ZONE });

  // ---------------------------------------------------------------------------
  // Pure helpers (exposed for tests)
  // ---------------------------------------------------------------------------

  /** RFC 4122 v4 id. crypto.randomUUID needs a secure context, which a plain-HTTP LAN site is not. */
  function newClientId(cryptoImpl) {
    const bytes = new Uint8Array(16);
    (cryptoImpl || global.crypto).getRandomValues(bytes);
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
  }

  function dayKey(date) {
    return dayKeyFormat.format(date);
  }

  function dayLabel(date, now) {
    const key = dayKey(date);
    if (key === dayKey(now)) return 'Today';
    if (key === dayKey(new Date(now.getTime() - 86400000))) return 'Yesterday';
    return dayLabelFormat.format(date);
  }

  /** Short timestamp for lists: the time today, otherwise the date. */
  function shortStamp(date, now) {
    return dayKey(date) === dayKey(now) ? timeFormat.format(date) : dayLabel(date, now);
  }

  /** Unread first, then active accounts; online before away before offline; then by name. */
  function sortContacts(contacts) {
    const rank = (c) => (c.isOnline ? (c.isAway ? 1 : 2) : 0);
    return contacts.slice().sort((a, b) =>
      (Number(b.unreadCount > 0) - Number(a.unreadCount > 0))
      || (Number(b.isActive) - Number(a.isActive))
      || (rank(b) - rank(a))
      || a.displayName.localeCompare(b.displayName, undefined, { sensitivity: 'base' }));
  }

  function filterContacts(contacts, term) {
    const needle = (term || '').trim().toLowerCase();
    if (!needle) return contacts;
    return contacts.filter((c) =>
      c.displayName.toLowerCase().includes(needle) || (c.rank || '').toLowerCase().includes(needle));
  }

  /** Conversations for the Messages page: people with history, most recent first. */
  function conversationList(contacts) {
    return contacts
      .filter((c) => c.lastMessageUtc)
      .sort((a, b) => Date.parse(b.lastMessageUtc) - Date.parse(a.lastMessageUtc));
  }

  /** How many chat windows fit beside the buddy list. */
  function windowCapacity(viewportWidth) {
    if (viewportWidth < 576) return 1;
    return Math.max(1, Math.floor((viewportWidth - BUDDY_WIDTH - 2 * DOCK_GAP) / (WINDOW_WIDTH + DOCK_GAP)));
  }

  /** Merge messages by server id, keeping order by id; pending (id-less) messages stay last. */
  function mergeMessages(existing, incoming) {
    const byId = new Map();
    const pending = [];
    for (const m of existing) {
      if (m.id) byId.set(m.id, m); else pending.push(m);
    }
    for (const m of incoming) {
      // An unsend is final: a stale copy (an older history page, a late send response) must
      // never bring the text back.
      const known = byId.get(m.id);
      byId.set(m.id, known && known.isUnsent && !m.isUnsent ? known : m);
      const index = pending.findIndex((p) => p.clientMessageId === m.clientMessageId);
      if (index >= 0) pending.splice(index, 1);
    }
    return Array.from(byId.values()).sort((a, b) => a.id - b.id).concat(pending);
  }

  /**
   * Fetches pages newest-first until they reach a message newer than knownMaxId has already
   * covered, so a window that missed messages while offline gets all of them, not just the
   * latest page. `fetchPage(beforeId)` returns `{ messages, hasMore }` with messages oldest first.
   * `overlaps` is false when the page cap ran out first; the caller then drops its older copy.
   */
  async function fetchSince(fetchPage, knownMaxId, maxPages) {
    let collected = [];
    let before = null;
    for (let page = 0; page < maxPages; page += 1) {
      const history = await fetchPage(before);
      collected = history.messages.concat(collected);
      const oldest = history.messages[0];
      if (!history.hasMore || !oldest || oldest.id <= knownMaxId) {
        return { messages: collected, overlaps: true, hasMore: history.hasMore };
      }
      before = oldest.id;
    }
    return { messages: collected, overlaps: false, hasMore: true };
  }

  /** Splits text around case-insensitive matches of term: [{ text, match }]. For <mark> rendering. */
  function highlightParts(text, term) {
    const needle = (term || '').trim().toLowerCase();
    if (!needle) return [{ text, match: false }];
    const parts = [];
    const lower = text.toLowerCase();
    let from = 0;
    let at = lower.indexOf(needle, from);
    while (at >= 0) {
      if (at > from) parts.push({ text: text.slice(from, at), match: false });
      parts.push({ text: text.slice(at, at + needle.length), match: true });
      from = at + needle.length;
      at = lower.indexOf(needle, from);
    }
    if (from < text.length) parts.push({ text: text.slice(from), match: false });
    return parts;
  }

  /**
   * Alternates document.title with a notice ("Ravi says…") until stopped, the way old webmail
   * chat drew the eye to a background tab. Restores the page's own title when it stops.
   */
  function TitleFlasher(doc, setTimer, clearTimer) {
    this.doc = doc;
    this.setTimer = setTimer;
    this.clearTimer = clearTimer;
    this.timer = null;
    this.original = null;
  }

  TitleFlasher.prototype.start = function (notice) {
    if (this.timer) this.clearTimer(this.timer);
    if (this.original === null) this.original = this.doc.title;
    let showNotice = true;
    this.doc.title = notice;
    this.timer = this.setTimer(() => {
      showNotice = !showNotice;
      this.doc.title = showNotice ? notice : this.original;
    }, 1200);
  };

  TitleFlasher.prototype.stop = function () {
    if (this.timer) this.clearTimer(this.timer);
    this.timer = null;
    if (this.original !== null) this.doc.title = this.original;
    this.original = null;
  };

  TitleFlasher.prototype.active = function () {
    return this.timer !== null;
  };

  const GROUP_PREFIX = 'g:';

  function groupKey(id) {
    return `${GROUP_PREFIX}${id}`;
  }

  /**
   * The group id in a conversation key, or 0 for a direct conversation or a malformed group key
   * (anything but a positive whole number, e.g. from a hand-edited ?with= link).
   */
  function groupIdOf(key) {
    if (typeof key !== 'string' || !key.startsWith(GROUP_PREFIX)) return 0;
    const text = key.slice(GROUP_PREFIX.length);
    const id = /^\d{1,16}$/.test(text) ? Number(text) : 0;
    return Number.isSafeInteger(id) && id > 0 ? id : 0;
  }

  /** One spelling per conversation ("g:05" and "g:5" are the same group); '' when malformed. */
  function conversationKey(key) {
    if (typeof key !== 'string' || !key) return '';
    if (!key.startsWith(GROUP_PREFIX)) return key;
    const id = groupIdOf(key);
    return id ? groupKey(id) : '';
  }

  /**
   * The other members who could see a group message: those who had joined by the time it was
   * sent. Someone added later never sees it, however far their read marker has moved.
   */
  function couldSee(members, me, message) {
    const sent = Date.parse(message.sentUtc) || 0;
    return (members || []).filter((m) => m.userId !== me && !(m.joinedUtc && Date.parse(m.joinedUtc) > sent));
  }

  /** The other members who have read a group message ("Seen by"). */
  function seenBy(members, me, message) {
    return couldSee(members, me, message).filter((m) => (m.lastReadMessageId || 0) >= message.id);
  }

  /**
   * Whether a group message counts as seen (two ticks): every other member who could see it has
   * read it. Members with inactive accounts never will, so they do not hold the ticks back.
   */
  function seenByEveryone(members, me, message) {
    const expected = couldSee(members, me, message).filter((m) => m.isActive !== false);
    return expected.length > 0 && expected.every((m) => (m.lastReadMessageId || 0) >= message.id);
  }

  function peopleCount(n) {
    return `${n} ${n === 1 ? 'person' : 'people'}`;
  }

  /** "Ravi is typing…", "Ravi and Sita are typing…", "3 people are typing…". */
  function typingLine(names) {
    if (!names.length) return '';
    if (names.length === 1) return `${names[0]} is typing…`;
    if (names.length === 2) return `${names[0]} and ${names[1]} are typing…`;
    return `${names.length} people are typing…`;
  }

  global.PMChatInternals = {
    groupKey, groupIdOf, conversationKey, seenBy, seenByEveryone, typingLine, peopleCount,
    newClientId, dayLabel, shortStamp, sortContacts, filterContacts, conversationList,
    windowCapacity, mergeMessages, fetchSince, highlightParts, TitleFlasher,
  };

  // ---------------------------------------------------------------------------
  // DOM helpers
  // ---------------------------------------------------------------------------

  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined && text !== null) node.textContent = text;
    return node;
  }

  /**
   * Lists and logs are redrawn whole on every update (presence, unread counts, read ticks), which
   * would drop keyboard focus. Controls that can hold focus carry data-focus-key; this is the key
   * of the one that has focus inside container, or '' when focus is elsewhere.
   */
  function focusKeyIn(container) {
    const active = document.activeElement;
    return active && active !== container && container.contains(active) && active.dataset
      ? active.dataset.focusKey || ''
      : '';
  }

  /** After a redraw, focus the control with that key again; false when it is gone. */
  function refocus(container, key) {
    if (!key) return false;
    const again = Array.from(container.querySelectorAll('[data-focus-key]')).find((n) => n.dataset.focusKey === key);
    if (!again) return false;
    again.focus({ preventScroll: true });
    return true;
  }

  /** Writes text only when it changed, so a live region does not announce the same words again. */
  function setText(node, text) {
    if (node.textContent !== text) node.textContent = text;
  }

  /** An unread count on a badge: "99+" past 99. */
  function countLabel(n) {
    return n > 99 ? '99+' : String(n);
  }

  function unreadBadge(n) {
    return el('span', 'badge rounded-pill pm-chat__unread', countLabel(n));
  }

  /** The highest saved message id (optionally among those keep() accepts); 0 when none. */
  function newestId(messages, keep) {
    return messages.reduce((max, m) => (m.id > max && (!keep || keep(m)) ? m.id : max), 0);
  }

  function icon(name) {
    const i = el('i', `bi bi-${name}`);
    i.setAttribute('aria-hidden', 'true');
    return i;
  }

  function iconButton(name, label, className) {
    const button = el('button', `pm-chat__icon-btn ${className || ''}`.trim());
    button.type = 'button';
    button.setAttribute('aria-label', label);
    button.title = label;
    button.append(icon(name));
    return button;
  }

  function avatar(contact) {
    const wrap = el('span', 'pm-chat__avatar');
    wrap.append(el('span', 'pm-chat__initials', contact ? contact.initials : '?'));
    const dot = el('span', 'pm-chat__presence');
    wrap.append(dot);
    return wrap;
  }

  function groupAvatar(title) {
    const wrap = el('span', 'pm-chat__avatar is-group');
    wrap.append(icon('people-fill'));
    wrap.title = title || '';
    return wrap;
  }

  function setPresence(avatarNode, contact) {
    if (avatarNode.classList.contains('is-group')) return;
    const dot = avatarNode.querySelector('.pm-chat__presence');
    const online = !!(contact && contact.isOnline);
    dot.classList.toggle('is-online', online && !contact.isAway);
    dot.classList.toggle('is-away', online && !!contact.isAway);
    dot.classList.toggle('is-inactive', !!(contact && !contact.isActive));
  }

  function statusText(contact) {
    if (!contact) return '';
    if (!contact.isActive) return 'Inactive account';
    if (!contact.isOnline) return 'Offline';
    return contact.isAway ? 'Away' : 'Online';
  }

  function appendHighlighted(node, text, term) {
    for (const part of highlightParts(text, term)) {
      node.append(part.match ? el('mark', null, part.text) : document.createTextNode(part.text));
    }
  }

  // ----- links in messages ------------------------------------------------------------

  const LINK_PATTERN = /\bhttps?:\/\/[^\s<>"']+/gi;
  const PREVIEWS_PER_MESSAGE = 3;
  const PREVIEW_KINDS = {
    project: 'Project',
    document: 'Project document',
    task: 'Action task',
    idea: 'Project idea',
    'repository-document': 'Repository document',
  };

  /**
   * Splits message text into plain text and web links ({ text } / { text, url }). Only http and
   * https are links; punctuation that ends a sentence ("see https://…/12.") stays text, as does
   * a closing bracket the link did not open.
   */
  function linkParts(value) {
    const text = value == null ? '' : String(value);
    const parts = [];
    let last = 0;
    for (const match of text.matchAll(LINK_PATTERN)) {
      let raw = match[0];
      while (/[.,;:!?'"]$/.test(raw) || (raw.endsWith(')') && !raw.includes('('))) raw = raw.slice(0, -1);
      let url = null;
      try {
        url = new URL(raw);
      } catch (_) { /* not a link after all */ }
      if (!url || (url.protocol !== 'http:' && url.protocol !== 'https:')) continue;
      if (match.index > last) parts.push({ text: text.slice(last, match.index) });
      parts.push({ text: raw, url });
      last = match.index + raw.length;
    }
    if (last < text.length) parts.push({ text: text.slice(last) });
    return parts;
  }

  /** Writes message text with its links as anchors; links off this PRISM server open a new tab. */
  function appendLinked(node, text, origin) {
    for (const part of linkParts(text)) {
      if (!part.url) {
        node.append(document.createTextNode(part.text));
        continue;
      }
      const a = el('a', 'pm-chat__link', part.text);
      a.href = part.url.href;
      if (part.url.origin !== origin) {
        a.target = '_blank';
        a.rel = 'noopener noreferrer';
      }
      node.append(a);
    }
  }

  /** The links in a message that point at this PRISM server, as path + query, at most three. */
  function previewLinks(text, origin) {
    const links = [];
    for (const part of linkParts(text)) {
      if (!part.url || part.url.origin !== origin) continue;
      const link = part.url.pathname + part.url.search;
      if (!links.includes(link)) links.push(link);
      if (links.length === PREVIEWS_PER_MESSAGE) break;
    }
    return links;
  }

  /** A preview card (from GET {api}/previews): one link with the record's kind, name and detail. */
  function previewCard(preview) {
    const card = el('a', 'pm-chat__card');
    card.href = preview.link;
    const kind = PREVIEW_KINDS[preview.kind] || 'PRISM';
    card.setAttribute('aria-label', `${kind}: ${preview.title}${preview.subtitle ? `, ${preview.subtitle}` : ''}`);
    // Only a same-site path the server built (a project's cover photo); never another site.
    if (typeof preview.imageUrl === 'string' && preview.imageUrl.startsWith('/') && !preview.imageUrl.startsWith('//')) {
      const image = el('img', 'pm-chat__card-image');
      image.src = preview.imageUrl;
      image.alt = '';
      image.loading = 'lazy';
      card.append(image);
    } else {
      card.append(icon(preview.kind === 'project' ? 'kanban' : preview.kind === 'task' ? 'check2-square' : preview.kind === 'idea' ? 'lightbulb' : 'file-earmark-text'));
    }
    const text = el('span', 'pm-chat__card-text');
    text.append(el('span', 'pm-chat__card-kind', kind), el('span', 'pm-chat__card-title', preview.title));
    if (preview.subtitle) text.append(el('span', 'pm-chat__card-detail', preview.subtitle));
    card.append(text);
    return card;
  }

  // ---------------------------------------------------------------------------
  // Sound: a short two-note chime made with Web Audio, so no audio file has to ship.
  // ---------------------------------------------------------------------------

  function Chime() {
    this.context = null;
  }

  /** Browsers only allow audio after the person has interacted with the page. */
  Chime.prototype.unlock = function () {
    const AudioContext = global.AudioContext || global.webkitAudioContext;
    if (!AudioContext) return;
    try {
      if (!this.context) this.context = new AudioContext();
      if (this.context.state === 'suspended') this.context.resume().catch(() => {});
    } catch (_) {
      this.context = null;
    }
  };

  Chime.prototype.play = function () {
    const ctx = this.context;
    if (!ctx || ctx.state !== 'running') return;
    try {
      const start = ctx.currentTime;
      [[880, 0], [1320, 0.12]].forEach(([frequency, offset]) => {
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.type = 'sine';
        osc.frequency.value = frequency;
        gain.gain.setValueAtTime(0.0001, start + offset);
        gain.gain.exponentialRampToValueAtTime(0.12, start + offset + 0.02);
        gain.gain.exponentialRampToValueAtTime(0.0001, start + offset + 0.25);
        osc.connect(gain).connect(ctx.destination);
        osc.start(start + offset);
        osc.stop(start + offset + 0.3);
      });
    } catch (_) {
      // No sound is better than a broken chat.
    }
  };

  // Handed to the other chat scripts (chat/app.js … chat/page.js), loaded after this one.
  global.PMChatParts = {
    countLabel, unreadBadge, newestId, linkParts, appendLinked, previewLinks, previewCard,
    TIME_ZONE, HISTORY_PAGE, POLL_MS, CATCH_UP_PAGES, WINDOW_WIDTH, BUDDY_WIDTH,
    DOCK_GAP, TYPING_SEND_MS, TYPING_SHOW_MS, IDLE_MS, IDLE_CHECK_MS, SEARCH_DELAY_MS,
    UNSENT_RETRY_MS, GROUP_LIMIT, ARM_MS, PICKER_MAX, SERVER_TIMEOUT_MS, UNSENT_LOOKBACK_MS,
    timeFormat, dayKeyFormat, dayLabelFormat, newClientId, dayKey, dayLabel,
    shortStamp, sortContacts, filterContacts, conversationList, windowCapacity, mergeMessages,
    fetchSince, highlightParts, TitleFlasher, GROUP_PREFIX, groupKey, groupIdOf,
    conversationKey, couldSee, seenBy, seenByEveryone, peopleCount, typingLine,
    el, focusKeyIn, refocus, setText, icon, iconButton,
    avatar, groupAvatar, setPresence, statusText, appendHighlighted, Chime,
  };
})(window);

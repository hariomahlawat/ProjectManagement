# Chat

Person-to-person chat inside PRISM, in the style of the old Google Talk / Yahoo Messenger: a
buddy list and small chat windows docked at the bottom-right of every page.

Status: phase 1 (one-to-one text chat, #2730) and phase 2 (typing, read ticks, alerts, away,
Messages page with search) are built. Owner decisions (2026-10-22): everyone may chat with
everyone; messages are kept indefinitely; administrators cannot read chats.

## What users see

- A **chat button** in the top bar, beside the notification bell, with a red badge for unread
  messages. It opens the **buddy list**: every active user, with a presence dot (green =
  online, amber = away, grey = offline), rank, a search box and per-person unread counts.
  Unread conversations sort first, then online, away and offline people, then by name. Its
  header has a **sound on/off** toggle and a link to the Messages page.
- **Docked windows**, one per conversation. They can be minimised, closed and reopened, and
  they stay open across page navigation (the set of open windows is remembered per browser
  and per user in `localStorage` key `prism.chat.dock.{userId}`; it holds user ids only, never
  message text). As many windows open as fit beside the buddy list; on a phone, one window
  fills the width.
- A message to someone with no open window **pops that window open**, with a "New message"
  highlight. A conversation counts as read only when the person is in it (keyboard focus in an
  expanded window on a visible tab), so a window that merely popped open stays unread.
- **Enter** sends, **Shift+Enter** adds a line, **Esc** minimises. Messages are plain text, up
  to 4,000 characters. A failed send stays in the window with a Retry link.
- A conversation with a **deactivated** account stays readable but cannot be replied to.
- **Typing indicator**: the other person's window header shows "typing…" while they type, for
  about six seconds after the last keystroke, and clears when their message arrives.
- **Read ticks** under your own messages: one tick once stored, two blue ticks once the other
  person has read it. Read receipts cannot be turned off.
- **Alerts** for a message you are not looking at: a short chime (made in the browser, no
  audio file) and, when the PRISM tab is in the background, a flashing tab title ("Ravi
  says…") until you return to the tab. With several PRISM tabs open only one chimes. Browsers
  only allow sound after you have clicked or typed on the page, so the first message after
  opening a fresh page may arrive silently. The sound setting is remembered per browser
  (`prism.chat.sound.{userId}`).
- **Online / away / offline**: online while any PRISM tab is open and in use; **away** after
  five minutes without mouse or keyboard activity in every open tab; offline with no tab open.
- **Messages page** (`/Chat`, also linked from the buddy list): every conversation, most recent
  first, with a large conversation view, and **search across all your messages** (at least two
  characters, case-insensitive, newest 50 matches). A search hit opens the conversation at
  that message, highlighted, with "Show earlier" and "Show newer" to read around it. Links
  take the form `/Chat?with={userId}&at={messageId}`. Opening a conversation there closes its
  docked window, so each conversation has one view.

## How it works

| Piece | Where |
| --- | --- |
| Tables | `ChatConversations`, `ChatParticipants`, `ChatMessages` (`Models/Chat`, migration `20261217110004_AddChat`) |
| Rules | `Services/Chat/ChatService.cs` |
| Online status | `Services/Chat/ChatPresenceTracker.cs` (in-memory, per process) |
| HTTP API | `Features/Chat/ChatApi.cs`, under `/api/chat` |
| Live updates | `Hubs/ChatHub.cs`, at `/hubs/chat` |
| Browser | `Pages/Shared/_ChatLauncher.cshtml`, the `#pmChat` root in `_Layout.cshtml`, `wwwroot/js/chat.js`, `wwwroot/css/chat.css` |
| Messages page | `Pages/Chat/Index.cshtml` (a shell; `chat.js` fills it) |

- **Everything that writes data is an antiforgery-protected POST** (send, mark read), like the
  notification API. After the database save the API pushes `ReceiveMessage` to both people's
  connections, `ConversationRead` to the reader's other tabs and `PeerRead` to the other
  person (read ticks). The hub broadcasts `PresenceChanged` (with `isAway`).
- **The browser may invoke only two hub methods, and neither stores anything**: `Typing(peerUserId)`,
  forwarded as `Typing` to that person only and throttled to once per two seconds per tab and
  person on the server, and `SetAway(bool)` for this tab's idle state. The auth cookie is
  `SameSite=Strict`, so another site cannot open the connection as the user.
- **Search** (`GET /api/chat/search?q=`) joins through the caller's own participant rows, so it
  can only ever match the caller's conversations. The term is matched as plain text (`%` and
  `_` are not wildcards). It is a substring scan without a full-text index, which is fine at
  LAN scale; add an index if chat volume grows large.
- **History paging** (`GET /api/chat/with/{peer}/messages`): no cursor = latest page;
  `before={id}` = page ending before that id (`hasMore`, plus `hasNewer` when it ends short of
  the latest); `after={id}` = page starting after that id. Every page carries
  `peerLastReadMessageId` for the ticks.
- **Direct conversations are unique per pair.** `ChatConversations.DirectKey` is the two user
  ids in ordinal order joined by `|`, with a unique index; a race to create the first
  conversation resolves to the winner.
- **Retries cannot duplicate.** The browser generates a `ClientMessageId` per message;
  `(SenderUserId, ClientMessageId)` is unique, and a retry returns the stored message.
  Browsers de-duplicate pushed messages by id.
- **Unread** is the count of the other person's messages with an id greater than the reader's
  `ChatParticipants.LastReadMessageId`, which only moves forward.
- **No foreign keys to `AspNetUsers`**, so chat history never blocks removing an account and
  survives it. The participant table already allows more than two members, so group chat
  needs no schema rework.
- If the live connection drops, the browser reconnects with back-off and polls every 30
  seconds meanwhile. Nothing is loaded from outside the LAN: the SignalR client and icons are
  already bundled.
- Sending is throttled per user (`chat-send` rate-limit policy, 20 messages per 10 seconds by
  default; HTTP 429 beyond that).

## Privacy

- Messages are returned only to the two people in the conversation. The history lookup is by
  the caller's own pair key, so there is no way to ask for someone else's thread.
- **There is no administrator read path**: no admin page, report, export or search covers chat
  messages, and none should be added without an owner decision.
- Messages are **not end-to-end encrypted**. Anyone with direct access to the PostgreSQL
  database or its backups can read them, as with every other table. Treat chat accordingly and
  do not describe it to users as secret.
- Messages are not written to the audit log.

## Configuration

| Key | Default | Purpose |
| --- | --- | --- |
| `Chat:Enabled` | `true` | Off hides the chat button and dock, answers 404 on `/api/chat/*` and refuses hub connections. Stored messages are untouched, so switching back on loses nothing. Read live; no restart needed. |
| `Chat:SendPermitLimit` | `20` | Messages one user may send per window. |
| `Chat:SendWindowSeconds` | `10` | Length of that window. |

## Deployment notes

- The migration only adds the three tables and their indexes; rollback of the application is
  safe with the tables left in place. To turn the feature off without a rollback, set
  `Chat:Enabled` to `false`.
- Online status lives in the web process, per open tab. PRISM runs as one process on one
  server, so this is exact; a multi-server deployment would need a SignalR backplane and a
  shared presence store. During an app-pool recycle, presence rebuilds as browsers reconnect,
  and each tab repeats its away state on reconnect.
- Phase 2 needs no migration. During a rolling restart an old page talking to the new server,
  or the reverse, keeps working: new fields are optional, and a hub method one side does not know fails harmlessly.

## Not yet built

A Yahoo-style status message, a "Chat" button next to people's names elsewhere in PRISM, unsend
within a few minutes, group chats, file attachments and project-linked conversations.

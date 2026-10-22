# Chat

Person-to-person chat inside PRISM, in the style of the old Google Talk / Yahoo Messenger: a
buddy list and small chat windows docked at the bottom-right of every page.

Status: phase 1 (one-to-one text chat) is built. Owner decisions (2026-10-22): everyone may
chat with everyone; messages are kept indefinitely; administrators cannot read chats.

## What users see

- A **chat button** in the top bar, beside the notification bell, with a red badge for unread
  messages. It opens the **buddy list**: every active user, with a presence dot (green =
  online), rank, a search box and per-person unread counts. Unread conversations sort first,
  then online people, then everyone else by name.
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
- Online status is "has at least one PRISM tab open". There is no "away" yet.

## How it works

| Piece | Where |
| --- | --- |
| Tables | `ChatConversations`, `ChatParticipants`, `ChatMessages` (`Models/Chat`, migration `20261217110004_AddChat`) |
| Rules | `Services/Chat/ChatService.cs` |
| Online status | `Services/Chat/ChatPresenceTracker.cs` (in-memory, per process) |
| HTTP API | `Features/Chat/ChatApi.cs`, under `/api/chat` |
| Live updates | `Hubs/ChatHub.cs`, at `/hubs/chat` |
| Browser | `Pages/Shared/_ChatLauncher.cshtml`, the `#pmChat` root in `_Layout.cshtml`, `wwwroot/js/chat.js`, `wwwroot/css/chat.css` |

- **Sending and marking read are antiforgery-protected POSTs**, like the notification API. The
  hub is push-only: after the database save, the API pushes `ReceiveMessage` to both people's
  connections, `ConversationRead` to the reader's other tabs, and the hub itself broadcasts
  `PresenceChanged`. Browsers never send messages through the WebSocket.
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
- Online status lives in the web process. PRISM runs as one process on one server, so this is
  exact; a multi-server deployment would need a SignalR backplane and a shared presence store.
  During an app-pool recycle, presence rebuilds as browsers reconnect.

## Not yet built (phase 2 and later)

Typing indicator, read ticks, sound and tab-title flash for new messages, a full Messages page
with history search, an "away" status, a Yahoo-style status message, a "Chat" button next to
people's names elsewhere in PRISM, unsend within a few minutes, group chats, file attachments
and project-linked conversations.

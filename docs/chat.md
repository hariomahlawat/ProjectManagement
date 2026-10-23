# Chat

Person-to-person chat inside PRISM, in the style of the old Google Talk / Yahoo Messenger: a
buddy list and small chat windows docked at the bottom-right of every page.

Status: phase 1 (one-to-one text chat, #2730), phase 2 (typing, read ticks, alerts, away,
Messages page with search, #2731), status messages, unsend and chat buttons next to names
(#2732), and group chats are built. File attachments (1 GB per person) are next. Owner decisions (2026-10-22): everyone may chat with
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
  fills the width. When a new window needs room, the oldest window the person is not busy in
  closes first (busy = something typed, a message still sending or failed, or keyboard focus).
  A closed window's draft and failed messages come back when the conversation is reopened in
  the same page (kept in memory only, never in browser storage).
- A message to someone with no open window **pops that window open**, with a "New message"
  highlight, unless every window on screen is busy: then it waits in the unread count, with
  the chime and title flash, instead of pushing a window aside. A conversation counts as read
  only when the person is in it (keyboard focus in an expanded window on a visible tab), so a
  window that merely popped open stays unread.
- A window stays where the person has scrolled: new messages and the 30-second refresh scroll
  to the newest message only when the view is already at the bottom.
- **Enter** sends, **Shift+Enter** adds a line. **Esc** steps back one level: in a window it
  closes the people panel, then minimises the window (focus stays on its title bar); in the
  contact list it cancels a status edit, clears the search, closes New group, then closes the
  list. Messages are plain text, up to 4,000 characters. A failed send stays in the window with
  a Retry link.
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
- **Status message** (Yahoo-style): a box at the top of the buddy list, "Set a status
  message". Up to 140 characters on one line; Enter or leaving the box saves, Esc cancels,
  clearing it removes it. Everyone sees it under your name in their buddy list and in your
  chat window's header, live.
- **Unsend**: for 5 minutes after sending (`Chat:UnsendWindowMinutes`), your own messages show
  an "Unsend" link. Unsending erases the text on the server (the row stays, so the
  conversation shows "Message unsent" in its place for both people), takes it out of search
  and unread counts, and reaches the other person live. After the window the link disappears
  and the server refuses.
- **Chat buttons next to names**: a small chat icon after a person's name opens a chat with
  them. Present on the Project Overview header (PO and HoD) and the Ongoing projects list
  (PO). To add it elsewhere, mark the element `data-chat-user="{userId}"` (and optionally
  `data-chat-name`); `chat.js` adds the button, never for the signed-in user, and also for
  content added to the page later.
- **Group chats** (owner decisions, 2026-10-22): "New group" under the people search in the
  buddy list. A group has a name and up to 50 people (active accounts only).
  - **Any member can add people; only the manager can remove people or rename the group;
    anyone can leave.** The manager is the creator; if they leave, or their account is
    deactivated or removed, the longest-standing member with an active account becomes manager
    (with none, the group has no manager).
  - **A member sees the group from when they joined** (owner decision, 2026-10-23, WhatsApp
    style): messages from before they were added are not shown, searched or counted as unread.
    Someone who leaves or is removed **loses access to the group's history**; their open window
    keeps what it showed, with the reply box closed. If they are added back, the group starts
    again from that moment, so they never see what was said while they were out.
  - Groups are listed above people in the buddy list, with unread counts, and on the Messages
    page. The people button in a group window shows members, Add people, Rename (manager) and
    Leave group (two clicks). The panel covers the conversation; ticked people, a half-typed
    name and an armed Leave survive changes other members make meanwhile.
  - Notices appear in the conversation ("Ravi added Sita", "Sita left", "… renamed the group
    to …"); they are not counted as unread and do not chime.
  - Each message shows who sent it; typing shows names ("Ravi and Sita are typing…"); read
    ticks become **Seen by**: one tick until everyone who can read it has (members with inactive
    accounts and members who joined after it are not waited for), then two blue ticks, and the
    tick's label lists who has read it.
  - Unsend, search (hits say which group), the Messages page and links
    (`/Chat?with=g:{groupId}`) all work for groups. A malformed group link (`g:5.5`, `g:abc`)
    opens nothing; `g:05` opens group 5.
- **Messages page** (`/Chat`, also linked from the buddy list): every conversation, most recent
  first, with a large conversation view, and **search across all your messages** (at least two
  characters, case-insensitive, newest 50 matches). A search hit opens the conversation at
  that message, highlighted, with "Show earlier" and "Show newer" to read around it. Links
  take the form `/Chat?with={userId}&at={messageId}`. Opening a conversation there closes its
  docked window, so each conversation has one view.

## Accessibility

- **Focus survives updates.** The contact list, the Messages page list and conversation logs
  are redrawn whole on every presence, unread or read-tick change. Every control that can hold
  focus there carries `data-focus-key` (`contact:{id}`, `group:{id}`, `conv:{key}`,
  `retry:{clientMessageId}`, `unsend:{id}`, and the people panel's controls), and the redraw puts
  focus back on the same control. If it is gone (sent after Retry, Unsend expired), focus goes
  to the message box instead of falling out of the window.
- **Focus returns** where it came from: closing a window or the contact list goes back to the
  chat button, minimising to the window's title bar, closing the people panel to its button.
  Opening the people panel moves focus into it (`role=region`, `aria-controls`/`aria-expanded`
  on the button).
- **What a screen reader hears**: every message starts with a visually hidden "Name: " (or
  "You: "), including group messages after the first of a run. Conversation logs are
  `role=log` with `aria-live=off`, because a redraw would read them out again; instead one
  polite live region (`ChatApp.announce`) says each new message once (the text when the person
  is in that conversation, otherwise "New message from …"), group-change errors, and how many
  messages a search found. Window status lines only change their text when it really changes.
- Inactive people are shown with a muted name and faded avatar, not a faded row, so their text
  keeps its contrast; the row says "Inactive account".

## How it works

| Piece | Where |
| --- | --- |
| Tables | `ChatConversations`, `ChatParticipants`, `ChatMessages` (`Models/Chat`, migration `20261217110004_AddChat`); `ChatUserStatuses` and `ChatMessages.UnsentUtc` (migration `20261217163005_AddChatStatusAndUnsend`); group name/manager and notices (migration `20261217220006_AddChatGroups`) |
| Group rules | `Services/Chat/ChatGroupService.cs` |
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
  `SameSite=Strict`, so another site cannot open the connection as the user. One tab may signal
  typing to at most 16 conversations within two seconds (`ChatHub.MaxTypingTargets`), which
  bounds what a misbehaving client can make the server remember or look up.
- **Status** (`GET`/`POST /api/chat/status`) and **unsend** (`POST /api/chat/messages/{id}/unsend`)
  are antiforgery-protected like the other writes; changes are pushed as `StatusChanged` (to
  everyone) and `MessageUnsent` (to both people). A check constraint keeps an unsent message's
  text empty in the database. Unsending someone else's message, or an unknown id, answers 400.
- **"Chat is off" vs. "not found"**: while `Chat:Enabled` is false every `/api/chat` endpoint
  answers 404 with the header `X-Chat-Disabled: true`, and only that makes the browser stand
  chat down. Any other 404 is an ordinary error for that one request. (Pages from before this
  header stood down on any 404; endpoints still avoid 404 for expected refusals, for them.)
  A page that lost its live connection asks `GET /api/chat/unsent?since=` (its own
  conversations only) after reconnecting and while polling, so messages unsent meanwhile are
  blanked too, including older ones already on screen and search results.
- **Groups** (`/api/chat/groups…`): list, create, get, history (`before`/`after` like direct
  history, plus each member's read marker), send, read, add members, remove a member, leave,
  rename. Every call checks membership on the server; a non-member gets **410**, sending
  included (not 404, see above). Each change (membership, group row, notices) commits in one
  transaction. Pushes: `GroupChanged` (new state, to members), `GroupRemoved` (to whoever lost
  access), `GroupRead` (to members, for "Seen by" and the reader's other tabs),
  `ReceiveGroupMessage` for messages and notices and `GroupMessageUnsent` for unsends. Typing
  uses the hub method `TypingInGroup(groupId)`, which checks membership and forwards to the
  other members only.
- **Group visibility** comes from `ChatParticipants.JoinedUtc`: group history, unread counts
  and search only include group messages with `SentUtc >= JoinedUtc` of the caller's current
  membership row (a new row when someone is added back). Each member in a group's member list
  carries `joinedUtc` for "Seen by".
- **Old pages during a rolling deployment** never receive group traffic they would misread:
  group messages, notices and unsends use their own hub events (`ReceiveGroupMessage`,
  `GroupMessageUnsent`), and search returns group hits only when asked with `groups=true`.
- Group conversations have `Kind = 'Group'`, a `Title` and a `ManagerUserId`, and no pair
  key; check constraints enforce both shapes. Notices are `ChatMessages` rows with an
  `EventKind` and empty body. Leaving or removal deletes the `ChatParticipants` row.
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
  survives it.
- If the live connection drops, the browser reconnects with back-off and polls every 30
  seconds meanwhile. Nothing is loaded from outside the LAN: the SignalR client and icons are
  already bundled.
- **Rate limits**, per user, HTTP 429 beyond them:
  - `chat-send`: direct and group messages, 20 per 10 seconds by default.
  - `chat-change`: status messages, unsends and group changes (create, add, remove, rename,
    leave), 20 per minute by default. Most of these are pushed to many people.
  - `chat-search`: message searches, 60 per minute by default (the page searches 300 ms after
    typing stops).

## Privacy

- Direct messages are returned only to the two people in the conversation. The history lookup
  is by the caller's own pair key, so there is no way to ask for someone else's thread. Group
  messages are returned only to current members, from when each joined.
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
| `Chat:ChangePermitLimit` | `20` | Status messages, unsends and group changes one user may make per window. |
| `Chat:ChangeWindowSeconds` | `60` | Length of that window. |
| `Chat:SearchPermitLimit` | `60` | Message searches one user may run per window. |
| `Chat:SearchWindowSeconds` | `60` | Length of that window. |
| `Chat:UnsendWindowMinutes` | `5` | How long after sending a message its sender may unsend it. `0` turns unsend off. |

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
- The review fixes of 2026-10-23 need no migration (`JoinedUtc` already exists). Changes in
  behaviour on deployment: members added to a group after it started no longer see the
  messages from before they joined, and the new rate limits apply. A new page talking to an
  old server while chat is switched off sees plain 404s (no `X-Chat-Disabled` header) and
  shows errors instead of hiding chat until it is reloaded.

## Not yet built

File attachments (1 GB per person) are being built next. Project-linked conversations were
dropped (owner decision, 2026-10-22).

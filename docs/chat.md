# Chat

Person-to-person chat inside PRISM, in the style of the old Google Talk / Yahoo Messenger: a
buddy list and small chat windows docked at the bottom-right of every page.

Status: phase 1 (one-to-one text chat, #2730), phase 2 (typing, read ticks, alerts, away,
Messages page with search, #2731), status messages, unsend and chat buttons next to names
(#2732), group chats and file attachments (1 GB per person, with storage management) are built. Owner decisions (2026-10-22): everyone may chat with
everyone; messages are kept indefinitely; administrators cannot read chats.

## Introducing chat

- **What's new card** (`Pages/Shared/_WhatsNew.cshtml`, `wwwroot/js/core/whats-new.js`): signed-in
  users see "PRISM just learned to talk." once, near the top bar, with **Show me** (opens the
  contact list and briefly rings the chat button), **Quick guide** and **Not now**. It shows on up
  to three page loads until answered, never over a dialog or while typing, and never to someone
  who has already opened chat (`prism.chat.dock.{userId}` exists). Cards queue: the Dark theme
  card waits until the Chat card has been answered and a day has passed
  (`prism.whatsNew.lastAnswered`), and no card appears while the contact list is open.
- **First-open tips** at the top of the contact list (`buildTips` in `chat/dock.js`): say hello,
  the chat icon next to names, unsend (when on), status message, files (when on), conversations
  are saved. **Got it** hides them for good (`prism.chat.tips.{userId}`).
- **Quick guide** at `/Chat/Guide`: one printable page; the numbers (unsend minutes, files per
  message, file size, allowance) come from the live settings, and the files section appears only
  while attachments are on. 404 when chat is off.

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
  A closed window's draft, failed messages and messages still sending come back when the
  conversation is reopened in the same page (kept in memory only, never in browser storage): a
  send that fails after its window closed shows there with Retry, so its text is not lost.
- A message to someone with no open window **pops that window open**, with a "New message"
  highlight, unless every window on screen is busy: then it waits in the unread count, with
  the chime and title flash, instead of pushing a window aside. A conversation counts as read
  only when the person is in it (keyboard focus in an expanded window on a visible tab), so a
  window that merely popped open stays unread.
- A window stays where the person has scrolled: new messages and the 30-second refresh scroll
  to the newest message only when the view is already at the bottom. A message from someone
  else arriving while the person reads back shows **"↓ 1 new message"** (counting up) over the
  bottom of the conversation; it goes to the latest message, and clears on reaching it.
- **"New messages"** divider: a conversation opened with unread messages shows a red line
  where they start (worked out from the unread count) and opens there; messages arriving while
  the person is elsewhere start one too. It stays until the person replies, or closes the window.
- **Reading**: messages from one person within five minutes form a run, drawn closer together;
  in a run of someone else's messages the time shows once, under the last. Pointing at a time
  (or a bubble) shows the full date and time ("Wed, 18 Nov 2026, 10:30").
- **Copy** beside a message (on hover, or keyboard focus in it) copies its text, also on
  plain-http deployments where the browser's Clipboard API is not offered; touch screens use
  press-and-hold instead.
- **Reconnecting…**: when the live connection has been down for four seconds, the contact list
  and every window say so ("New messages may take up to 30 seconds to appear", the polling
  interval) and screen readers hear it; it clears, with "Chat is connected again", on
  reconnecting. Short blips show nothing.
- **Open in Messages** (expand icon) in a docked window's title bar opens the same conversation
  on the Messages page.
- **Alt+C** anywhere in PRISM opens the contact list with the cursor in its search: type a
  name, **Enter** opens the first match, **↓/↑** move through people and groups (↑ from the
  first returns to the search). Ctrl+K stays PRISM's search palette.
- Near the 4,000-character limit a counter beside the message box shows how many characters are
  left (red under 50); reaching the limit is said once to screen readers.
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
  and the server refuses. Unsending also clears the message's reactions and mentions.
- **Message toolbar** (Phase 3, owner defaults of 2026-11-18): pointing at a message (or
  keyboard focus in it) shows a toolbar over its corner: **✓ Noted**, **👍**, **👀** (looking
  into it), **Reply**, **Copy**, and on your own recent messages **Edit**. It is one tab stop
  (`role=toolbar`); ← → Home End move inside it. In a conversation you can no longer write to
  (inactive account, removed from the group) only Copy is offered.
- **Replies**: Reply shows "Replying to Ravi: …" above the message box (× or Esc cancels); the
  sent message carries a quote of the original (who, and its opening words). Clicking the quote
  goes to the original and highlights it, or opens the conversation there if it is not loaded
  ("An earlier message"). The server stores only which message it replies to, and checks it is
  in the same conversation and, in a group, one the sender could see; the quote is built in the
  browser from messages it has, so a reply never carries text from before a member joined.
- **Reactions**: a fixed set (✓ Noted, 👍, 👀), not an emoji picker. Each shows as a chip
  under the message with a count ("✓ Noted 3"); its tooltip and label name who reacted, and
  your own chips are highlighted. Clicking a chip (or the toolbar button) adds or takes back
  yours; the change shows at once and reaches everyone who can see the message live. Anyone
  who can read a message may react to it (in a group, members who had joined by then).
- **Edit**: your own messages for 15 minutes after sending (`Chat:EditWindowMinutes`). Edit
  (or ↑ in an empty message box) puts the text in the box ("Editing your message"; × or Esc
  restores what you were typing); Send saves it. The message says "Edited" (its tooltip gives
  when); the earlier text is not kept (owner default). The new text reaches everyone live and
  is what search finds. Files cannot be changed by editing (remove a file instead).
- **#records** (Phase 5): typing `#` and at least two characters in any message box lists
  matching PRISM records from PRISM's own search suggestions (`/Common/Search?handler=Suggestions`,
  the same as the top-bar search, so each person only ever sees records they may open). Enter or
  Tab inserts the record's link, which shows as a preview card as usual. Links off this server
  are never offered; if search is unavailable the list simply does not appear.
- **Remind me** (Phase 5): the alarm button in a message's toolbar saves a **reminder in your
  own Notebook** (personal; nobody else sees it): in 1 hour, this evening at 17:00 (earlier in
  the day), tomorrow 09:00, Monday 09:00, or a chosen date and time. It holds who said what,
  the message text and file names, and a link that opens the message in chat. It goes through
  the Notebook's own API (`POST /api/notebook/items`: its checks, its antiforgery) and appears
  wherever Notebook reminders appear (the Notebook and My Workspace). Chat stores nothing.
- **Do not disturb** (Phase 5): the bell in the contact list header: for 1 hour, until 17:00
  (earlier in the day) or until tomorrow 09:00, or Turn off. While on, arriving messages make
  no sound, flash no tab title, pop no window open and are not announced (mentions included);
  they still count as unread, and the contact list says "Do not disturb until 17:00". Kept in
  this browser for this user (`localStorage` key `prism.chat.dnd.{userId}`, like sound and
  mute) and followed by its other tabs; it ends by itself. If you have no status message of
  your own, it sets "Do not disturb until 17:00" as your status so others know, and clears it
  when it ends (also when it ended while no PRISM tab was open: the next visit clears it); a status you set yourself, or changed since, is left alone.
- **@mentions** (groups): typing `@` in a group's message box lists its members (never
  yourself); ↑ ↓ choose, Enter or Tab inserts "@Name", Esc closes the list. The message stores
  who was mentioned (members only; others are dropped on the server). Mentions are marked in the
  text, and a mention of you is highlighted and **gets through a muted group**: the group pops
  open, chimes and is announced (owner default). Editing a message can change its mentions,
  but only to members who could see it.
- **Chat buttons next to names**: a small chat icon after a person's name opens a chat with
  them. Present on the Project Overview header (PO and HoD) and the Ongoing projects list
  (PO). To add it elsewhere, mark the element `data-chat-user="{userId}"` (and optionally
  `data-chat-name`); the chat scripts add the button, never for the signed-in user, and also for
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
    page. The people button in a group window opens the people panel, over the conversation:
    - each member with their presence dot and status ("Inactive account" for deactivated ones),
      who manages the group, and a chat button that opens a direct chat with them;
    - **Add people** (a full group says so instead of offering a list; the list shows at most
      200 people at once, and says to search for the rest), **Rename** (manager), **Remove**
      (manager) and **Leave group**. Remove and Leave each ask for a second click, and the
      confirmation disarms itself after five seconds. A change that worked says so in the
      panel ("Added Sita.", "Renamed to …", "Removed Ravi.") and to screen readers;
    - **Mute this group on this computer**: its messages still count as unread, but do not
      chime, flash the tab title, pop the window open or get announced unless you are in it.
      Remembered per browser and per user in `localStorage` key `prism.chat.muted.{userId}`
      (group ids only), like the sound setting, and applied at once in the user's other open
      tabs. Muted groups show a bell-slash in the list and "Muted" in their window.
    Ticked people, a half-typed name and an armed button survive changes other members make
    while the panel is open. New group checks for a name and at least one person before
    sending, and puts focus on what is missing.
  - Notices appear in the conversation ("Ravi added Sita", "Sita left", "… renamed the group
    to …"); they are not counted as unread and do not chime.
  - Each message shows who sent it; typing shows names ("Ravi and Sita are typing…"); read
    ticks become **Seen by**: one tick until everyone who can read it has (members with inactive
    accounts and members who joined after it are not waited for), then two blue ticks, and the
    tick's label lists who has read it.
  - Unsend, search (hits say which group), the Messages page and links
    (`/Chat?with=g:{groupId}`) all work for groups. A malformed group link (`g:5.5`, `g:abc`)
    opens nothing; `g:05` opens group 5.
- **Links and preview cards**: web links in a message are clickable (http and https only;
  links to other sites open in a new tab). A link to a PRISM record also shows a small card
  under the message, for up to three records per message:
  - **Project** (`/Projects/Overview/{id}`): cover photo, name and project officer;
  - **Project document** (`/Projects/Documents/Preview?documentId={id}`): title and project;
  - **Action task** (`/ActionTasks/Details/{id}`): title, status and due date;
  - **Project idea** (`/ProjectIdeas/Details/{id}`): title and status;
  - **Repository document** (`/DocumentRepository/Documents/Reader/{id}` or `View?id=`):
    subject, office and category.

  Each person sees a card only for a record they may open, under the same rule as the record's
  own page, applied by the server with that person's permissions: deleted projects, ideas,
  tasks and repository documents, unpublished or archived project documents, and other
  people's tasks (unless the viewer may see all tasks) get no card, and the answer is the same
  as for a record that does not exist. The link itself still works as any link does.
- **File attachments** (when `Chat:Attachments:Enabled` is on; design and owner decisions in
  [`chat-attachments.md`](chat-attachments.md)): a paperclip button beside the message box
  adds up to 5 files per message. Each uploads at once, with progress, Cancel, Remove and
  Retry; the message (text optional) is sent when they are all up. Allowed: PDF, `.docx`,
  `.xlsx`, `.pptx`, `.jpg`/`.png`/`.webp`, `.txt`/`.csv`, checked by content as well as name, at
  most 100 MB each. Each person may keep 1 GB of sent files; removing a file from one of your
  messages (Remove, then Remove? within five seconds) or unsending the message frees the space.
  Images show as thumbnails; other files show as cards that download them. There is no malware
  scanning (owner decision).
  - **Drag and drop**: files dropped anywhere on a chat window (or the conversation on the
    Messages page) are attached, with a "Drop files to attach" overlay while dragging. Files
    dropped on a person or group in the contact list open that conversation with them attached
    (not for inactive accounts). A dropped folder is left out, with a note to drop its files.
  - **Paste**: Ctrl+V of a screenshot (or of files copied in Windows Explorer, in browsers that
    offer them) attaches it; a screenshot is named for when it was taken
    ("Screenshot 18 Nov 2026 14.32.png"). A paste that carries text (from Word or a web page)
    pastes the text.
  - **Checked before uploading**: a file of a type chat does not take, an empty file or one over
    the size limit shows why at once, with Remove (no Retry); the server checks everything
    again. Images waiting to be sent show a small preview.
  - An upload that makes no progress for a minute (or gets no answer within three minutes of
    its last byte) stops and offers Retry instead of holding up the message.
  - **Image viewer**: a click on an image thumbnail opens it full size over the page, with the
    file name and sender, ← → (and buttons) through the conversation's other images, Download,
    Open in new tab, and Esc or the backdrop to close; focus returns to the thumbnail of the
    image last shown. Ctrl/Shift/middle click opens the image in a new tab as before.
- **My files** (`/Chat?view=files`, the "My files" button on the Messages page): the files you
  sent, with how much of your allowance they use; sort by date or size; tick files and
  "Remove selected", or "Remove" everything older than 30, 90, 180 days or a year (each asks for
  a second click). A removed file can be restored for a day. Automatic retention removes sent
  files older than a year; you are told, and can restore them for a day too. Files an
  administrator removed say so and are restored by an administrator.
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
  `retry:{clientMessageId}`, `unsend:{id}`, `copy:{id}`, `thumb:{attachmentId}`, and the people panel's controls), and the redraw puts
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
  is in that conversation, otherwise "New message from …"), including messages fetched by
  polling or catch-up while the live connection was down ("3 new messages from …"), a message
  that could not be sent, group-change errors, and how many messages a search found. Window status lines only change their text when it really changes.
- A docked window's title button reports `aria-expanded` (minimised or not). The chat button and
  the people search carry `aria-keyshortcuts="Alt+C"`. "↓ N new messages" is a button that
  says where it goes; the "Reconnecting…" notes are announced once through the live region
  rather than being live regions themselves.
- The message toolbar is one tab stop (`role=toolbar`, roving tabindex) so a long conversation
  does not cost dozens of Tab presses; reaction buttons and chips are toggle buttons
  (`aria-pressed`) labelled with who reacted. The @mention list is a `listbox` driven from the
  message box (`aria-autocomplete=list`, `aria-activedescendant`, `aria-expanded`).
- The image viewer is a modal dialog (`aria-modal`, labelled by its caption): focus starts on
  Close, Tab stays inside it, Esc closes it. The drop overlay is decorative (`aria-hidden`);
  dropping is a shortcut for the attach button, which stays the keyboard route.
- Inactive people are shown with a muted name and faded avatar, not a faded row, so their text
  keeps its contrast; the row says "Inactive account".

## How it works

| Piece | Where |
| --- | --- |
| Tables | `ChatConversations`, `ChatParticipants`, `ChatMessages` (`Models/Chat`, migration `20261217110004_AddChat`); `ChatUserStatuses` and `ChatMessages.UnsentUtc` (migration `20261217163005_AddChatStatusAndUnsend`); group name/manager and notices (migration `20261217220006_AddChatGroups`); replies, edits, reactions and mentions: `ChatMessages.ReplyToMessageId`/`EditedUtc`, `ChatMessageReactions`, `ChatMessageMentions` (migration `20270108080026_AddChatRepliesReactionsMentions`) |
| Group rules | `Services/Chat/ChatGroupService.cs` |
| Replies, edits, reactions, mentions | `ChatService.EditAsync` / `ReactAsync` and the send paths; `Services/Chat/ChatMessageDetails.cs` adds files, reply id, edit time, mentions and reactions to every message returned or pushed; browser: `wwwroot/js/chat/actions.js` |
| Rules | `Services/Chat/ChatService.cs` |
| Online status | `Services/Chat/ChatPresenceTracker.cs` (in-memory, per process) |
| HTTP API | `Features/Chat/ChatApi.cs`, under `/api/chat` |
| Live updates | `Hubs/ChatHub.cs`, at `/hubs/chat` |
| Browser | `Pages/Shared/_ChatLauncher.cshtml`, the `#pmChat` root in `_Layout.cshtml`, the scripts in `wwwroot/js/chat/` (listed, in load order, in `Pages/Shared/_ChatScripts.cshtml`; `core.js` describes each), `wwwroot/css/chat.css` |
| Messages page | `Pages/Chat/Index.cshtml` (a shell; `chat/page.js` fills it) |

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
  With attachments on it also asks `GET /api/chat/attachments/removed?since=` (paged with
  `before`, only messages the caller may read), so files removed meanwhile show as removed.
  Files it remembers as removed are checked with `GET /api/chat/attachments/current?ids=` (up
  to 500 at a time), so one restored meanwhile comes back.
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
- **Edit and react** (`POST /api/chat/messages/{id}/edit` `{ body, mentionedUserIds }`,
  `POST …/react` `{ kind, on }`): own messages within the edit window; reactions by anyone who
  can read the message. Refusals are 400 (409 when too late to edit), never 404. Pushed as
  `MessageEdited` / `GroupMessageEdited` (the message) and `ReactionsChanged`
  (`{ messageId, groupId, reactions }`) to everyone who can see the message.
- **Previews** (`GET /api/chat/previews?link=…&link=…`, at most 10 links, each a path with
  its query, including the path base): `Services/Chat/ChatLinkPreviewService.cs` recognises
  the record pages above and builds a card for each record the caller may open (for repository
  documents the same readable rule as the Reader page, `DocRepoAccess`: deleted ones never,
  inactive ones only for those allowed to view inactive documents); the rest are
  left out of the answer. The browser asks once per link per page, in batches, and keeps the
  answer (card or none) for the page's lifetime. Previews read records directly and never go
  through the record pages, so opening a card's record is what records a view (for example an
  AOTS document), not the preview.
- **Attachments** (`/api/chat/attachments…`, 404 while switched off): upload (`POST`,
  multipart `file`), download (`GET {id}`, `?thumbnail=true`), remove (`POST {id}/remove`),
  your usage (`GET usage`) and, for administrators, totals by size only (`GET totals`). Files
  live under the upload root in `chat/`, with random names; who may download follows who may
  read the message, checked on every request. Removals are pushed as `AttachmentRemoved`.
  `Services/Chat/ChatAttachmentService.cs`, `ChatAttachmentInspector.cs`,
  `Hosted/ChatAttachmentCleanupWorker.cs`, `wwwroot/js/chat/files.js`.
- **Storage management** (`Services/Chat/ChatStorageService.cs`): My files
  (`GET /api/chat/attachments/mine`, `POST …/mine/remove`, `…/mine/remove-older`,
  `…/mine/restore`); the admin page **Admin › Recovery › Chat storage** (preview, type REMOVE,
  undo; sizes and people only; audited as `Chat.AttachmentsRemoved` / `Chat.AttachmentsRestored`);
  retention once a day (`Chat.AttachmentsRemovedByRetention`); a `ChatFilesRemoved`
  notification to each person whose files an administrator or retention removed. Removals keep
  their file on disk for the undo window; restores are pushed as `AttachmentRestored`.
- **Search** (`GET /api/chat/search?q=`) joins through the caller's own participant rows, so it
  can only ever match the caller's conversations. The term is matched as plain text (`%` and
  `_` are not wildcards). It is a substring scan without a full-text index, which is fine at
  LAN scale; add an index if chat volume grows large.
- **History paging** (`GET /api/chat/with/{peer}/messages`): no cursor = latest page;
  `before={id}` = page ending before that id (`hasMore`, plus `hasNewer` when it ends short of
  the latest); `after={id}` = page starting after that id. Every page carries
  `peerLastReadMessageId` for the ticks.
- **Catching up** after a drop (or on the 30-second refresh) starts from the newest message
  the window received as history (`contiguousId` in `chat/conversation.js`), not the newest it
  shows: a message pushed or returned by a send can be newer than messages missed while the
  connection was down, and starting from it would skip them.
- **Unread counts** are changed live by pushes and reads, and replaced by each contacts or
  groups list from the server. A list requested before a live change to a conversation is
  older than that change, so the page keeps its own count for that conversation
  (`touchUnread` / `keepLiveUnread`). A read that the server did not store asks for the real
  count again.
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
  - `chat-preview`: link-preview requests, 120 per minute by default.
  - `chat-upload`: file uploads, 30 per minute by default.

## Privacy

- Direct messages are returned only to the two people in the conversation. The history lookup
  is by the caller's own pair key, so there is no way to ask for someone else's thread. Group
  messages are returned only to current members, from when each joined.
- **There is no administrator read path**: no admin page, report, export or search covers chat
  messages, and none should be added without an owner decision.
- Messages are **not end-to-end encrypted**. Anyone with direct access to the PostgreSQL
  database or its backups can read them, as with every other table. Treat chat accordingly and
  do not describe it to users as secret.
- Attached files are stored as they were sent, unencrypted, in the upload folder and its
  backups; anyone with access to the server's disk can read them, as with the database.
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
| `Chat:PreviewPermitLimit` | `120` | Link-preview requests one user may make per window (each asks for up to 10 links). |
| `Chat:PreviewWindowSeconds` | `60` | Length of that window. |
| `Chat:Attachments:Enabled` | `true` | File attachments. Off hides the attach button and answers 404 on `/api/chat/attachments*`; files already sent stay stored. |
| `Chat:Attachments:AllowanceMb` | `1024` | Total size of the files one person has sent that are still kept. |
| `Chat:Attachments:MaxFileMb` | `100` | Largest single file. |
| `Chat:Attachments:MinimumFreeDiskGb` | `20` | Uploads are refused while the upload disk has less free. |
| `Chat:Attachments:MaxFilesPerMessage` | `5` | Files one message may carry. |
| `Chat:Attachments:UnusedUploadMinutes` | `60` | An upload not sent within this time is removed by the clean-up job. |
| `Chat:Attachments:RetentionDays` | `365` | Sent files older than this are removed automatically once a day; `0` turns retention off. |
| `Chat:Attachments:UndoHours` | `24` | How long a removal can be undone before the file is deleted from disk (unsending is final). |
| `Chat:Attachments:UploadPermitLimit` / `UploadWindowSeconds` | `30` / `60` | Uploads one person may start per window. |
| `Chat:UnsendWindowMinutes` | `5` | How long after sending a message its sender may unsend it. `0` turns unsend off. |
| `Chat:EditWindowMinutes` | `15` | How long after sending a message its sender may edit it. `0` turns editing off (the Edit button is not offered). |

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
- `wwwroot/js/chat.js` was split into the seven scripts in `wwwroot/js/chat/` (2026-10-23), with
  no change in behaviour. A page already open keeps the script it loaded; a page rendered by
  the old build that asks for `/js/chat.js` after the new build is serving gets a 404 and shows
  no chat until it is reloaded. Add any new chat script to `Pages/Shared/_ChatScripts.cshtml`
  (the browser tests and `ChatApiTests` load and check that list).
- The review fixes of 2026-10-23 need no migration (`JoinedUtc` already exists). Changes in
  behaviour on deployment: members added to a group after it started no longer see the
  messages from before they joined, and the new rate limits apply. A new page talking to an
  old server while chat is switched off sees plain 404s (no `X-Chat-Disabled` header) and
  shows errors instead of hiding chat until it is reloaded.

- The chat UX update of 2026-11-18 (drag and drop, paste, image viewer, early file checks, and
  fixes for unread counts, catch-up and closed windows) changes only the browser scripts, the
  chat stylesheet and two server checks (link previews of inactive repository documents;
  thumbnails made at most two at a time). No migration, setting or route changes; a page
  already open keeps its old scripts until it is reloaded. Repository-document links that
  showed a card for an inactive document to someone not allowed to view inactive documents no
  longer do.

- The reading update of 2026-11-18 (Phase 2: "New messages" divider, "↓ new messages", runs,
  Copy, Reconnecting…, Open in Messages, Alt+C, the character counter) changes only the chat
  scripts and stylesheet: no server, migration or setting change. Alt+C is new to the page; it
  was not used elsewhere in PRISM. The page's name-button observer now ignores chat's own
  redraws, so it no longer rescans the page on every chat update.

- **Phase 3 (replies, reactions, edits, mentions)** adds migration
  `20270108080026_AddChatRepliesReactionsMentions`: two nullable columns on `ChatMessages`
  (`ReplyToMessageId`, `EditedUtc`) and two tables, `ChatMessageReactions` (message, user,
  kind; check constraint on the three kinds) and `ChatMessageMentions` (message, user), both
  removed with their message. Additive; no existing row changes. As with every migration, the
  previous build refuses to start against the migrated database, so rollback is the rollback
  SOP: restore, or this migration's Down (drops reactions, mentions and the reply/edit marks;
  message text is untouched). New setting `Chat:EditWindowMinutes` (default 15, in
  `appsettings.json`). New endpoints `POST /api/chat/messages/{id}/edit` and `…/react` (send
  rate limit) and hub events `MessageEdited`, `GroupMessageEdited` and `ReactionsChanged`: a page
  loaded before this release does not know them and shows edits and reactions on its next load;
  it sends replies and mentions as plain messages. The model snapshot is updated for the chat
  tables, checked by `ChatModelSnapshotTests`; `ChatPostgresIntegrationTests` runs the new
  queries on a migrated PostgreSQL database. Edits and reactions missed while the live
  connection was down are brought in on reconnect: the page asks
  `GET /api/chat/messages/current?ids=…` (at most 500 ids per request, the same who-may-read
  rule as history; others are left out) for the current state of every message it has loaded,
  and redraws those that changed. An older server answers 404 and the page carries on.

- **Phase 5 (#records, Remind me, do not disturb)** changes the chat scripts and stylesheet and
  adds three attributes to the chat root in `_Layout.cshtml` (`data-record-search-url`,
  `data-notebook-api`, `data-notebook-url`); no migration, setting or chat endpoint changes.
  Each feature is off when its attribute is missing, so a page from an older server simply
  does not offer it. The Notebook keeps a chat reminder as a note with a reminder time (its own
  normalisation of the "Reminder" type its create editor also sends); `ChatApiTests` posts
  chat's exact body to the real Notebook API.

## Not yet built

**Announcements** (one-to-many messages, for example from a HoD, that recipients acknowledge)
are **parked** (owner, 2026-11-18): not planned for now. If they are taken up again, these
decisions come first:
1. Who may send one: Comdt and HoD only, or anyone to a group they manage?
2. To whom: a group, everyone with a role, or chosen people?
3. Who sees the acknowledgements: only the sender, or also the sender's chain of command?
4. Can recipients reply in a thread, or only acknowledge?
5. Do unacknowledged announcements get a reminder (and after how long)?
They would reuse the reactions table pattern for acknowledgements and the notification outbox
for delivery, and need one migration.

File attachments are built but **switched off** (`Chat:Attachments:Enabled`); before switching
them on in production, check that the data disk has about 50 GB free beyond the 20 GB floor
(see [`chat-attachments.md`](chat-attachments.md)). Project-linked conversations were dropped
(owner decision, 2026-10-22).

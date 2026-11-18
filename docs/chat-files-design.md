# Chat Files area: design (proposed, not built)

Status: **proposal for owner review** (2026-11-18). Nothing here is built. It turns "My files"
into a Drive-style **Files** area over chat attachments, without creating a second document
store. Current behaviour is in [`chat.md`](chat.md) and [`chat-attachments.md`](chat-attachments.md).

## Goal

People should be able to find, preview and manage the files exchanged in chat the way they
would in Dropbox or Google Drive: browse, filter, search by name, see images and PDFs without
downloading, download several at once, and see how much of their allowance is used.

## Not a general Drive (deliberate)

| A general Drive would need | Why not here |
| --- | --- |
| Uploads that belong to no message, folders, sharing lists | A second permission model and a second document store beside the Document Repository and Project Documents (AGENTS.md: no parallel implementations; upload/download is a high-risk area). |
| Files kept indefinitely | Chat files are deliberately short-lived: 1 GB allowance, retention after a year (owner decisions, 2026-10-23). |

So the access rule stays exactly as built (D6): **a chat file is visible to exactly the people
who can read its message**, checked on every request. The Files area is a new *view* of those
files. Anything worth keeping is moved into a proper record with **Save to Document
Repository** / **Attach to project documents** (below).

## What users see

Route: `/Chat?view=files` (today's My files link keeps working and opens the new view).

- **Left rail**: *Shared with me*, *Sent by me*, then each person and group with files (as
  "folders"), most recent first.
- **Toolbar**: search by file name; type filter (Images, PDF, Word, Excel, PowerPoint,
  Text/CSV); date range (last 7/30/90 days, any); sort (newest, oldest, name, size); grid or
  list (remembered per browser, like the sound setting).
- **Grid**: image thumbnails (the existing WebP thumbnails) and file-type tiles; each shows
  name, size, who sent it and when. **List**: the same as columns.
- **Selecting** (tick boxes, Shift-click ranges, Ctrl+A): *Download as ZIP*, and for your own
  files *Remove* (existing removal, undoable for a day).
- **Opening a file**: images open in the chat image viewer (already built), stepping through
  the current filtered list; PDFs open in the browser's own viewer (see decision O3); other
  files show a details panel with Download.
- **Per file**: *Show in conversation* (opens `/Chat?with=…&at={messageId}`, already
  supported), *Forward* (choose a person or group; see "Forward"), *Download*, and the save
  actions below.
- **Storage meter**: "412 MB of 1 GB used", split by type, with **Free up space**: your
  largest and oldest files, ready to tick and remove.
- **In each conversation**: a *Files* tab (and a *Links* tab) in the window's details panel
  showing the same list filtered to that conversation.

## Server

All endpoints under the existing `/api/chat/attachments` group: chat and attachments
switches, sign-in, antiforgery on writes, per-user rate limits.

| Endpoint | Purpose | Notes |
| --- | --- | --- |
| `GET files?scope=shared\|sent\|conversation&key=&q=&type=&from=&to=&sort=&cursor=&limit=` | One page of files the caller may see | Built on the same join as `ListCurrentAsync` (caller's `ChatParticipants`, group `JoinedUtc <= SentUtc`), messages not unsent, files not removed. Keyset paging on `(CreatedUtc, Id)`, at most 100 per page. Returns `ChatFileListItemDto`: id, name, type, size, image size, sender, conversation key and name, message id, sent time. |
| `GET files/summary` | Counts per conversation and per type for the rail and meter | Same access join; counts only. |
| `POST files/zip` `{ ids: [...] }` | Download several files as one ZIP | At most 50 files / 500 MB per request (settings). Each id is checked with the same rule as a single download; files the caller may not see are left out (and listed in a `skipped.txt`), never an error that reveals they exist. Streamed with `ZipArchive` to the response, no temp file. Duplicate names get " (2)". Rate-limited (`chat-upload` policy or a new `chat-zip`). |
| `POST files/{id}/forward` `{ to: key }` | Send the file again to another conversation | Copies the attachment row (same stored file, new id, counted against the forwarder's allowance only if the owner decides so: O4) and sends a message with it through the normal send path. |

No schema change is needed for browsing. Indexes to check against production data before
building: `ChatAttachments (MessageId)` exists; add `(UploaderUserId, CreatedUtc)` only if
"Sent by me" paging is slow. Name search uses `ILIKE` on `FileName` within the caller's
files, which is a small set per person.

## Save to Document Repository / project documents

*Save to Document Repository* opens the repository's own upload form pre-filled with the
file (subject = file name, office/category chosen by the user). The repository's upload path
runs its own checks, permissions (`DocRepo.Upload`), duplicate detection (SHA-256) and audit.
*Attach to project documents* does the same through the project document upload, for projects
the user may upload to. Chat only hands over the bytes, server side, so the file is not
downloaded and re-uploaded by the browser. The chat file is unchanged; the record is a new,
independent copy.

## Owner decisions needed

| # | Question | Recommendation |
| --- | --- | --- |
| O1 | Keep 1 GB per person and one-year retention once files are easier to browse? | Keep; revisit with real usage numbers from Admin › Chat storage. |
| O2 | ZIP across many conversations allowed? | Yes: every file is checked individually. |
| O3 | Show PDFs in the browser (served `inline`) instead of only as downloads? | Yes, with `Content-Security-Policy: sandbox` and `X-Content-Type-Options: nosniff` on that response; the browser's built-in viewer works offline. |
| O4 | Does forwarding count against the forwarder's allowance? | No (same stored bytes); the file leaves the allowance of nobody until every copy is removed, which needs a reference count on the stored file. Simpler alternative: forwarding copies the bytes and counts once more. |
| O5 | Should *Save to Document Repository* be offered to everyone or only to repository uploaders? | Only to those with `DocRepo.Upload`, matching the repository. |

## Delivery plan

1. List and summary endpoints + the Files view (grid/list, filters, search, meter), reusing
   the image viewer. Read-only, no migration. Tests: access rule for direct, group (joined
   before/after), left group, unsent, removed; paging stability.
2. ZIP download. Tests: skipped files, limits, names, rate limit.
3. Per-conversation Files/Links tab.
4. Save to Document Repository / project documents (needs the target modules' owners).
5. Forward (after O4).

Each step is additive and can ship alone; old pages keep using My files until reloaded.

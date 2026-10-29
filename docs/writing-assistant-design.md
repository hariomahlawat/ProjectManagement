# Writing Assistant: design proposal

Status: **proposal for sign-off** (2026-10-29). Nothing here is built yet. Items marked
**[verify]** are to be confirmed during the Phase A pilot before they are relied on.

## 1. Goal

Give PRISM users Grammarly-style help (spelling, grammar and style suggestions in British
English) that works on the offline LAN, with no Internet access and no GPU, in two places:

- inside PRISM: a standalone **Writing Assistant** page and, later, PRISM's own text boxes;
- inside **Microsoft Word** (Office 2010 to 2024 LTSC, Windows), through a desktop add-in.

### Phases

| Phase | Delivers | Depends on |
| --- | --- | --- |
| **A** | LanguageTool service on the PRISM server; PRISM checking endpoint; Writing Assistant page | Java runtime approved for the server |
| **B** | Organisation dictionary and house-style rules, managed by Admin; applied everywhere | A |
| **C** | Word add-in (VSTO) with a "Check Writing" side panel | A (B recommended) |
| Later, optional | AI "Improve this sentence" rewrites | A separate machine; not on the current 4-core server |

### Non-goals

- Live as-you-type underlining inside Word (Word does not let add-ins draw their own
  squiggles; the add-in uses a check-and-review panel instead).
- AI rewriting on the current server (4 cores would make it slow for the user and would slow
  PRISM for everyone; see section 9).
- Storing, logging or learning from users' text.

## 2. Architecture

```
 Browser (PRISM page)            Word on a user PC (Phase C add-in)
          |  HTTPS, PRISM login cookie        |  HTTPS, PRISM add-in token
          v                                    v
 +--------------------------------------------------------------+
 |  PRISM (IIS, Windows Server 2022)                            |
 |  POST /api/writing/check                                     |
 |   - authentication, size and rate limits, timeout            |
 |   - house style + organisation dictionary (Phase B)          |
 |   - never stores or logs the text                            |
 +-------------------------------+------------------------------+
                                 |  HTTP to 127.0.0.1 only
                                 v
 +--------------------------------------------------------------+
 |  LanguageTool server (Java), Windows service, localhost only |
 |  en-GB rules; optional English n-gram data on disk           |
 +--------------------------------------------------------------+
```

Users and the add-in **never talk to LanguageTool directly**. PRISM is the only client, so
LanguageTool inherits PRISM's authentication, limits and audit posture, and no new port is
opened on the LAN.

## 3. LanguageTool service

| Item | Decision |
| --- | --- |
| Engine | LanguageTool (open source, LGPL-2.1), run unmodified as a separate process. |
| Java | A supported LTS runtime, e.g. Eclipse Temurin 21 (free). LanguageTool needs Java 17 or later **[verify for the chosen version]**. |
| Language | `en-GB` (British spelling: organise, colour, programme). |
| Binding | `127.0.0.1` only (LanguageTool's default unless started with `--public` **[verify]**). |
| Memory | Heap capped, e.g. `-Xmx2g`; the 36 GB server has ample room. |
| Limits | Server-side `maxTextLength` and `maxCheckTimeMillis` in its properties file, below PRISM's own limits **[verify property names for the chosen version]**. |
| Accuracy pack | Optional English n-gram data (~8 GB on disk, not RAM). It improves confused-word detection (their/there, affect/effect). Decide after the pilot. |
| Runs as | A Windows service under a dedicated low-privilege account, with read access only to its own folder. |
| Network | Windows Firewall rule blocking all **outbound** traffic for the Java process; no inbound rule (localhost only). |
| Updates | Java and LanguageTool updated together each quarter through the offline package process (section 8). |

### Is Java a security risk?

The runtime itself is not; the risks are an unpatched version, a service exposed on the
network, or excessive privileges. The table above addresses each: a supported LTS build
patched quarterly, localhost-only binding behind PRISM, a low-privilege service account, and
outbound traffic blocked.

## 4. PRISM checking endpoint (Phase A)

`POST /api/writing/check`

Request: `{ "text": "...", "language": "en-GB" }` (language fixed to `en-GB` initially).

Response, normalised so callers never depend on LanguageTool's own format:

```json
{
  "issues": [
    {
      "offset": 12, "length": 5,
      "category": "spelling | grammar | style | punctuation | typography",
      "message": "Possible spelling mistake found.",
      "replacements": ["colour", "color"],
      "ruleId": "MORFOLOGIK_RULE_EN_GB",
      "source": "languagetool | house-style"
    }
  ],
  "truncated": false
}
```

| Concern | Decision |
| --- | --- |
| Authentication | Signed-in PRISM users (cookie + antiforgery token from the PRISM page). Phase C adds an add-in token (section 7). |
| Size limit | e.g. 20,000 characters per request; the page and add-in check long documents in chunks. |
| Rate limit | Per-user fixed window via the existing `SharedRateLimiters` pattern, e.g. 60 checks per minute. |
| Timeout | e.g. 10 seconds to LanguageTool; on timeout the caller gets a clear "try a shorter passage" message. |
| Logging | Request metadata only (user, length, duration, result count). **Never the text.** |
| Failure | If LanguageTool is down, the endpoint returns 503 with "Writing checks are unavailable right now"; nothing else in PRISM is affected. |
| Feature flag | `WritingAssistant:Enabled`, **default `false`**. Enabled only after the service is installed on the server, so deploying the code changes nothing until then. |
| Config | `WritingAssistant:LanguageToolUrl` (default `http://127.0.0.1:8081`), `MaxTextLength`, `TimeoutSeconds`, rate-limit settings. |

## 5. Writing Assistant page (Phase A)

A standalone page at `/Tools/Writing`, opened in a new tab from the navigation drawer, like
PDF Tools:

- A text area to paste or type into; **Check** runs the endpoint.
- Issues underlined in colour by category; a side list with message, suggestions and
  **Apply** / **Ignore** for each; **Apply all spelling fixes**.
- Counts by category, a British-English badge, and **Copy text** to take the corrected text
  back to Word, an email or a PRISM form.
- Nothing is saved; leaving the page discards the text.

**Phase A.2 (optional, after the page proves useful):** a small "Check writing" button on
selected PRISM text boxes (remarks, Notebook, action-task descriptions) that opens the same
suggestions panel for that field.

## 6. Organisation dictionary and house style (Phase B)

Stored in PRISM (new tables, additive migration), managed on an Admin page:

- **Dictionary:** words never flagged as misspellings: unit names, ranks, acronyms, project
  and place names. Applied by PRISM, which drops LanguageTool spelling issues for these words.
- **House-style rules:** "prefer X over Y" phrases with an explanation, for example
  "w.e.f." → "with effect from", or a preferred date format. Applied by PRISM as extra issues
  with `source: "house-style"`.

Keeping both in PRISM leaves LanguageTool unmodified and lets one change apply to the page,
PRISM's text boxes and the Word add-in at once.

## 7. Word add-in (Phase C)

| Item | Decision |
| --- | --- |
| Technology | VSTO add-in (C#, .NET Framework 4.x). The modern web add-in type is ruled out: it needs Microsoft's Internet-hosted library and recent Office versions. |
| Office support | Word 2010, 2013, 2016, 2019, 2021, 2024 LTSC; 32-bit and 64-bit Office; Windows only. |
| User experience | **Check Writing** ribbon button opens a side panel listing issues for the document or the selection. Click to jump to the text; **Apply**, **Ignore**, **Add to my dictionary**. Word's built-in spell checker keeps working. |
| Build and test | Must be built on Windows with Visual Studio, and tested on each Office version in use. The code can be written here; compiling and testing happen on your Windows PC. |
| Installation | One installer pushed by IT (e.g. Group Policy), installing per machine. |

### Signing in from Word (new, security-sensitive)

PRISM has no API tokens today; it uses browser login cookies. The add-in therefore needs a
narrowly scoped **add-in token**:

- The user signs in once from the add-in with their PRISM username and password; PRISM issues
  a token that can call **only** `/api/writing/check`.
- Tokens are stored hashed in PRISM, expire (e.g. 90 days), and can be revoked by the user and
  by Admin; issuing and revoking are audited.
- The token is stored on the PC with Windows data protection (DPAPI) for that user.
- Login attempts reuse PRISM's existing login rate limiting and lockout rules.

This is an authentication change and will get its own focused tests and review.

### Code signing without a purchased certificate

Office warns about or blocks unsigned add-ins. Options, best first:

1. **Internal certificate authority.** If the organisation runs Active Directory Certificate
   Services, IT issues a code-signing certificate, already trusted by domain PCs.
2. **Self-signed certificate distributed by Group Policy.** Create one code-signing
   certificate, keep its private key on the build PC, and push its public part to every PC's
   *Trusted Root* and *Trusted Publishers* stores by Group Policy. No purchase needed.
3. Installing the add-in per machine under Program Files can reduce Office's trust prompts
   **[verify on each Office version]**, but should not replace signing.

Recommendation: option 1 if an internal CA exists, otherwise option 2.

## 8. Deployment, rollout and rollback

- **Package:** the offline release package gains a `writing-assistant/` folder: the Java
  runtime, the LanguageTool release, its properties file, a service install script, and
  optionally the n-gram data. Hashes recorded with the package. Documented in
  `docs/deployment/offline-ws2022.md`.
- **Install order:** (1) install Java and LanguageTool as a service; (2) confirm it answers on
  localhost; (3) deploy PRISM; (4) set `WritingAssistant:Enabled=true`.
- **Rollout:** a pilot group first (the Writing Assistant page is visible only to them),
  then everyone.
- **Rollback:** set `WritingAssistant:Enabled=false` (the page and endpoint disappear; nothing
  else changes). Stopping or uninstalling the service affects nothing else. Phase B tables are
  additive and can stay.
- **Patching:** Java and LanguageTool are updated quarterly by replacing their folders and
  restarting the service.

## 9. Capacity on the current server (4 cores, 36 GB RAM)

- **LanguageTool:** comfortable. A few GB of RAM; a check of a paragraph typically takes well
  under a second of CPU **[verify in pilot]**. Suitable for an office's worth of users.
- **AI rewriting:** not recommended here. A small CPU model would likely need tens of seconds
  to minutes per paragraph and would compete with PRISM and PostgreSQL for the 4 cores. If
  wanted later, run it on a separate machine behind the same endpoint.

## 10. Testing

- **Unit tests:** endpoint behaviour against a fake LanguageTool client (limits, timeouts,
  503 when down, normalisation, Phase B dictionary and house-style merging, no text in logs).
- **Contract tests:** feature flag off by default; endpoint requires authentication; page has
  no external assets.
- **Quality pilot (before rollout):** about 50 to 100 real (anonymised) sentences from PRISM-style
  letters and notes, checked for useful catches, false alarms on acronyms and ranks, and
  British spellings. The result sets the initial dictionary and decides on the n-gram pack.
- **Add-in:** manual test checklist per Office version (2010 to 2024, 32- and 64-bit).

## 11. Open questions

1. Does the organisation run an internal certificate authority (for add-in signing)?
2. Pilot group: which users or roles?
3. Which PRISM text boxes matter most for Phase A.2?
4. Install the ~8 GB n-gram pack from the start, or decide after the pilot?

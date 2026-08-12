PRISM Publications Phase 20.2 — Cover B Reliability
====================================================

Purpose
-------
Close the Cover B / Contemporary generation failure without weakening server-side publication integrity.

Root causes hardened
--------------------
1. Browser approval previously accepted any non-empty Cover B fingerprint, while the server required an exact match to the current preflight fingerprint.
2. Superseded preflight requests could briefly leave stale Cover B approval/quality UI visible.
3. Final PDF submission relied on button state instead of independently enforcing current Cover B approval.
4. Server 409 validation responses did not expose structured issue codes, so the client could not recover specifically from CoverReviewStale / CoverReviewRequired.
5. Single-photo ImageSharp processing failures could escape the publication photo boundary as an opaque request failure.

Implementation
--------------
- Introduces one canonical isCurrentCoverApproved() predicate.
- Requires exact equality with lastPreflight.coverReviewFingerprint.
- Adds preflightPending + revision sequencing; superseded requests are aborted immediately and stale responses are ignored.
- Cover approval is unavailable while cover preflight is pending.
- Approving an Automatic Cover B hero no longer silently converts it into an explicit saved hero.
- Final Cover B download has an independent current-approval guard before POST.
- CoverReviewRequired/CoverReviewStale 409 responses clear stale approval, re-run preflight and direct the user back to Cover B.
- Generation validation now returns structured blocker issue codes to AJAX callers.
- Cover B editorial approval and image quality are shown as separate states. The approval action disappears once approved; quality remains independently visible.
- Recoverable ImageSharp/source-processing exceptions make the individual photograph unavailable rather than crashing the whole publication request.
- Include back cover and additional introduction settings now participate in immediate Digital preflight.
- Physical PDF verification is surfaced for Digital as well as Print after successful generation.

Expected Cover B workflow
-------------------------
1. Select Digital / Comfortable and B · Contemporary / Premium.
2. PRISM shows “Checking cover” while current server preflight resolves the hero/fingerprint.
3. When ready, “Approve cover” becomes enabled.
4. Approval produces one “Approved for cover” state; image quality remains a separate chip (for example “Image quality · Low”).
5. Preview remains technically available without editorial approval, by design.
6. Final Download remains unavailable until project approvals and the exact current Cover B fingerprint are approved.
7. If cover state changes between approval and generation, PRISM does not issue a stale PDF; it resets cover approval, reruns preflight, and asks for re-approval.
8. After successful PDF composition, Final output can show “PDF verified · N pages” for Digital as well.

Important
---------
The server's strict Cover B fingerprint validation is intentionally retained. This phase fixes the client/server state contract rather than bypassing publication approval.

# PRISM Photos — Matching Recovery & Pagination Stabilisation

## Purpose
This phase resolves the observed condition where unresolved faces could remain in **Matching** for an extended period without becoming actionable, and removes redundant single-page `Page 1` pagination from Photos/Collections/Albums/People review surfaces.

## Candidate-matching reliability changes

### 1. Bounded candidate-search execution
Known-person candidate search now has a hard per-batch execution timeout. Default:

- `CandidateSearchTimeoutSeconds = 60`

If the bounded candidate-search operation exceeds this limit, the affected face records are moved to `Failed` with an explicit failure reason instead of remaining indefinitely in `Processing`.

### 2. Processing lease and stale recovery
Candidate `Processing` is now treated as a lease, not a permanent state. Default:

- `CandidateProcessingStaleSeconds = 180`

The background worker checks for stale Processing rows on every cycle and moves eligible stale rows back to `Pending` before continuing. This also recovers work left behind by application restarts or interrupted worker cycles.

### 3. Controlled automatic retry
Failed candidate searches remain reviewable and retry automatically after:

- `CandidateFailureRetryDelaySeconds = 300`

The reviewer therefore sees a real failure state rather than a false endless “Matching” state.

### 4. Worker-cycle safety
The entire candidate-refresh cycle is bounded to the configured search timeout plus a small orchestration allowance. This prevents the hosted service itself from becoming permanently blocked around queue/query orchestration.

### 5. Immediate worker wake-up
Queue mutations now signal the candidate worker immediately. Polling remains as a resilience fallback, but newly queued/rematched faces no longer need to wait for the normal idle interval.

### 6. Worker runtime telemetry
A process-local candidate-worker runtime state now records:

- configured / started state;
- worker ID;
- heartbeat;
- current batch start;
- last successful batch;
- last failure;
- processed count;
- stale leases recovered.

The existing **Media Intelligence / Readiness** workspace exposes queued, processing, failed and oldest-active metrics together with worker health.

### 7. Reviewer warning for delayed worker
The People review workspace now detects a stale matcher heartbeat while unresolved matching work exists. It surfaces a warning instead of silently continuing to claim matching is progressing normally. Live workload polling can clear/show this warning without a full page reload.

## Current stuck-face recovery after deployment
On application start, the matcher begins within a few seconds. Existing rows behave as follows:

- `Pending` → picked up normally;
- stale `Processing` older than the configured lease → recovered to `Pending` and processed;
- candidate search succeeds → `Ready`;
- candidate search times out/errors during the bounded search → `Failed` with a visible failure reason and later controlled retry.

Confirmed identities are never modified automatically.

## Pagination correction
Single-page pagination is now hidden in:

- Source Collections;
- Albums;
- People directory;
- People review queue.

Pagination appears only when Previous or Next actually exists.

## Configuration
No `appsettings` change is required. New options have safe defaults in `MediaPeopleOptions`:

```json
{
  "CandidateSearchTimeoutSeconds": 60,
  "CandidateProcessingStaleSeconds": 180,
  "CandidateFailureRetryDelaySeconds": 300
}
```

They may be overridden later if production measurements justify it.

## Database
No EF migration is required.

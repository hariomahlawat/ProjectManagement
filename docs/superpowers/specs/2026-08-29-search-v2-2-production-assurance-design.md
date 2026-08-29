# Search V2.2 — Relevance, Faceting & Production Assurance Design

## Goal
Converge the current Search V2 implementation without changing its product architecture: make projection freshness automatic, ranking semantics explicit, facets truthful under active filters, project context cross-source, aliases single-source-of-truth, operations recoverable, and quality measurable against real PostgreSQL and PRISM relevance data.

## Constraints
- Keep PostgreSQL as the only search backend.
- Keep deterministic exact identifier/title navigation above probabilistic relevance.
- Keep authorization before retrieval/ranking/counting/suggestions.
- Keep current Search UI architecture; refine rather than redesign.
- Do not add embeddings, LLM reranking, Notebook/private tasks, Media/People, Industry or Calendar in this phase.
- Do not overwrite the operator's current ServeV2/ShadowMode rollout state in the incremental package.

## Architecture decisions
1. Separate schema compatibility (`IndexVersion`) from projection semantics (`ProjectionVersion`). Search state and entries use the projection version as the active generation compatibility token; a projection version bump forces an atomic rebuild automatically.
2. SearchEntryTerms becomes semantically typed (`Identifier`, `Alias`, `Name`, `Organisation`, `Location`, `Person`, `Context`). Only `Alias` gets deterministic exact-alias privilege.
3. SearchAliases is the runtime source of truth for configured query aliases. Query expansion is performed token/phrase-wise and preserves AND semantics for unrelated query terms.
4. Facets use disjunctive semantics: each facet applies all active filters except its own dimension. Source facets count distinct canonical entities available from that source rather than the source of the current all-sources representative.
5. Parent-project stage/status/category metadata is propagated into project-linked projections where it is meaningful.
6. Search Health gains operational rebuild/retry APIs and failed-job diagnostics while live search continues against the previous generation.
7. Telemetry distinguishes engine/gateway/suggestion latency. Relevance and PostgreSQL test tooling remain opt-in and corpus-grounded.

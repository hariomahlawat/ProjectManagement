# PRISM Compendium Phase 45 — Flow / Proof Parity

## Purpose

This package fixes the large *apparent* blank area in **Balanced → Flow below image** while preserving the semantic sentence-boundary flow introduced in the Compendium planner.

The phase addresses three related causes rather than masking the symptom:

1. **Live Page proof typography parity** — the browser proof now scales narrative typography and Balanced geometry from the physical A4 point model used by the planner/PDF instead of using a fixed small `rem` body size.
2. **Measured Flow-below optimisation** — Balanced Flow-below evaluates a denser, bounded set of image heights and scores the semantic remainder beside the image around an editorial target, strongly penalising excessive voids while never splitting a sentence merely to fill space.
3. **Fit-image parity** — narrative flow and the browser proof now use the image's *actual rendered Fit height*, not merely its maximum frame height.

Justification itself is unchanged. Normal prose remains controlled by the Phase 44 semantic alignment policy; headings and bullets remain natural/left aligned.

## Prerequisite

Apply this package over the current PRISM tree that already contains **Compendium Phase 44 — Semantic Justification**.

## Paste method

The folders in this package mirror the project structure. Copy the contents into the root of `ProjectManagement-master` and replace the matching files.

There is **no EF Core migration and no database schema change** in Phase 45.

`CHANGED-FILES.txt` contains the complete replacement-file manifest.

## Verification commands

From the solution/project root, run:

```powershell
dotnet build
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --filter "FullyQualifiedName~Compendium"
node --check .\wwwroot\js\pages\projects-compendium.js
node --test .\wwwroot\js\projects\publications-compendium*.test.js
```

The supplied source was verified in the packaging environment with:

- JavaScript syntax check: PASS
- Full Compendium JavaScript contract suite: **280/280 PASS**
- Structural delimiter/lexical scan of all changed C# files: PASS

The packaging environment does not contain the .NET SDK, so `dotnet build` and xUnit could not be executed here. Run the two .NET commands above on the development machine before deployment.

## Manual QA — priority cases

### 1. Balanced + Flow below image + Fill

Use a project with a medium/long narrative. Confirm that:

- side narrative is visually at the same physical density as the generated PDF;
- the server-chosen semantic split no longer appears to leave a large artificial hole in Live Page;
- the continuation begins full width below the image;
- a small residual gap can remain when the next complete sentence genuinely cannot fit — this is intentional.

### 2. Live Page zoom/focus parity

Check **Fit**, **75%**, **100%**, and Review Focus. Narrative line wrapping should remain proportionally stable because body typography now scales with the displayed A4 sheet.

### 3. Balanced + Flow below image + Fit

Use a wide/landscape source image. Confirm that the side-flow budget and Live Page use the image's actual occupied Fit height. The below-flow text should no longer wait for empty frame height that the fitted image does not occupy.

### 4. Side column

Confirm that Side column behaviour is unchanged by the Flow-below optimiser.

### 5. Alignment

Toggle **Publication default / Left aligned / Justified**. Phase 44 alignment behaviour should remain intact.

## Approval/review semantics

Phase 45 does **not** perform a blanket review-fingerprint reset. The review contract remains the Phase 44 semantic-justification contract. This phase corrects proof geometry and candidate selection; it does not change authoritative project content.

## Design constraint retained intentionally

PRISM still does **not** break a sentence or word simply to remove every last point of whitespace beside an image. The planner prefers paragraph/sentence boundaries. The new scoring searches for a better measured image height first, and accepts a small editorial residual when a semantic boundary requires it.

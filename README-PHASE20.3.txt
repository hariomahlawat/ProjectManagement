PRISM Publications - Phase 20.3
Cover B QuestPDF Composition Fix
================================

ROOT CAUSE
----------
ComposeContemporaryCover() created a QuestPDF Layers element containing only
layers.Layer() calls. QuestPDF requires exactly one PrimaryLayer in every
Layers element. Therefore Cover B could pass PRISM preflight and approval but
fail only when QuestPDF physically composed the PDF, producing HTTP 500 for
both Preview and Download.

FIX
---
The full-page Forest950 institutional field is now the single QuestPDF
PrimaryLayer:

    layers.PrimaryLayer().Background(Forest950);

All other Cover B elements remain overlay Layer() elements. This preserves the
approved Cover B geometry while satisfying the QuestPDF layer topology.

The change deliberately mirrors the already-working Back Cover pattern and
does not modify the Digital page planner, project layout, Cover B approval
fingerprints, image-quality policy, or Print / Compact compositor.

DIAGNOSTICS
-----------
Unexpected PDF-composition HTTP 500 responses now carry the stable internal
JSON code:

    pdfCompositionFailed

The detailed exception remains server-log-only; no implementation internals
are exposed to normal users.

REGRESSION COVERAGE
-------------------
- Source contract requires exactly one PrimaryLayer inside
  ComposeContemporaryCover().
- C# runtime regression composes a Digital / Contemporary publication with
  real hero bytes, including a low-quality 1024x1024 hero case.
- Existing Cover A / Cover B builder tests remain intact.

LOCAL VALIDATION
----------------
Run:

    Set-ExecutionPolicy -Scope Process Bypass
    .\tools\Test-PrismPublicationsPhase20_3.ps1

The script performs source checks, JavaScript syntax/contract tests and, when
the .NET SDK is available, dotnet build + dotnet test.

DECISIVE RUNTIME CHECK
----------------------
Digital / Comfortable -> B Contemporary / Premium -> choose/approve hero ->
Preview PDF -> Download brochure PDF.

A low-DPI hero may remain a warning; it must not prevent PDF composition.

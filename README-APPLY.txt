PRISM PROCUREMENT JOURNEY — FINAL PROFESSIONAL POLISH
=====================================================

PREREQUISITE
------------
This is an incremental replacement package for the Procurement Journey redesign
and cinematic-refinement files already applied on 30 Jul 2026.

APPLICATION
-----------
1. Close the running PRISM application.
2. Copy the contents of this ZIP into the ProjectManagement project root.
3. Preserve the folder structure and replace the existing files.
4. Apply the included data migration:

   dotnet ef database update

5. Build and run tests:

   dotnet build
   dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj

6. Start PRISM and hard-refresh the Process page once with Ctrl+F5.

WHAT THIS REFINEMENT DOES
-------------------------
- Models Price Negotiation as a true conditional detour:
    Commercial Opening -> EAS remains the principal route.
    Commercial Opening -> Price Negotiation -> EAS is the optional route.
- Models Transfer of Technology as an optional detour after Payment and adds a
  non-stage "Capability complete" destination to make the bypass visible.
- Removes the mandatory EAS dependency on PNC from the authoritative workflow.
- Keeps TEC and Benchmarking parallel and mandatory before Commercial Opening.
- Replaces oversized branch arrowheads with restrained route markers.
- Uses a continuous base path plus a subtle animated travelling signal.
- Prevents TEC/BM branch remnants from leaking into unrelated later scenes.
- Fits the Complete Map more efficiently and increases useful vertical presence.
- Improves semantic map zoom without allowing adjacent nodes to overlap.
- Reduces the repeat-use hero to a compact 108 px command header and removes
  redundant journey CTA buttons once the introduction has been completed.
- Retains the full introduction through the existing Introduction control.
- Keeps all assets and runtime behaviour fully offline.

DATABASE CHANGE
---------------
Migration included:
  20261207180000_RefineProcurementJourneyTopology

The migration removes EAS -> PNC as a mandatory dependency for SDD-1.0 and
SDD-2.0 while ensuring EAS -> COB exists. It contains data-only SQL and does not
change the EF model snapshot.

ROLLBACK
--------
The migration Down method restores the former EAS -> PNC dependency. The files
can be reverted using IMPLEMENTATION.patch in reverse or source control.

# PRISM Photos — Phase 8.7 Identity Lifecycle and Known-Person Review

## Purpose

Phase 8.7 turns the working People pilot into a governed identity-lifecycle workflow. It focuses on correcting mistakes safely, resolving duplicate people, reviewing known-person suggestions with reference imagery, and making the audit trail visible to authorised reviewers.

## Implemented capabilities

### Identity correction

- Select one or more confirmed appearances on a person page.
- Move selected appearances to another existing person.
- Create a new person from selected appearances (split workflow).
- Return selected appearances to People Classification.
- Retire an invalid face detection and invalidate its current embedding.
- Prevent two faces from the same photograph from being assigned to the same person in a batch.
- Prevent moves or merges where the target already has a confirmed face in a selected photograph.
- Require a correction reason for moves, splits, returns to review, invalid-face retirement and merges.

### Duplicate-person governance

- Merge one person into another controlled surviving identity.
- Move all active assignments transactionally.
- Redirect pending known-person decisions where possible.
- Preserve the retired source identity as a hidden merged record.
- Preserve assignment-level and person-level audit history.

### Representative-face integrity

- Recalculate the representative face after an appearance is moved, removed or suppressed.
- Prefer an appearance backed by currently available media.
- Retain the person record when only unavailable-but-audited appearances remain.
- Automatically hide a person only when no active appearance remains.

### Known-person review

- Known-person candidate records now carry the confirmed person’s representative face.
- Group and individual review cards display the new face beside the confirmed reference face.
- Similarity is labelled as a ranking signal, not an identity probability.
- Links open the candidate person for contextual review before confirmation.
- Identity confirmation remains human controlled; no automatic naming is introduced.

### Visible audit history

Person details now display the latest 100 related identity events, including:

- person creation;
- appearance confirmation or movement;
- assignment removal;
- split and merge operations;
- rename, hide and restore;
- representative-face changes;
- invalid-face retirement;
- reviewer, timestamp and recorded reason.

### UI terminology and workflow

- “People classification” is presented as “Identify people” in the operational UI.
- “Confirmed faces” is presented as “confirmed appearances”.
- Face-image quality is explicitly distinguished from identity confidence.
- Individual appearance corrections explain that source photographs are not deleted.
- People directory action is labelled “Review people”.

## Data and migration impact

No database migration is required. Phase 8.7 uses the existing person, assignment, review-decision and identity-audit tables.

## Deployment

1. Back up the application and database.
2. Extract the ready-to-replace ZIP into the project root, preserving paths.
3. Clean, restore, build and test the solution.
4. Restart the application.
5. Verify the routes below:

```text
/Photos/People
/Photos/People/Details/{personId}
/Photos/People/Review?Mode=groups
/Photos/People/Review?Mode=faces
/Admin/MediaIntelligence
```

Recommended build commands:

```powershell
dotnet clean ProjectManagement.sln
dotnet restore ProjectManagement.sln
dotnet build ProjectManagement.sln --configuration Release --no-restore
dotnet test ProjectManagement.sln --configuration Release --no-build
```

## Functional verification

Use test data in a non-production environment and verify:

1. Move one appearance from Person A to Person B.
2. Split one or more appearances into a new person.
3. Return an appearance to review and confirm it appears in Individual Faces.
4. Mark a deliberately invalid detection as not a face.
5. Merge a duplicate person into the correct person.
6. Confirm that same-photograph conflicts are rejected.
7. Confirm that the representative face changes when the previous cover is removed.
8. Confirm that each operation appears in Identity history with reviewer and reason.
9. Upload a new image of a confirmed person, process it, refresh identity matches and verify the side-by-side known-person candidate.
10. Reject a candidate and verify that it is not immediately recreated for the same embedding-model version.

## Safety properties retained

- No automatic identity confirmation or automatic naming.
- Identity assignments remain human initiated.
- Content and embedding lifecycle safeguards remain active.
- Suppressed faces cannot remain active identity references.
- Corrections preserve audit history instead of deleting records.
- Source photographs are never deleted by identity correction actions.

## Validation completed in the packaging environment

- JavaScript syntax checks passed.
- Changed C# files passed lexical and delimiter checks.
- Razor structural tag/count checks passed for changed pages.
- Patch whitespace validation passed.
- ZIP integrity and package/path verification are performed during packaging.

The packaging environment does not contain the .NET SDK, MSBuild or a C# compiler. A successful Release build and .NET test run must therefore be completed on the development machine before deployment.

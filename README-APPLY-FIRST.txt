PRISM Industry Directory — Organisation permission refinement

Apply
-----
Copy the files in this ZIP into the ProjectManagement project root, preserving the folder structure and replacing existing files.

Final permission rules
----------------------
Add organisation:
- Admin
- HoD
- Comdt
- Project Officer
- Project Office / ProjectOffice
- MCO
- TA
- ITO

Edit an organisation and manage its files/JDP links:
- The user who created the organisation (record owner)
- Admin
- HoD
- Comdt

Delete organisation:
- Admin
- HoD

Unchanged contact rules:
- Any authenticated user may add a contact.
- A contact creator may edit/delete that contact.
- Admin, HoD and Comdt may edit/delete any contact.

Implementation notes
--------------------
- Create and edit-any permissions are separate policies.
- Ownership is checked server-side against IndustryPartner.CreatedByUserId for every organisation update, file mutation and JDP-link mutation.
- Direct edit URLs are neutralised for users who are neither owner nor an override role.
- No database migration is required; ownership is already recorded in the existing model.
- Automated authorization and ownership tests are included.

Verification
------------
dotnet build
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj

The .NET SDK was not available in the packaging environment, so the full build/test suite could not be executed here. Static reference and package-integrity checks passed.

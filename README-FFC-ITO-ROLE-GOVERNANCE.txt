PRISM ERP — FFC Role Governance / ITO Capability
Ready-to-paste cumulative package

PURPOSE
-------
This package is cumulative with the earlier "Role Governance + ITO Publications"
phase. It preserves ITO role provisioning and Brochure/Compendium rights, and adds
the approved FFC authorization model.

APPROVED FFC ROLE MODEL
-----------------------
Full FFC management:
  • Admin
  • HoD
  • Comdt
  • ITO

Full FFC management includes:
  • Create country-year FFC records
  • Edit records through the normal record workspace/form
  • Archive and restore records
  • Add, edit and remove FFC project rows
  • Maintain quantity, delivery and installation information
  • Upload and delete FFC attachments
  • Activate/deactivate FFC country master data
  • Use the normal/full project editor, including linked-project progress

Detailed Table inline editing:
  • Admin — allowed
  • HoD — allowed
  • Comdt — allowed
  • ITO — NOT allowed

The ITO restriction applies specifically to the two Detailed Table inline actions:
  1. Inline edit overall status
  2. Inline edit project progress

For ITO, these inline editors are not rendered as editable controls in the UI and
the corresponding server handlers also return Forbid/403 if invoked directly.

Project Office, Project Officer, MCO, TA and other ordinary authenticated roles
remain read/report users for FFC unless another existing policy grants a separate
capability. This phase does not add them to FFC management.

AUTHORIZATION ARCHITECTURE
--------------------------
The duplicated Admin/HoD checks in FFC have been replaced with two authoritative
contracts in ProjectOfficeReportsPolicies:

  FfcManagerRoles
    Admin, HoD, Comdt, ITO

  FfcInlineEditorRoles
    Admin, HoD, Comdt

Registered policies:
  ProjectOfficeReports.ManageFfc
  ProjectOfficeReports.InlineEditFfc

Razor UI visibility and server mutation handlers consume the same centralized
role contracts, preventing UI/server authorization drift.

LINKED PROJECT PROGRESS
-----------------------
FFC linked-project progress is stored as the canonical External Project Remark.
ITO must be able to use the normal/full FFC project editor, while still being
excluded from the Detailed Table inline progress editor.

For that reason this package adds RemarkActorRole.Ito for the FFC workflow only.
ITO is deliberately NOT added to the generic Identity-role-to-Remark parser.
Consequently, this does not grant ITO the normal Project Remarks composer or
unrelated project-remark authoring rights outside FFC.

The FFC workspace explicitly constructs the ITO remark actor only when saving
linked progress through the full FFC editor. Audit/read surfaces render this role
as "ITO".

ADMIN EFFECTIVE PERMISSIONS
---------------------------
Administration now shows the FFC distinction explicitly:
  • Manage the FFC portfolio — Admin, HoD, Comdt, ITO
  • Inline-edit FFC status and progress — Admin, HoD, Comdt

DATABASE
--------
There is no new database schema migration for this FFC phase.
The cumulative package retains the earlier idempotent migration:
  20261216170000_EnsureCanonicalIdentityRoles
which provisions the canonical ITO Identity role.

HOW TO APPLY
------------
1. Stop the application/IIS app pool if that is your normal deployment practice.
2. Copy the CONTENTS of this package into the ProjectManagement project root.
3. Preserve the folder structure and replace the matching files.
4. Build and run tests.
5. Restart the application.

RECOMMENDED VALIDATION
----------------------
PowerShell from the ProjectManagement root:

  dotnet build .\ProjectManagement.csproj
  dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

SMOKE TEST MATRIX
-----------------
Comdt account:
  • New record visible and usable
  • Normal record/project/attachment/country management works
  • Detailed Table inline overall-status edit works
  • Detailed Table inline project-progress edit works

ITO account:
  • New record visible and usable
  • Normal record/project/attachment/country management works
  • Normal/full linked-project progress save works
  • Detailed Table overall status is read-only
  • Detailed Table project progress is read-only
  • Direct calls to the two inline POST handlers are rejected (403)

Project Officer / Project Office account:
  • FFC remains readable
  • FFC management actions are not offered

IMPORTANT
---------
The .NET SDK is not installed in the packaging environment, so a real dotnet
build/test could not be executed here. Static authorization and source-contract
validation was completed and is supplied in VALIDATION-static.txt.

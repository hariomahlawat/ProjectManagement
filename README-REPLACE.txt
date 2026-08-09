PRISM ERP - Project Ideas permission/governance update

Permission matrix implemented
--------------------------------
View non-deleted Ideas:
  - Any authenticated/authorised PRISM user

Edit/update operational Idea record (title, description, assignments, Active/On Hold):
  - Assigned Project Officer
  - Any HoD
  - Comdt
  - Admin alone does NOT grant edit authority

Archive / restore from archive / delete / restore deleted Idea:
  - Comdt
  - HoD
  - Admin
  - Assigned Project Officer does NOT gain lifecycle authority by assignment

Unchanged behaviours
--------------------
  - Conference remark governance remains Comdt/HoD.
  - General/Conference remarks continue to count collectively.
  - Existing note/document collaboration permissions are preserved.
  - My Ideas remains assignment-based (Assigned Project Officer only).
  - Dashboard code is intentionally unchanged; organisation-wide Idea visibility now matches its non-deleted Idea population.
  - No database migration is required.

Production replacement files
----------------------------
Services/ProjectIdeas/ProjectIdeaGovernancePolicy.cs
Services/ProjectIdeas/ProjectIdeaPermissionService.cs
Services/ProjectIdeas/ProjectIdeaReadService.cs
Services/ProjectIdeas/ProjectIdeaCommandService.cs
Pages/ProjectIdeas/Index.cshtml.cs
Pages/ProjectIdeas/Details.cshtml.cs
Pages/ProjectIdeas/Details.cshtml
Pages/ProjectIdeas/Edit.cshtml.cs
Pages/ProjectIdeas/Edit.cshtml

Optional regression-test replacements
-------------------------------------
ProjectManagement.Tests/ProjectIdeaPermissionServiceTests.cs
ProjectManagement.Tests/ProjectIdeaReadServiceTests.cs
ProjectManagement.Tests/ProjectIdeaCommandServiceTests.cs
wwwroot/js/projects/project-ideas-governance-contract.test.js

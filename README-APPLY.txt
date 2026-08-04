PRISM ERP — Projects Repository Live Search Fix
================================================

Application method
------------------
1. Close the running application.
2. Copy the contents of this folder into the ProjectManagement project root.
3. Allow the existing files to be replaced and the five new Razor partials to be added.
4. Clean and rebuild the solution.
5. Start the application and perform a hard browser refresh once so the versioned JavaScript and CSS assets are reloaded.

Files replaced
--------------
Pages/Projects/Index.cshtml
Pages/Projects/Index.cshtml.cs
wwwroot/js/pages/projects-index.js
wwwroot/css/projects/index.css
ProjectManagement.Tests/ProjectRepositoryPresentationContractTests.cs

Files added
-----------
Pages/Projects/_ProjectRepositoryHeaderSummary.cshtml
Pages/Projects/_ProjectRepositoryLifecycle.cshtml
Pages/Projects/_ProjectRepositoryResults.cshtml
Pages/Projects/_ProjectRepositoryLiveMetadata.cshtml
Pages/Projects/_ProjectRepositoryLive.cshtml

Implementation
--------------
- The search input remains permanently in the DOM, so focus and caret position are retained.
- Search requests are debounced by 300 ms and sent through a dedicated Razor Pages GET handler.
- Previous requests are cancelled immediately when the query changes.
- Only the repository header summary, lifecycle counts, results, pagination and live metadata are replaced.
- Card/table preference, row navigation, image fallback, sorting, lifecycle tabs, pagination and filter actions continue to work after every live refresh.
- Browser URL and Back/Forward history remain synchronized.
- Static filter-option queries are skipped for live result requests.
- Normal GET form submission remains the progressive fallback.
- No database migration or configuration change is required.

Validation performed
--------------------
- JavaScript syntax validation passed with Node.js (`node --check`).
- Static implementation contract checks passed.
- A .NET compile was not run in the generation environment because the .NET SDK was not installed. Build the solution once in Visual Studio before deployment.

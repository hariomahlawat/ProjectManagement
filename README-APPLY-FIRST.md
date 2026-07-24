# PRISM ERP — Project Overview JDP Header

## Which bundle to use

- **Incremental production bundle**: use when `Project-Overview-Proliferation-v1` has already been applied.
- **Cumulative production bundle**: use when starting from the uploaded `ProjectManagement-master (6)(3).zip`, or when unsure whether the proliferation files were all applied.
- **Ready-to-replace bundle**: cumulative production files plus regression tests and this guide.

Extract the selected ZIP into the directory containing `ProjectManagement.csproj` and allow matching files to be replaced.

## Implemented behaviour

- Replaces the low-value **Lifecycle progress** header card with **JDP**.
- A project has either one JDP or no JDP.
- The JDP card shows the linked organisation and whether that organisation is linked to other ongoing or completed projects.
- Clicking the card opens a right-side JDP drawer.
- Authorised users can search the existing Industry Directory, link/change the JDP, or remove it without leaving the project page.
- The same drawer remains read-only for users without edit authority.
- JDP edit rights are enforced server-side for Admin, HoD, Comdt and the assigned Project Officer.
- The lower JDP panel remains synchronised after an AJAX update and links to the organisation record.
- Existing directory creation remains available as a secondary action when an organisation is not yet recorded.
- Existing legacy records with more than one JDP link are explicitly flagged; selecting and saving the correct JDP safely removes only the extra links for that project.
- Existing partner links to other projects are never removed when this project's JDP is changed or removed.

## Database impact

The JDP enhancement requires **no new migration**. It uses the existing `IndustryPartnerProjects` relationship. The application service now enforces the one-project/one-JDP contract across both the overview workflow and the existing directory link workflow.

The cumulative bundle also contains the previously supplied proliferation migration because it includes the complete proliferation implementation.

## Verification

Run from the project root:

```powershell
dotnet build ProjectManagement.sln
dotnet test ProjectManagement.Tests/ProjectManagement.Tests.csproj
```

Then verify:

1. Open a project with no JDP and link an organisation from the header card.
2. Confirm the card and lower JDP panel update without a page reload.
3. Open a project whose JDP is linked to other projects and confirm ongoing/completed counts and project links.
4. Change the JDP and confirm links belonging to other projects remain intact.
5. Sign in as an unrelated Project Officer and confirm the drawer is read-only.

## Validation completed in this environment

- JavaScript syntax validation passed with `node --check`.
- Static source-contract and delimiter checks passed.
- The .NET SDK was not available in this environment, so the C# build and xUnit suite could not be executed here.
- The repository-wide JavaScript suite could not be completed because `jsdom` is not installed in this environment.

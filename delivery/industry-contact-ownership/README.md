# Industry Directory — contact ownership and final UX polish

This delivery is a **delta for the JDP-only Industry Directory currently shown in the 18 July 2026 screenshots**. Apply it after the earlier `industry-directory-final-refinements` package.

## Permission model implemented

| User | View | Add contact | Edit/Delete contact |
|---|---:|---:|---:|
| Any authenticated PRISM user | Yes | Yes | Own contacts only |
| Admin | Yes | Yes | Any contact |
| HoD | Yes | Yes | Any contact |
| Comdt | Yes | Yes | Any contact |

Existing contacts have no recorded author. They remain viewable to everyone, but only Admin, HoD or Comdt can edit or remove them.

The rule is enforced in both the UI and the service layer. Hiding an action in Razor is not treated as authorization.

## UX refinements included

- Removes the duplicate Add Contact button from the Contacts-tab heading.
- Keeps one Add Contact action in the drawer toolbar.
- Hides the toolbar action while the add-contact form is open.
- Uses a full-width contact-person/office field, with Phone and Email beneath it.
- Uses `Primary` instead of `Main`.
- Keeps blank contact names readable as `General contact`.
- Hides organisation-level actions while the organisation edit form is active.
- Slightly reduces oversized directory empty-state height.

## Database change

Migration included:

`20260718003500_AddIndustryPartnerContactOwnership`

It adds nullable `CreatedByUserId` to `IndustryPartnerContacts`. Nullable is deliberate so that existing records are preserved without guessing ownership.

## Apply

From the project root:

```powershell
.\delivery\industry-contact-ownership\Apply-IndustryDirectoryContactOwnership.ps1
```

Or apply the patch manually:

```powershell
git apply --check .\delivery\industry-contact-ownership\industry-directory-contact-ownership.patch
git apply .\delivery\industry-contact-ownership\industry-directory-contact-ownership.patch
```

Then run:

```powershell
dotnet restore
dotnet build
dotnet test
```

Start the application and allow the existing automatic EF migration startup process to apply the migration.

## Verification

1. Sign in as an ordinary authenticated user and add a contact.
2. Confirm the same user can edit and delete that contact.
3. Sign in as another ordinary user and confirm Edit/Remove are not offered.
4. Confirm a forged POST from the second user returns access denied.
5. Confirm Admin, HoD and Comdt can edit/remove the contact.
6. Confirm an old contact created before this migration is editable only by Admin, HoD or Comdt.
7. Confirm organisation editing, JDP linking and file management retain their existing separate permissions.

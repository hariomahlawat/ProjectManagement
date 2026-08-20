# PRISM Photos — User Linkage Governance Hardening

This is a ready-to-paste **delta package** for the current PRISM Photos implementation.
It assumes the immediately preceding Photos Person Discovery / PRISM User Linkage phase is already present.

## Apply

1. Stop PRISM / IIS application pool if this is a production deployment.
2. Copy the contents of this ZIP into the PRISM project root and overwrite matching files:

   `E:\Dot Net Web Development\ProjectManagement\`

3. Keep the new migration file together with the Media Library snapshot and immutable migration manifest.
4. Clean, build, test, then restart PRISM.

## Database migration

New immutable Media Library migration:

`20260820103000_HardenMediaPersonUserLinkExperience`

It adds:
- explicit user opt-in for using the linked Photos portrait as the PRISM avatar;
- governed account-link concern/report fields;
- an index for open identity-link concerns.

Do not manually add these columns while omitting the migration history entry. Use the existing PRISM controlled migration/startup mechanism.

## Functional changes

- Linking a Media Person to a PRISM user now requires an explicit visual verification confirmation in the manager UI.
- Linking does **not** automatically replace the PRISM avatar.
- The linked user can opt in/out of the Photos portrait from Profile.
- `My Photos` opens the confirmed gallery first; Find More Photos remains an explicit action.
- A linked user may report **This isn't my identity** from Profile.
- While that report is open:
  - Photos portrait use is disabled;
  - My Photos is suspended;
  - linked-user self-review is suspended;
  - an Admin/HoD identity manager must resolve the report or unlink the account.
- People management gains linkage/review-state visibility and filtering.
- Already-linked PRISM accounts are shown as unavailable for a second active link.

## Build / validation after paste

```powershell
dotnet clean
Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue
dotnet build .\ProjectManagement.csproj
dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

node --check .\wwwroot\js\pages\photos-person-linkage.js
node --check .\wwwroot\js\pages\photos-person-profile.js
node --test .\wwwroot\js\pages\photos-person-linkage-contract.test.js .\wwwroot\js\pages\photos-person-profile-contract.test.js
```

## Production verification

1. As Admin/HoD, open a confirmed Photos person and search for a PRISM user.
2. Click **Link user** and verify the confirmation dialog requires visual verification before submission.
3. Verify the linked account's avatar does not change automatically.
4. Sign in as the linked user; open **Profile** and explicitly enable **Use Photos portrait**.
5. Verify **My Photos** opens the confirmed gallery without automatically expanding Find More Photos.
6. Open **Find more photos of me** and verify only that linked user can self-review their own candidates.
7. From Profile, submit **This isn't my identity**.
8. Verify the Photos avatar is removed and My Photos/self-review are blocked while the concern is open.
9. As Admin/HoD, resolve the concern or unlink the account and verify access/state updates accordingly.

# My account

The signed-in user's own page (`/Identity/Account/Manage`) and Change password
(`/Identity/Account/Manage/ChangePassword`). Redesigned 2026-11-13: the old page showed roles
and a password button on a 1,180px card and did not say whose account it was.

## Layout

One 760px column, in this order:

1. **Who you are**: picture or initials, rank and full name (the user name when the account
   has no full name), user name, last recorded sign-in in IST, and roles as chips.
2. **PRISM profile image** and **Photos identity**, only when the account is linked to a
   person in Photos. The picture follows `ShouldUsePortraitAsAvatar`, the same resolved state
   the header uses. "This isn't me" opens the report form. While a report is open the rows
   are replaced by a notice.
3. **Appearance**: Light / Dark / System. These are the same `data-theme-preference-value`
   controls and `prism.theme` storage as the user menu (`wwwroot/js/core/theme-preference.js`).
4. **Password**: a link to Change password.
5. **Sign out**: posts to `/Identity/Account/Logout`, as the user menu does.

The user menu item is "My account" (was "Profile"), matching the page title.

## Change password

Same narrow column and card. The rules under the new password come from the configured
Identity options (`IdentityPasswordPolicy.Describe`), so they cannot drift from what is
enforced. A Caps Lock hint shows while a password field has focus
(`wwwroot/js/pages/account-password.js`; the form works without it).

When the account must change its password (`MustChangePassword`;
`EnforcePasswordChangeFilter` sends every other page here) the page says so and offers no
"My account" link or Cancel, since both would only bounce back.

## Not changed

Handlers, routes, authorization, the Photos identity service and what a password change
does are as before. This was markup, wording and styles, plus read-only display fields on
the page model.

## Files

| Concern | File |
| --- | --- |
| Account page | `Areas/Identity/Pages/Account/Manage/Index.cshtml` (+ `.cs`) |
| Change password | `Areas/Identity/Pages/Account/Manage/ChangePassword.cshtml` (+ `.cs`) |
| Styles for both | `wwwroot/css/pages/account.css` (site tokens only; no colour literals, no Dark overrides) |
| Caps Lock hint | `wwwroot/js/pages/account-password.js` |
| Tests | `ProjectManagement.Tests/AccountManagePageTests.cs`, `AccountPhotoAvatarContractTests.cs` |

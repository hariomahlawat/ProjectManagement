# Birthday and anniversary wish

On a person's birthday or wedding anniversary PRISM wishes them: balloons and confetti rise over
the page and a glass card in the middle reads "Happy Birthday, Col R Sharma". An anniversary uses
heart balloons in gold and rose and names the couple ("Happy 25th Anniversary, Col R Sharma &
Anita" when the wedding year and spouse name are on record). A birthday never states the age.

Only the person sees their own wish. Everyone else already sees the day on the Calendar.

## Who is wished

A person is wished when a live entry on **Celebrations** is linked to their account and falls
today (IST). The link is the optional "PRISM account" on the celebration form; the Celebrations
list has a "Not linked" filter and offers a one-click "Link to ..." when the name on an entry fits
exactly one account. An entry for 29 February is wished on 28 February in other years, as on the
Calendar. If a birthday and an anniversary fall on the same day, the birthday is wished.

## Adding your own

**My account** has a "Birthday and anniversary" section for every person's account:

- **Is this you?** When an entry on Celebrations fits the person's name and no one else's, the
  section offers it: "Yes, that's me" links it. No other entry can be linked from here.
- **Add my birthday / anniversary** creates a Celebrations entry already linked to the account,
  named with the person's rank and name.
- **Change** edits the date (and spouse name) of the linked entry. **Unlink** stops the wishes;
  the entry stays on the Calendar for the Celebrations managers to keep or delete.

A linked entry is shown everywhere (Calendar, Dashboard, Workspace, the Celebrations list, the
"Wish ..." task) under the account's rank and name, so a promotion shows at once; the name written
on the entry is used only while it is not linked (`CelebrationHelpers.DisplayName`).

There is one record of each date: the Celebrations entry. The account holds nothing, so My
account and the Celebrations list can never disagree, and each change is in the same audit log.
The server only ever reads or changes the entry linked to the signed-in account
(`Services/Celebrations/MyCelebrationService.cs`).

## When it plays

- On the first page after every sign-in on the day.
- After that at most once an hour, and only as a page opens. It never starts in the middle of a
  page, so it cannot interrupt typing.
- Whenever the small balloon beside the person's name (top right) is clicked. The balloon is there
  all day.
- Never while something is shown full screen, such as a briefing being presented.

It closes by itself after about seven seconds, or at once on any click or Esc. The click still
does whatever it was aimed at; the page underneath stays usable throughout. There is no sound.

On the day the Dashboard header reads "Happy Birthday, Col R Sharma" beside the date, all day.

## How it is built

| Part | Where |
| --- | --- |
| Today's wish for an account | `Services/Celebrations/CelebrationWishService.cs` |
| Card markup, only on the day | `Pages/Shared/Components/CelebrationWish/Default.cshtml`, from `_Layout` |
| When it plays; balloons and confetti | `wwwroot/js/components/prism-wish.js` |
| Look | `wwwroot/css/components/prism-wish.css` (site tokens only) |

The service never throws: if the wish cannot be read the page renders without it. When it last
played is kept in the browser's local storage (`prism.wish.<user id>`), so nothing is written to
the database and nothing is sent anywhere. Everything is served by PRISM itself; no Internet
access is needed. Reduce Motion shows the card without the balloons; Reduce Transparency makes the
card solid.

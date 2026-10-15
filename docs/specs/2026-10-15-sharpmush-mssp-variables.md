# Six more MSSP variables, read for what they say

SharpMUSH reports six variables beyond the generic MSSP set. Any codebase may send them; they arrive
over option 70 or the plaintext `MSSP-REQUEST` like every other variable, are stored under the `mssp`
source, and render as **declared** — the game's statement about itself, never a measurement of ours.
None of them reaches presence, reachability or a ranking. Names and reading rules live in one place,
`SelfReport` (`src/MUI.Catalog/Fields/SelfReport.cs`).

| Variable | Holds | How it is stored and shown |
|---|---|---|
| `PLAYERS DAY` | distinct players connected in the last 24 hours | `FieldReconciler.MovingFields`: overwritten without a change row, refresh window one day (`FieldRegistry.Rolling`) |
| `PLAYERS WEEK` | distinct players connected in the last 7 days | as `PLAYERS DAY` |
| `LOGINS` | `1`/`0`: players may log in | `FieldShape.Flag`, shown yes/no; a flip is a change-feed event |
| `PLAYER CREATION` | `1`/`0`: new characters at the login screen | as `LOGINS` |
| `GUESTS` | `1`/`0`: guests may connect | as `LOGINS` |
| `PORTAL APPLICATION` | repeated: an absolute URL, a space, a display name | every value kept (`SelfReport.Lists`, one per line); a line is a link only when it is an http(s) address and a name |

**The two counts move without an event.** They change whenever somebody new connects, so a
`field_change` row per probe would bury every real event in the feed — the reason `PLAYERS` and
`UPTIME` are not stored at all. Unlike those, nothing else holds these numbers, so they are kept,
dated by `last_confirmed_at`, and overwritten in place. The game page says them under our measured
figure and apart from it — "By the game's own count: 12 different players in the last day, 40 in the
last week" with a declared chip and its age — and never feeds them into the presence series: that
series holds counts read at a moment, and a game's own sum over a window is not one.

**Rule 4 holds on both sides of the store.** A count that is not a whole number at or above zero is
kept verbatim, because the row holds what the game said, but it is never read as a number: it stays
under "Declared by the game" in the game's own words and the sentence leaves that side out. A flag
other than `1` or `0` prints as sent rather than as our paraphrase of it. A portal line that is not an
address and a name prints as text — refused as a link, never hidden.

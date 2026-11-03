# WeShopAlot Product Roadmap

## Product goal

By the end of Sprint 5 (Dec 13), WeShopAlot has an honest, documented HTTP contract that the Angular and WPF clients both follow. It handles checkout failures gracefully and runs as a shared, tested instance that each team can demo end to end.

> Draft goal for the Product Owner to confirm or edit.

## Roles

| Role | Who |
|---|---|
| Product Owner | Mike Costarella: owns this roadmap and the backlog order |
| Scrum Master / Architect | Abdullah: runs the Scrum events; signs off on API contract changes |
| Sprint Team A | jgpranto, Nihalyazin |
| Sprint Team B | sumaiai, yashpulapalli-eng |

## How to read this roadmap

- Each theme below is an **epic** issue; its backlog items are the epic's **sub-issues**.
- Dates live on the [WeShopAlot board](https://github.com/orgs/MyWebSiteParticipants/projects/2) (Roadmap view), not in this file.
- Teams pull sub-issues into a sprint at sprint planning and tag them with their **Sprint Team**.
- Changing the order of themes is a Product Owner decision, made by a pull request to this file.

## Themes

| Sprint | Dates | Theme (epic) | What it delivers | Course modules |
|---|---|---|---|---|
| 2 | Oct 19 – Nov 1 | [#25 Onboarding](https://github.com/MyWebSiteParticipants/WeShopAlot/issues/25) | Everyone runs the API, Angular and WPF clients locally; GETTING_STARTED.md; one merged PR each | 1–2 |
| 3 | Nov 2 – Nov 15 | [#26 Fix the API contract](https://github.com/MyWebSiteParticipants/WeShopAlot/issues/26) | Basket becomes `PUT/GET/DELETE /api/baskets/{id}`; both clients move to it; delivery-method auth decision; accurate Swagger padlocks | 2, 9, 10 |
| 4 | Nov 16 – Nov 29 | [#27 Checkout edge cases](https://github.com/MyWebSiteParticipants/WeShopAlot/issues/27) | Declined-card handling, two-tab basket rule, idempotent order submit, consistent error responses | 6, 10, 11 |
| 4 | Nov 16 – Nov 29 | [#28 Brand & fleet conventions](https://github.com/MyWebSiteParticipants/WeShopAlot/issues/28) | New WeShopAlot brand replacing Wired Brain; fleet hamburger accordion menu in both clients | 7 |
| 5 | Nov 30 – Dec 13 | [#29 Ship it](https://github.com/MyWebSiteParticipants/WeShopAlot/issues/29) | Integration and client tests, deployment to the YSU server, final demo | 12–13 |

## Already done (before Sprint 2)

- Upgraded to current .NET and Angular versions (October 2026), including the saved-address fix
- Cards / List product views in both clients
- Footer build stamp and API URL
- Seeder moved into `WeShopAlot.Data` (`DatabaseSeeder`), and a SQL Server integration-test tier that rebuilds a database per run with it (November 2026; `docs/Database.md`). Part of the Sprint 5 testing theme, delivered early; #21 updated

## Working agreement for contract changes

Any change to an API route, request or response shape starts in an issue that both teams can see. The architect signs off before either client team starts, so Team A and Team B never build against different contracts.

Reference: [Service → UI walkthrough](https://mikecostarella.github.io/CS_AngularAndDotNetAPI/#/walkthrough)

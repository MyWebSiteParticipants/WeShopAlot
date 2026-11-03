# Sprint 3 testing stories (Nov 2 – Nov 13)

Backlog items for the WeShopAlot board, written as GitHub issues: copy a title into
the issue title and the block under it into the body. Each is one pull request into
`dev` from a branch named as shown. Module 6 (unit testing) is the lecture behind
them; `WeShopAlot.Testing.Unit` is the project they grow.

Done already, in the PR that opened this project: `ProductSpecParams`,
`ProductsWithTypesAndBrandsSpecification`, `SpecificationEvaluator`, `Order`
defaults and `MappingProfiles` tests, the `Search = null` fix, and CI running
the unit project first.

Also done, in the PR that moved seeding into `WeShopAlot.Data`: `DatabaseSeeder`
(`RecreateAsync` + `SeedAsync`, shared by the console app and the tests), the SQL
Server test tier (`WeShopAlotSqlApiFactory` + `SqlApiTests`, a throw-away LocalDB
database per run), and `SeedDataTests` on the JSON. `docs/Database.md` explains it.
Stories 9–11 build on that tier.

---

## 1. PageIndex below 1 produces a negative Skip

**Branch:** `test/spec-params-page-index` · **Size:** S · **Label:** bug, testing

`ProductsWithTypesAndBrandsSpecification` computes `Skip = PageSize * (PageIndex - 1)`.
`GET /api/product?pageIndex=0` therefore asks for `Skip(-6)`. LINQ-to-Objects treats
that as 0; SQL Server's `OFFSET -6 ROWS` does not.

**Acceptance**
- [ ] A failing unit test first: `ProductSpecParams { PageIndex = 0 }` (and `-1`) must behave as page 1.
- [ ] Decide where the clamp lives (the `PageIndex` setter, like the `PageSize` cap) and implement it.
- [ ] Add the boundary trio for the cap while you are there: 49, 50, 51.
- [ ] `dotnet test WeShopAlot.Testing.Unit` green; `ApiTests` still green.

## 2. Secondary sort for equal prices

**Branch:** `test/spec-sort-ties` · **Size:** S · **Label:** testing, design

`priceAsc` and `priceDesc` replace the name ordering entirely, so two products at
the same price come back in whatever order the database likes. The constructor also
calls `AddOrderBy(x => x.Name)` and then overwrites it, which reads like a bug.

**Acceptance**
- [ ] A test in `SpecificationEvaluatorTests` with two products at the same price that pins the order you want (name as the tie-breaker is the obvious choice).
- [ ] `BaseSpecification` grows a `ThenBy` (or the evaluator applies name after price) so the test passes.
- [ ] Remove the dead `AddOrderBy(x => x.Name)` call if it no longer does anything, and say so in the PR.

## 3. MappingProfiles passes AssertConfigurationIsValid

**Branch:** `test/mapping-config-valid` · **Size:** M · **Label:** testing

`MappingProfilesTests` builds the configuration but never calls
`AssertConfigurationIsValid()`. Add that call as its own test and make it pass.
Expect it to fail first: destination members such as `Address.Id`, `Address.IsDeleted`
and `CustomerBasket.ClientSecret` have no source. Each one is either `Ignore()` with
a reason, or a real missing mapping.

**Acceptance**
- [ ] `Configuration_IsValid` test exists and is green.
- [ ] Every `Ignore()` added has a one-line comment saying why the member is not mapped.
- [ ] No behavior change to the API (`ApiTests` green).

## 4. TokenService unit tests

**Branch:** `test/token-service` · **Size:** S · **Label:** testing

`TokenService.CreateToken` needs only an `IConfiguration` with `Token:Key` and
`Token:Issuer`. Build one in memory, create a token, parse it back with
`JwtSecurityTokenHandler`, and assert on what the API's authentication checks.

**Acceptance**
- [ ] Tests in `WeShopAlot.Testing.Unit/Services/TokenServiceTests.cs`: issuer matches configuration; name and email claims present; expiry about one day out; signed with HS512.
- [ ] Remove the empty `try { } catch { throw; }` around `WriteToken` (it does nothing) in the same PR, since the tests now cover the method.

## 5. Server-side price rule as a pure function

**Branch:** `test/order-subtotal-pure` · **Size:** M · **Label:** testing, refactor

`Checkout_BasketToOrder_UsesServerPrices` proves over HTTP that an order uses catalog
prices, not the client's. The arithmetic lives inside `OrderService.CreateOrderAsync`
next to repository calls, so it can only be tested by booting the API.

**Acceptance**
- [ ] Extract the item-pricing and subtotal step into a pure static method (for example `OrderPricing.Price(basketItems, products)`), with no repository or DbContext parameter.
- [ ] Unit tests: client price ignored; quantity multiplies; subtotal sums; an item whose product is missing is a clear failure, not a silent zero.
- [ ] `OrderService` calls the new method; `ApiTests` still green (it is the safety net for this refactor).

## 6. Angular: calculateTotals as a pure function

**Branch:** `test/basket-totals-pure` · **Size:** S · **Label:** testing, client

`BasketService.calculateTotals()` is private and reached only through a flushed HTTP
request in `basket.service.spec.ts`. The same move the lecture made with Bullpen's
risk guard applies: pull the arithmetic out.

**Acceptance**
- [ ] `src/app/shared/models/basket.ts` (or a new `basket-totals.ts`) exports `calculateTotals(basket: Basket): BasketTotals`.
- [ ] `basket-totals.spec.ts` covers: empty basket; one item × quantity; shipping added; two items summed. No `TestBed`, no `HttpTestingController`.
- [ ] `BasketService` calls it; existing specs unchanged and green (`npx ng test --watch=false`).

## 7. Retire the empty test projects

**Branch:** `chore/empty-test-projects` · **Size:** S · **Label:** housekeeping

`WeShopAlot.Testing.Services`, `WeShopAlot.Testing.Repositories` (namespace
`OurGovRepositoryTests`) and `WeShopAlot.Testing.XSLT` each hold one `TestMethod1`
that asserts nothing. `WeShopAlot.Testing.Shared` carries `OurGov.*` namespaces and a
`Dispose()` that throws `NotImplementedException`; its `BaseIntegrationTestsClass` was
an early sketch of what `WeShopAlotSqlApiFactory` now does, so nothing is lost.

**Acceptance**
- [ ] Either delete the three projects and `Testing.Shared` from the solution, or give each a first real test — decide in the PR description, not silently.
- [ ] No `OurGov` namespace remains in the repo (`git grep OurGov` is empty).
- [ ] Solution builds; CI green.

## 8. Test results and coverage visible on every PR

**Branch:** `ci/test-reports` · **Size:** M · **Label:** ci

CI runs the suites but a reviewer has to open the log to see counts. Publish them.

**Acceptance**
- [ ] `dotnet test` runs with `--logger trx --collect:"XPlat Code Coverage"` for both .NET test projects and the results are uploaded as workflow artifacts.
- [ ] A test-summary step (for example `dorny/test-reporter` or `EnricoMi/publish-unit-test-result-action`) posts pass/fail counts on the PR.
- [ ] Angular: `ng test --watch=false --code-coverage` and the `coverage/` folder uploaded the same way.
- [ ] `docs/BuildGate.md` updated with where to find the reports.

## 9. The SQL Server test tier runs in CI

**Branch:** `ci/sqlserver-tier` · **Size:** M · **Label:** ci, testing

`SqlApiTests` run on every developer machine with LocalDB but CI skips them
(`--filter "TestCategory!=SqlServer"`), because the Linux runners have no SQL Server.
GitHub Actions can start one as a service container.

**Acceptance**
- [ ] The `api` job in `.github/workflows/ci.yml` declares a `services:` entry for
      `mcr.microsoft.com/mssql/server:2022-latest` with `ACCEPT_EULA=Y`, a strong
      `MSSQL_SA_PASSWORD` (a literal is fine: the container lives for one job and is
      unreachable from outside), port 1433, and a health check (`/opt/mssql-tools18/bin/sqlcmd -C -Q "SELECT 1"`).
- [ ] The `WeShopAlot.Testing.WebApis` step sets `WESHOPALOT_TEST_SQLSERVER` to
      `Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=true` (no
      database name) and drops the filter, so both tiers run.
- [ ] A deliberate failure first: open the PR with a pending-migration test broken on
      purpose (or a wrong count in `Catalog_IsTheShippedOne`), watch CI go red, revert.
      The PR description links both runs.
- [ ] `docs/BuildGate.md` and `docs/Database.md` (section 4, "In CI") updated.

## 10. The other money columns are still decimal(5,2)

**Branch:** `fix/money-precision` · **Size:** M · **Label:** bug, testing, design

`Order.Subtotal` was widened to `decimal(18,2)` in the `AddressNames` migration because
orders over $999.99 failed to save. The other five money columns were left as they were:
`Product.Price`, `DeliveryMethod.Price`, `BasketItem.Price`, `OrderItem.Price` and
`CustomerBasket.ShippingPrice` are all `[Column(TypeName = "decimal (5,2)")]`, so no
product can cost $1,000 or more. Add a $1,200 guitar to `products.json` and the seeder
dies with a SQL arithmetic-overflow error; the in-memory tests never notice, and
`SeedDataTests.Prices_FitTheDecimal5_2Columns` only keeps the *shipped* data under the
limit. This is the first story that needs the SQL Server tier to prove the bug and the fix.

**Acceptance**
- [ ] A failing `SqlApiTests` test first: seed one extra product priced 1200.00 through
      `DatabaseSeeder.SeedAsync(db, data)` with a modified `SeedData`, and assert it can be
      read back through `GET /api/product` (today the seed throws).
- [ ] Decide the precision with the Product Owner (`decimal(18,2)`, matching `Subtotal`, is
      the obvious answer), change the five attributes, add one migration that alters the five
      columns, and run the seeder.
- [ ] Update `Prices_FitTheDecimal5_2Columns` to the new limit (and rename it).
- [ ] `Schema_EveryMigrationApplied_NonePending` and the new test green; the fast tier
      unchanged.
- [ ] Angular and WPF: check whether anything formats or caps a price at four digits.

## 11. One more question only the database can answer

**Branch:** `test/sql-tier-<topic>` · **Size:** S · **Label:** testing

Pick one behavior from the list below, write it as a `SqlApiTests` test that fails or
is missing today, and make it pass. One story per developer; coordinate in standup so
no two people take the same one.

- `GET /api/product?pageIndex=0` against SQL Server (`OFFSET -6 ROWS` fails where LINQ
  to Objects silently gave page 1). Pairs with story 1: the SQL test is the proof the
  clamp was needed.
- Case-insensitive search: `?search=BLONDIE` finds Blondie. Works today because of
  `ToLower()`; the SQL tier shows whether the database collation would have handled it
  anyway, and what the generated SQL looks like (turn on EF Core logging in the factory).
- Deleting a user's address: does `Address.IsDeleted` soft-delete, or is the row gone?
  Read the row back after `DELETE /api/account/address` (if the endpoint exists; if it
  does not, that is the finding).
- Two orders for the same user: `GET /api/order` returns both, newest first, from the
  database's ordering and not from insertion luck.

**Acceptance**
- [ ] The test reads the rows (`factory.CreateDbContext()`), not only the HTTP response,
      for at least one assertion.
- [ ] The PR description says in one sentence why the in-memory tier could not have
      caught this.


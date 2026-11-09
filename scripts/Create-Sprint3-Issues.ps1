<#
.SYNOPSIS
    Puts the November 2026 seeder/SQL-tier changes onto the WeShopAlot board.

.DESCRIPTION
    Run once, in Windows PowerShell, after "gh auth status" says you are signed in.
    What it does, against MyWebSiteParticipants/WeShopAlot:

      1. Comments on #21 (Integration tests for the purchase flow) and #14 (idempotent
         order submit) with what the new test tiers already cover and what is left.
      2. Creates three issues in the board's format ([Area] title, Why, Done when,
         course link) for Sprint 3 stories 9, 10 and 11, and appends them to the
         sub-issue lists of their epics (#29 Ship it, #27 Checkout edge cases).
      3. With -IncludeStories1To8, also creates issues for testing stories 1-8 under
         #29. Off by default: that is a Product Owner call.

    -WhatIf prints every gh command without running it.

.EXAMPLE
    cd C:\projects\WeShopAlot
    .\scripts\Create-Sprint3-Issues.ps1 -WhatIf
    .\scripts\Create-Sprint3-Issues.ps1
    .\scripts\Create-Sprint3-Issues.ps1 -IncludeStories1To8
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Repo = 'MyWebSiteParticipants/WeShopAlot',
    [switch]$IncludeStories1To8
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding $false
$tmp  = Join-Path $env:TEMP 'weshopalot-issues'
New-Item -ItemType Directory -Force -Path $tmp -WhatIf:$false | Out-Null   # temp files only; always create, even under -WhatIf

function Write-Body([string]$name, [string]$text) {
    $path = Join-Path $tmp "$name.md"
    [IO.File]::WriteAllText($path, $text.Trim() + "`n", $utf8)
    return $path
}

function New-BoardIssue([int]$Epic, [string]$Title, [string]$Body) {
    $file = Write-Body ("new-" + ($Title -replace '[^A-Za-z0-9]+', '-')) $Body
    if ($PSCmdlet.ShouldProcess("$Repo", "gh issue create --title `"$Title`" (epic #$Epic)")) {
        $url = gh issue create --repo $Repo --title $Title --body-file $file
        if ($LASTEXITCODE -ne 0) { throw "gh issue create failed for '$Title'" }
        $number = [int]($url -replace '.*/issues/(\d+).*', '$1')
        Write-Host "  created #$number  $Title"
        return $number
    }
    return 0
}

function Add-ToEpic([int]$epic, [int[]]$numbers) {
    $numbers = $numbers | Where-Object { $_ -gt 0 }
    if (-not $numbers) { return }
    if ($PSCmdlet.ShouldProcess("#$epic", "append sub-issues $($numbers -join ', ') to the epic body")) {
        $body = gh issue view $epic --repo $Repo --json body --jq .body
        if ($LASTEXITCODE -ne 0) { throw "could not read epic #$epic" }
        $body = $body.TrimEnd()
        foreach ($n in $numbers) { if ($body -notmatch "(?m)^- #$n\s*$") { $body += "`n- #$n" } }
        $file = Write-Body "epic-$epic" $body
        gh issue edit $epic --repo $Repo --body-file $file | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "gh issue edit failed for epic #$epic" }
        Write-Host "  epic #$epic now lists $($numbers -join ', ')"
    }
}

function Add-Comment([int]$Issue, [string]$Text) {
    $file = Write-Body "comment-$Issue" $Text
    if ($PSCmdlet.ShouldProcess("#$Issue", "gh issue comment")) {
        gh issue comment $Issue --repo $Repo --body-file $file | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "gh issue comment failed for #$Issue" }
        Write-Host "  commented on #$Issue"
    }
}

gh auth status 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "gh is not signed in. Run: gh auth login" }

Write-Host "1. Status comments on existing items"
Add-Comment -Issue 21 -Text @'
**Status, 3 Nov 2026** (Product Owner)

Most of this landed early, in the seeder/SQL-tier PR on master:

- `WeShopAlot.Testing.WebApis/ApiTests` (in-memory tier, 19 tests) covers products, basket, address, delivery methods, payment intent and order creation over real HTTP.
- `SqlApiTests` (new SQL Server tier, 9 tests) runs the same API against a real database rebuilt per run by `DatabaseSeeder`: migrations applied, the shipped catalog, SQL ordering, and a checkout whose `Order` row satisfies the foreign keys.
- CI runs the unit and in-memory tiers on every PR.

Still open from the *Done when* list: the SQL Server tier does not run in CI yet (the Linux runners have no SQL Server). That is its own item now — see the `[CI]` sub-issue under this epic. How the tiers work, and how to add a test to the SQL one: `docs/Database.md`, section 4.
'@
Add-Comment -Issue 14 -Text @'
**Note, 3 Nov 2026** (Product Owner)

The automated test for this belongs in the SQL Server tier (`WeShopAlot.Testing.WebApis/SqlApiTests`, `[TestCategory("SqlServer")]`): submit the same basket twice through `POST /api/order`, then read `Orders` back with `factory.CreateDbContext()` and assert one row with the same `PaymentIntentId`. The in-memory tier cannot see whether a second row was written. `docs/Database.md`, section 4, has the pattern.
'@

Write-Host "2. Stories 9-11 as board items"
$i9 = New-BoardIssue -Epic 29 -Title "[CI] Run the SQL Server test tier in GitHub Actions" -Body @'
**Branch:** `ci/sqlserver-tier` · **Size:** M · **Label:** ci, testing

**Why**
`SqlApiTests` run on every developer machine with LocalDB but CI skips them
(`--filter "TestCategory!=SqlServer"`), because the Linux runners have no SQL Server.
GitHub Actions can start one as a service container.

**Done when**
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

_Course link: Module 12 · docs/Sprint3-Testing-Stories.md story 9 · docs/Database.md_
'@
$i10 = New-BoardIssue -Epic 27 -Title "[API] Widen the remaining decimal(5,2) money columns" -Body @'
**Branch:** `fix/money-precision` · **Size:** M · **Label:** bug, testing, design

**Why**
`Order.Subtotal` was widened to `decimal(18,2)` in the `AddressNames` migration because
orders over $999.99 failed to save. The other five money columns were left as they were:
`Product.Price`, `DeliveryMethod.Price`, `BasketItem.Price`, `OrderItem.Price` and
`CustomerBasket.ShippingPrice` are all `[Column(TypeName = "decimal (5,2)")]`, so no
product can cost $1,000 or more. Add a $1,200 guitar to `products.json` and the seeder
dies with a SQL arithmetic-overflow error; the in-memory tests never notice, and
`SeedDataTests.Prices_FitTheDecimal5_2Columns` only keeps the *shipped* data under the
limit. This is the first story that needs the SQL Server tier to prove the bug and the fix.

**Done when**
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

_Course link: Modules 4, 11 · docs/Sprint3-Testing-Stories.md story 10 · docs/Database.md_
'@
$i11 = New-BoardIssue -Epic 29 -Title "[API] One database-only test per developer (SQL Server tier)" -Body @'
**Branch:** `test/sql-tier-<topic>` · **Size:** S · **Label:** testing

**Why**
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

**Done when**
- [ ] The test reads the rows (`factory.CreateDbContext()`), not only the HTTP response,
      for at least one assertion.
- [ ] The PR description says in one sentence why the in-memory tier could not have
      caught this.

_Course link: Module 12 · docs/Sprint3-Testing-Stories.md story 11 · docs/Database.md_
'@
Add-ToEpic 29 @($i9, $i11)
Add-ToEpic 27 @($i10)

if ($IncludeStories1To8) {
    Write-Host "3. Testing stories 1-8 as board items (under #29)"
    $s1 = New-BoardIssue -Epic 29 -Title "[API] PageIndex below 1 produces a negative Skip" -Body @'
**Branch:** `test/spec-params-page-index` · **Size:** S · **Label:** bug, testing

**Why**
`ProductsWithTypesAndBrandsSpecification` computes `Skip = PageSize * (PageIndex - 1)`.
`GET /api/product?pageIndex=0` therefore asks for `Skip(-6)`. LINQ-to-Objects treats
that as 0; SQL Server's `OFFSET -6 ROWS` does not.

**Done when**
- [ ] A failing unit test first: `ProductSpecParams { PageIndex = 0 }` (and `-1`) must behave as page 1.
- [ ] Decide where the clamp lives (the `PageIndex` setter, like the `PageSize` cap) and implement it.
- [ ] Add the boundary trio for the cap while you are there: 49, 50, 51.
- [ ] `dotnet test WeShopAlot.Testing.Unit` green; `ApiTests` still green.

_Course link: Module 5 · docs/Sprint3-Testing-Stories.md story 1 · docs/Database.md_
'@
    $s2 = New-BoardIssue -Epic 29 -Title "[API] Secondary sort for equal prices" -Body @'
**Branch:** `test/spec-sort-ties` · **Size:** S · **Label:** testing, design

**Why**
`priceAsc` and `priceDesc` replace the name ordering entirely, so two products at
the same price come back in whatever order the database likes. The constructor also
calls `AddOrderBy(x => x.Name)` and then overwrites it, which reads like a bug.

**Done when**
- [ ] A test in `SpecificationEvaluatorTests` with two products at the same price that pins the order you want (name as the tie-breaker is the obvious choice).
- [ ] `BaseSpecification` grows a `ThenBy` (or the evaluator applies name after price) so the test passes.
- [ ] Remove the dead `AddOrderBy(x => x.Name)` call if it no longer does anything, and say so in the PR.

_Course link: Module 5 · docs/Sprint3-Testing-Stories.md story 2 · docs/Database.md_
'@
    $s3 = New-BoardIssue -Epic 29 -Title "[API] MappingProfiles passes AssertConfigurationIsValid" -Body @'
**Branch:** `test/mapping-config-valid` · **Size:** M · **Label:** testing

**Why**
`MappingProfilesTests` builds the configuration but never calls
`AssertConfigurationIsValid()`. Add that call as its own test and make it pass.
Expect it to fail first: destination members such as `Address.Id`, `Address.IsDeleted`
and `CustomerBasket.ClientSecret` have no source. Each one is either `Ignore()` with
a reason, or a real missing mapping.

**Done when**
- [ ] `Configuration_IsValid` test exists and is green.
- [ ] Every `Ignore()` added has a one-line comment saying why the member is not mapped.
- [ ] No behavior change to the API (`ApiTests` green).

_Course link: Module 3 · docs/Sprint3-Testing-Stories.md story 3 · docs/Database.md_
'@
    $s4 = New-BoardIssue -Epic 29 -Title "[API] TokenService unit tests" -Body @'
**Branch:** `test/token-service` · **Size:** S · **Label:** testing

**Why**
`TokenService.CreateToken` needs only an `IConfiguration` with `Token:Key` and
`Token:Issuer`. Build one in memory, create a token, parse it back with
`JwtSecurityTokenHandler`, and assert on what the API's authentication checks.

**Done when**
- [ ] Tests in `WeShopAlot.Testing.Unit/Services/TokenServiceTests.cs`: issuer matches configuration; name and email claims present; expiry about one day out; signed with HS512.
- [ ] Remove the empty `try { } catch { throw; }` around `WriteToken` (it does nothing) in the same PR, since the tests now cover the method.

_Course link: Module 10 · docs/Sprint3-Testing-Stories.md story 4 · docs/Database.md_
'@
    $s5 = New-BoardIssue -Epic 29 -Title "[API] Server-side price rule as a pure function" -Body @'
**Branch:** `test/order-subtotal-pure` · **Size:** M · **Label:** testing, refactor

**Why**
`Checkout_BasketToOrder_UsesServerPrices` proves over HTTP that an order uses catalog
prices, not the client's. The arithmetic lives inside `OrderService.CreateOrderAsync`
next to repository calls, so it can only be tested by booting the API.

**Done when**
- [ ] Extract the item-pricing and subtotal step into a pure static method (for example `OrderPricing.Price(basketItems, products)`), with no repository or DbContext parameter.
- [ ] Unit tests: client price ignored; quantity multiplies; subtotal sums; an item whose product is missing is a clear failure, not a silent zero.
- [ ] `OrderService` calls the new method; `ApiTests` still green (it is the safety net for this refactor).

_Course link: Module 11 · docs/Sprint3-Testing-Stories.md story 5 · docs/Database.md_
'@
    $s6 = New-BoardIssue -Epic 29 -Title "[Angular] calculateTotals as a pure function" -Body @'
**Branch:** `test/basket-totals-pure` · **Size:** S · **Label:** testing, client

**Why**
`BasketService.calculateTotals()` is private and reached only through a flushed HTTP
request in `basket.service.spec.ts`. The same move the lecture made with Bullpen's
risk guard applies: pull the arithmetic out.

**Done when**
- [ ] `src/app/shared/models/basket.ts` (or a new `basket-totals.ts`) exports `calculateTotals(basket: Basket): BasketTotals`.
- [ ] `basket-totals.spec.ts` covers: empty basket; one item × quantity; shipping added; two items summed. No `TestBed`, no `HttpTestingController`.
- [ ] `BasketService` calls it; existing specs unchanged and green (`npx ng test --watch=false`).

_Course link: Module 9 · docs/Sprint3-Testing-Stories.md story 6 · docs/Database.md_
'@
    $s7 = New-BoardIssue -Epic 29 -Title "[Chore] Retire the empty test projects" -Body @'
**Branch:** `chore/empty-test-projects` · **Size:** S · **Label:** housekeeping

**Why**
`WeShopAlot.Testing.Services`, `WeShopAlot.Testing.Repositories` (namespace
`OurGovRepositoryTests`) and `WeShopAlot.Testing.XSLT` each hold one `TestMethod1`
that asserts nothing. `WeShopAlot.Testing.Shared` carries `OurGov.*` namespaces and a
`Dispose()` that throws `NotImplementedException`; its `BaseIntegrationTestsClass` was
an early sketch of what `WeShopAlotSqlApiFactory` now does, so nothing is lost.

**Done when**
- [ ] Either delete the three projects and `Testing.Shared` from the solution, or give each a first real test — decide in the PR description, not silently.
- [ ] No `OurGov` namespace remains in the repo (`git grep OurGov` is empty).
- [ ] Solution builds; CI green.

_Course link: Module 12 · docs/Sprint3-Testing-Stories.md story 7 · docs/Database.md_
'@
    $s8 = New-BoardIssue -Epic 29 -Title "[CI] Test results and coverage visible on every PR" -Body @'
**Branch:** `ci/test-reports` · **Size:** M · **Label:** ci

**Why**
CI runs the suites but a reviewer has to open the log to see counts. Publish them.

**Done when**
- [ ] `dotnet test` runs with `--logger trx --collect:"XPlat Code Coverage"` for both .NET test projects and the results are uploaded as workflow artifacts.
- [ ] A test-summary step (for example `dorny/test-reporter` or `EnricoMi/publish-unit-test-result-action`) posts pass/fail counts on the PR.
- [ ] Angular: `ng test --watch=false --code-coverage` and the `coverage/` folder uploaded the same way.
- [ ] `docs/BuildGate.md` updated with where to find the reports.

_Course link: Module 12 · docs/Sprint3-Testing-Stories.md story 8 · docs/Database.md_
'@
    Add-ToEpic 29 @($s1, $s2, $s3, $s4, $s5, $s6, $s7, $s8)
}

Write-Host "Done. Board: https://github.com/orgs/MyWebSiteParticipants/projects/2"

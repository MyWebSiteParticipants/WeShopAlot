# The database: how it is built, rebuilt, and used in tests

WeShopAlot's database is never created by hand. Code describes it, and one command builds it from nothing, which is also what makes it a good foundation for integration tests: a test run can build its own copy, use it, and throw it away. This page explains the pieces, the one command, and the test tier built on it.

## 1. The pieces

| Layer | Where | What it is |
|---|---|---|
| **Schema** | `WeShopAlot.Data/Migrations` | EF Core migrations, generated from the entity classes and `WeShopAlotContext.OnModelCreating`. Every table lives in schema `WSA`. Two so far: `Initial-001` (2023) and `AddressNames` (2026). |
| **Reference data** | `WeShopAlot.Data/Seed/Seeding.cs` | The three `OrderStatus` rows (`Pending`, `Payment Received`, `Payment Failed`), declared with `HasData` from `OrderStatusEnum`. Because they are part of the model, the *migration* inserts them. |
| **Sample data** | `WeShopAlot.Data/Seed/Content/*.json` | The catalog: 7 product types, 10 brands, 4 delivery methods, 16 guitars. Plain JSON, embedded in the `WeShopAlot.Data` assembly. |
| **Seeder** | `WeShopAlot.Data/Seed/DatabaseSeeder.cs` | Two methods. `RecreateAsync` drops every `WSA` table and applies the migrations. `SeedAsync` loads the JSON into any table that is empty. |
| **The command** | `WeShopAlot.Data.ConsoleApp` | A console program that calls the two methods and exits. Nothing else is in it. |

Two different mechanisms seed data, and it matters which is which. `OrderStatus` arrives with the schema, so *any* database created from the model has it, including the in-memory one the fast tests use (`EnsureCreated` applies `HasData` too). The catalog arrives only when something calls `DatabaseSeeder.SeedAsync`: the console app, or a test factory.

## 2. Create or rebuild the database

**PowerShell**

```powershell
cd C:\projects\WeShopAlot\Solution
dotnet run --project WeShopAlot.Data.ConsoleApp
```

What happens, in order:

1. Reads `Settings:DbServerType` and `ConnectionStrings:WeShopAlotSQLConnection` from the console app's user secrets, then environment variables, then the command line (later sources win).
2. If the database can be reached, drops every foreign key and table in schema `WSA`, including EF Core's `__EFMigrationsHistory`. If the database does not exist yet, skips this and lets the next step create it.
3. Applies all migrations (`Migrate`). This creates the schema, the tables, and the `OrderStatus` rows.
4. Loads product types, brands, delivery methods, and products from the embedded JSON.
5. Prints the counts and exits with code 0; any failure prints the reason and exits with 1.

It takes a few seconds. The output ends with a line like `Done. 16 products, 3 order statuses.`

> **This is a wipe, not an update.** Every run starts the `WSA` schema over. Orders, users, and addresses in that database are gone. That is the point for a development machine ("give me a clean shop") and for a test run, and it is why the command must never be aimed at a database other people are using. The class server's database is rebuilt only by the person running it, on purpose.

Because of that, **resetting the database is the same command.** There is no separate reset step.

### Pointing it somewhere else for one run

The connection string in the secrets is the one the API uses. To rebuild a different database without changing the secrets, override the setting on the command line (everything after `--` goes to the program):

**PowerShell**

```powershell
cd C:\projects\WeShopAlot\Solution
dotnet run --project WeShopAlot.Data.ConsoleApp -- --ConnectionStrings:WeShopAlotSQLConnection "Server=(localdb)\MSSQLLocalDB;Database=WeShopAlot_Scratch;Trusted_Connection=true;TrustServerCertificate=true"
```

Or with an environment variable, which is how the class server is configured (see [Deployment.md](Deployment.md)): `ConnectionStrings__WeShopAlotSQLConnection` (two underscores for the colon) and `Settings__DbServerType`.

### PostgreSQL

Set `Settings:DbServerType` to `PostgresSql` and give an Npgsql connection string. The rebuild drops schema `"WSA"` with `CASCADE` and lets the first migration recreate it. The migrations were generated for SQL Server (`nvarchar`, identity columns), and Npgsql maps them, but SQL Server is what the course and the tests use.

## 3. Changing the schema or the data

**A new column or table.** Change the entity class, then add a migration. The console app is the startup project because it knows how to build the context from the secrets (`DesignTimeContextFactory`):

**PowerShell**

```powershell
cd C:\projects\WeShopAlot\Solution
dotnet tool install --global dotnet-ef        # once per machine
dotnet ef migrations add <Name> --project WeShopAlot.Data --startup-project WeShopAlot.Data.ConsoleApp
dotnet run --project WeShopAlot.Data.ConsoleApp
```

Commit the new files under `WeShopAlot.Data/Migrations`. The `SqlApiTests.Schema_EveryMigrationApplied_NonePending` test fails when the model and the migrations disagree, so a forgotten migration is caught before review.

**A new product.** Add it to `WeShopAlot.Data/Seed/Content/products.json` with the next `InternalId`, a brand and type that exist in the other two files, and `PictureUrl` of the form `images/products/<InternalId as 8 digits>-001.png`. Put the picture in `WeShopAlot..WebAPI/Content/images/products`. Run the seeder. `WeShopAlot.Testing.Unit/Seed/SeedDataTests` checks all of these rules against the JSON, with no database, so run `dotnet test WeShopAlot.Testing.Unit` first.

Prices are `decimal(5,2)`: at most 999.99 for a product, a delivery method, a basket item, and an order item (`Order.Subtotal` was widened to `decimal(18,2)` in the `AddressNames` migration). SQL Server rejects a larger value; the in-memory provider does not. Widening the rest is Sprint 3 story 10.

## 4. Integration tests on a rebuilt database

`WeShopAlot.Testing.WebApis` has two tiers. Both run the whole API (controllers, authentication, AutoMapper, middleware) in memory with `WebApplicationFactory` and send it real HTTP requests. They differ in the database.

| | Fast tier: `WeShopAlotApiFactory` + `ApiTests` | SQL Server tier: `WeShopAlotSqlApiFactory` + `SqlApiTests` |
|---|---|---|
| Database | EF Core in-memory provider, `EnsureCreated` | A real SQL Server database, created for this run by `DatabaseSeeder.RecreateAsync` |
| Data | A tiny hand-made catalog (3 products) in `SeedAsync` | The shipped catalog, loaded by `DatabaseSeeder.SeedAsync` |
| Needs | Nothing | SQL Server LocalDB (or any SQL Server; see below) |
| Catches | Routes, status codes, DTO shapes, auth, service logic | Everything the fast tier does, plus what only SQL enforces: migrations that match the model, foreign keys, column sizes, `ORDER BY` and `OFFSET` semantics, the real data |
| Time | A few seconds | Plus ~10 s to build and drop the database |
| In CI | Every pull request | Not yet (the runners have no SQL Server); see the Sprint 3 stories |

### How the SQL Server tier works

`WeShopAlotSqlApiFactory` does what you do on a new machine, once per test class:

1. Picks a database name of its own: `WeShopAlotTests_<date>_<time>_<8 random hex>`. Two test classes, or two developers on one server, never share a database.
2. Tells the API, through configuration, to use `SQLServer` and that connection string. The real `UseSelectedDatabaseServer` switch runs, as in production; nothing about the `DbContext` is replaced. Redis is replaced by the same in-memory stand-ins as the fast tier.
3. `TryRecreateAndSeedAsync()` calls `DatabaseSeeder.RecreateAsync` (migrations, `OrderStatus` rows) and `DatabaseSeeder.SeedAsync` (the catalog). This is the same code path as `dotnet run --project WeShopAlot.Data.ConsoleApp`.
4. Tests run. `factory.CreateDbContext()` gives a context on the same database for assertions that look past the HTTP layer at the rows themselves.
5. `DisposeAsync` drops the database.

If no SQL Server answers in step 3, `TryRecreateAndSeedAsync` returns `false` and every test in the class is reported **Inconclusive** (skipped), with the reason, rather than failed. A machine without LocalDB still gets a green run.

### Run it

**PowerShell**

```powershell
cd C:\projects\WeShopAlot\Solution
dotnet test WeShopAlot.Testing.WebApis                                   # both tiers
dotnet test WeShopAlot.Testing.WebApis --filter TestCategory=SqlServer   # SQL Server tier only
dotnet test WeShopAlot.Testing.WebApis --filter TestCategory!=SqlServer  # fast tier only (what CI runs)
```

By default the tier uses LocalDB (`Server=(localdb)\MSSQLLocalDB`), which the solution already requires. To use another server, SQL Server Express or a container for instance, set an environment variable to a connection string **without a database name**; the factory adds its own. The login must be allowed to create and drop databases.

**PowerShell**

```powershell
$env:WESHOPALOT_TEST_SQLSERVER = "Server=localhost\SQLEXPRESS;Trusted_Connection=true;TrustServerCertificate=true"
cd C:\projects\WeShopAlot\Solution
dotnet test WeShopAlot.Testing.WebApis --filter TestCategory=SqlServer
```

### Writing a test in this tier

Put it in `SqlApiTests` (or a new class with `[TestCategory("SqlServer")]` and the same `ClassInitialize` pattern). Three habits make these tests worth their extra seconds:

- **Assert on the real data.** `SeedData.Load()` gives you the JSON the database was seeded from, so a count or a name comes from the source, not from a number typed into the test. `Catalog_IsTheShippedOne` is the model.
- **Ask what only a database can answer.** `Checkout_WritesAnOrder_ThatSatisfiesTheForeignKeys` reads the `Order` row back and follows `StatusId` to the seeded `Pending` row. The in-memory provider would have accepted any `StatusId`; SQL Server would have refused the insert. That is the bug `OrderTests.NewOrder_StartsPending` in the unit project guards against, proved end to end.
- **Make each test self-contained.** Register your own user, look up product and delivery-method ids by name (identity values are not promised), and never rely on another test having run first. `RecreateAsync_ThenSeed_StartsFromAKnownState` rebuilds the database in the middle of the class; the others still pass in any order.

The rule behind `WeShopAlot.Testing.Unit` still applies: if a test needs no HTTP and no database, it belongs there. This tier is for the questions the other two cannot ask.

## 5. When something fails

| Symptom | Usual cause | Fix |
|---|---|---|
| Seeder: `No database server type was chosen` | The console app's own user secrets are not set (they are separate from the API's) | [Prerequisites.md](Prerequisites.md), step 3: set both values for `WeShopAlot.Data.ConsoleApp` |
| Seeder: `Cannot open database "WeShopAlot"` ... login failed | The login can reach the server but may not create databases | Have the database created for you (the class server), or use LocalDB, where you own everything |
| Seeder: `Invalid object name 'WSA.Product'` in the API afterwards | The seeder and the API have different connection strings, so a different database was rebuilt | `dotnet user-secrets list` in both projects; make them match (the checker warns about this) |
| Seeder: `products.json: product 'X' names brand 'Y', which is not in productBrands.json` | A JSON edit | `dotnet test WeShopAlot.Testing.Unit` tells you exactly which line |
| Tests: every `SqlApiTests` test is Inconclusive | No SQL Server reachable from the test | Install LocalDB (Prerequisites, step 1) or set `WESHOPALOT_TEST_SQLSERVER` |
| Tests: `Schema_EveryMigrationApplied_NonePending` fails with a pending migration | An entity changed and no migration was added | Section 3 |
| Tests: a `WeShopAlotTests_...` database is left on the server | The run was killed before `DisposeAsync` | Drop it by hand; it holds nothing of value, and its name says when it was made |
| Seeder on Linux or macOS fails to connect to `(localdb)` | LocalDB is Windows-only | Point the connection string at SQL Server in a container: `docker run -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=... -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest` |

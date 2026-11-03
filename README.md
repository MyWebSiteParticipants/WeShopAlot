# WeShopAlot

A complete e-commerce reference application: an **ASP.NET Core Web API (.NET 10)** with a product catalog, a Redis-backed basket, JWT sign-in, orders, and Stripe card payments, plus an **Angular 22** client that uses it. It is the reference app for the course **Angular and .NET Web APIs** — https://mikecostarella.github.io/CS_AngularAndDotNetAPI/ — where every module reads part of it, including the parts it gets wrong.

## What's in the solution (`Solution/`)

| Project | What it holds |
|---|---|
| `WeShopAlot..WebAPI` | Controllers, middleware, error types, the `[Cached]` attribute, Swagger, `Program.cs`; serves the built Angular app from `wwwroot` |
| `WeShopAlot.Data` | EF Core context, entities, migrations, repositories, query specifications; `Seed/` holds the sample catalog (JSON) and `DatabaseSeeder`, which rebuilds and seeds a database |
| `WeShopAlot.Infrastructure.Services` | Orders, Stripe payments, JWT tokens, Redis response cache |
| `WeShopAlot.Shared` | DTOs shared by the API and its .NET clients |
| `WeShopAlot.Data.ConsoleApp` | The command that rebuilds the database: runs `DatabaseSeeder` and exits. Also the startup project for `dotnet ef` |
| `Client` | The Angular client |
| `WeShopAlot.UI.ClientWPF` | A WPF desktop client of the same API (shop, basket, checkout with Stripe, orders) |
| `WeShopAlot.Testing.*` | Tests; `Testing.Unit` is pure functions, `Testing.WebApis` runs the whole API with WebApplicationFactory in two tiers: an in-memory database, and a real SQL Server database rebuilt per run by `DatabaseSeeder` |
| `Deployment`, `docker-compose.yml` | ARM templates for Azure; Redis for local development |

Outside `Solution/`: `scripts/` holds the prerequisite checker, and `docs/` holds the setup guide, the database guide, the deployment guide, and the build-gate guide. `.github/workflows/` and `.coderabbit.yaml` are the pull-request checks.

## Prerequisites

| Tool | Version | Used for |
|---|---|---|
| .NET SDK | 10.x | Every .NET project |
| Node.js (with npm) | `^22.22.3 \|\| ^24.15.0 \|\| >=26.0.0` (Angular 22's range) | The Angular client |
| SQL Server LocalDB, or any SQL Server | current | The database (PostgreSQL also works; see `Settings:DbServerType`) |
| Docker Desktop | current | Redis, for the basket and the response cache |
| HTTPS dev certificate, trusted | comes with the SDK | https://localhost:7244 and https://localhost:4200 |
| Stripe account in test mode | free | Card payments (no real money) |

Windows 10 or 11 is needed only for the WPF client. Git, Visual Studio 2026, and the Stripe CLI are optional.

**[docs/Prerequisites.md](docs/Prerequisites.md)** has the install commands, every setting, the one-time setup steps, and troubleshooting.

### Check your machine

A read-only script checks all of the above: tools and versions, user secrets (without printing them), the database and Redis connections, ports, and the setup steps still to do. For anything that isn't ready, it prints the command that fixes it.

PowerShell:

```powershell
cd C:\projects\WeShopAlot
.\scripts\Check-Prerequisites.ps1
```

Command Prompt (or double-click the file in File Explorer):

```cmd
cd C:\projects\WeShopAlot
scripts\check-prerequisites.cmd
```

Run it again after each fix until it says **Ready**. The exit code is 0 when ready, 2 when setup steps remain, and 1 when something fails.

## Secrets

No keys, passwords, or connection strings are committed. Set them with `dotnet user-secrets` in the API project (PowerShell):

```powershell
cd C:\projects\WeShopAlot\Solution\WeShopAlot..WebAPI
$bytes = New-Object byte[] 64; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
dotnet user-secrets set "Token:Key" ([Convert]::ToBase64String($bytes))   # HMAC-SHA512 needs 64+ bytes
dotnet user-secrets set "Settings:DbServerType" "SQLServer"
dotnet user-secrets set "ConnectionStrings:WeShopAlotSQLConnection" "Server=(localdb)\MSSQLLocalDB;Database=WeShopAlot;Trusted_Connection=true;TrustServerCertificate=true"
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379"
dotnet user-secrets set "StripeSettings:SecretKey" "sk_test_..."
dotnet user-secrets set "StripeSettings:WhSecret" "whsec_..."                # from "stripe listen"; optional
dotnet user-secrets set "AutoMapper:LicenseKey" "<optional; AutoMapper logs a warning without one>"
```

The database seeder (`WeShopAlot.Data.ConsoleApp`) has its own user secrets. Give it the same `Settings:DbServerType` and `ConnectionStrings:WeShopAlotSQLConnection`.

The Stripe publishable key (`pk_test_...`) is public by design and lives in `appsettings.json` and `Client/src/environments/`. It must come from the same Stripe account as your secret key, so if you use your own account, replace it there (and set `WESHOPALOT_STRIPE_PK` for the WPF client). Keys that appeared in this repository's history before October 2026 are retired; never reuse one.

## Run it

PowerShell:

```powershell
cd C:\projects\WeShopAlot\Solution
docker compose up -d                                   # Redis on 6379 (viewer on http://localhost:8081)
dotnet run --project WeShopAlot.Data.ConsoleApp        # build the database: drops the WSA schema, applies the migrations, loads the catalog, exits
cd Client
npm install
npx ng build                                           # builds into the API's wwwroot
cd ..
dotnet run --project "WeShopAlot..WebAPI" --launch-profile https
```

The seeder is also the reset: run it again for a clean shop. It wipes every table in the `WSA` schema of the database its secrets point at, so never aim it at a database other people use. **[docs/Database.md](docs/Database.md)** has the whole story: the migrations, the two kinds of seed data, overriding the connection string for one run, adding a migration, and the integration tests built on the same rebuild.

Browse to https://localhost:7244 for the app, or https://localhost:7244/swagger for the API. For client development, run `npm start` in `Client` (it exports the HTTPS certificate on first run) and browse to https://localhost:4200.

So that orders move from *Pending* to *Payment Received*, forward Stripe's webhooks while you test payments (PowerShell, in a separate window):

```powershell
stripe listen --forward-to https://localhost:7244/api/payment/webhook
```

## Deploy it

One shared copy for a class runs on a Windows server with IIS: build a package on your PC with `scripts\Publish-WeShopAlot.ps1`, copy it over, and the server only runs it. **[docs/Deployment.md](docs/Deployment.md)** has what to ask IT for, the install, the settings (environment variables, no secrets in files), the IIS site, seeding, updates, and troubleshooting.

## Test it

PowerShell:

```powershell
cd C:\projects\WeShopAlot\Solution
dotnet test                       # unit tests, in-memory API tests, and (with LocalDB) the SQL Server API tests
cd Client
npx ng test --watch=false         # Angular unit tests (Vitest)
```

Three .NET tiers: `WeShopAlot.Testing.Unit` (pure functions, no I/O), the in-memory `ApiTests` (the whole API, no database or Redis), and `SqlApiTests` (the whole API against a SQL Server database built for the run from the migrations and the shipped seed data, then dropped). The last tier is `TestCategory=SqlServer`; without a reachable SQL Server it reports Inconclusive rather than failing, and CI leaves it out. [docs/Database.md](docs/Database.md), section 4, explains how it works and how to write a test in it.

## Pull requests

Every pull request runs three checks on a clean Linux machine: **api** (build the Web API and run the unit and in-memory tests), **client** (build and test the Angular client) and **scan** (no `.env` files, no secrets), plus a CodeRabbit review. The class fork's `master` branch only merges green pull requests. **[docs/BuildGate.md](docs/BuildGate.md)** explains each check, what to do when one is red, and what to do if you pushed a secret (rotate it; deleting the file is not enough).

## History

Written in 2023–2024 on .NET 8 with an Angular 15 client; upgraded in October 2026 to .NET 10 and Angular 22. The tests added for the upgrade found several long-standing bugs (client routes that did not match the API, tokens without an issuer, broken address endpoints, a shipping rounding error); the course's WeShopAlot page describes each one.

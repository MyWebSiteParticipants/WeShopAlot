# What you need to run WeShopAlot

Everything a clean Windows machine needs to build and run the solution: the API, the Angular client, and the WPF desktop client. It covers what to install, which settings to supply, and the one-time setup steps, in that order. A script checks all of it for you.

## Check a machine

From the repository folder, run the checker. It is read-only: it installs nothing and changes no settings, and it never prints secret values.

**PowerShell**

```powershell
cd C:\projects\WeShopAlot
.\scripts\Check-Prerequisites.ps1
```

**Command Prompt** (or double-click `scripts\check-prerequisites.cmd` in File Explorer)

```cmd
cd C:\projects\WeShopAlot
scripts\check-prerequisites.cmd
```

Add `-Offline` to skip the internet checks. Each line is marked with a status:

| Status | Meaning |
|---|---|
| `OK` | Ready. |
| `TODO` | The machine is fine, but a one-time setup step is still to do (for example `npm install`, seeding the database, or starting Redis). |
| `WARN` | Optional, or works with a caveat. |
| `FAIL` | Missing or wrong. WeShopAlot will not run until it is fixed. |

Every line that isn't `OK` comes with the command that fixes it. The summary lists the `FAIL` items first, then the `TODO` steps in the order to do them. The exit code is `0` when the machine is ready, `2` when there are no failures but `TODO` steps remain, and `1` when anything fails, so the script can also gate a lab machine image or a CI job.

On a brand-new machine expect a page of `FAIL` and `TODO`. Work down the summary and run the script again until it says **Ready**.

## 1. Install the tools

The install commands use `winget`, which comes with Windows 10 and 11. Run them in **PowerShell**, then open a new terminal so the new `PATH` takes effect.

| Tool | Version | What it is for | Install |
|---|---|---|---|
| Windows | 10 or 11, 64-bit | The WPF client is Windows-only; the API and the Angular client also run on macOS and Linux | — |
| .NET SDK | 10.x | Builds and runs every .NET project (all target `net10.0`; the WPF client targets `net10.0-windows`). Includes the ASP.NET Core and Windows Desktop runtimes | `winget install Microsoft.DotNet.SDK.10` |
| Node.js (with npm) | `^22.22.3 \|\| ^24.15.0 \|\| >=26.0.0` | Builds and serves the Angular 22 client. The range is Angular's own requirement; the script reads it from `Client/package-lock.json`, so it stays right after upgrades | `winget install OpenJS.NodeJS.LTS` |
| SQL Server LocalDB | any current version | The database. LocalDB is a zero-configuration SQL Server that starts on demand | The Visual Studio Installer's **Data storage and processing** workload, or the SQL Server Express installer's **LocalDB** option. A full SQL Server Express also works: `winget install Microsoft.SQLServer.2025.Express` |
| Docker Desktop | current | Runs Redis (the basket store and response cache) from `Solution/docker-compose.yml` | `winget install Docker.DockerDesktop`, then start it once and accept the terms |
| HTTPS development certificate | — | Lets the browser trust https://localhost:7244 (the API) and https://localhost:4200 (the Angular dev server), which share one certificate | Comes with the .NET SDK. Trust it once: `dotnet dev-certs https --trust` |

**Optional**

| Tool | Why you might want it | Install |
|---|---|---|
| Git | Clone the repository and commit changes (a ZIP download works without it) | `winget install Git.Git` |
| Visual Studio 2026 | One-click debugging of the whole solution. Choose the **ASP.NET and web development** and **.NET desktop development** workloads. VS Code and the command line are enough otherwise | Visual Studio Installer |
| Stripe CLI | Forwards Stripe's payment webhooks to localhost. Without it, orders stay *Pending* after payment | `winget install Stripe.StripeCli` |

**Instead of LocalDB or Docker:** any SQL Server you can reach works; change the connection string to point at it. PostgreSQL also works: set `Settings:DbServerType` to `PostgresSql` and give a Npgsql connection string. Any Redis 6 or later works too, on another machine or in WSL; change `ConnectionStrings:Redis`.

**Internet access** is needed for `dotnet restore` (api.nuget.org), `npm install` (registry.npmjs.org), and payments (api.stripe.com).

## 2. Get Stripe test keys

Payments run against Stripe in **test mode**: no real cards and no real money. Create a free account at https://dashboard.stripe.com, stay in test mode, and open **Developers → API keys**. You need both keys, and they must come from the same account:

- **Secret key** (`sk_test_…`): goes in user secrets (next step). Never commit it.
- **Publishable key** (`pk_test_…`): public by design, and it ships to every browser. The repository has the course's key in `Solution/Client/src/environments/environment.ts` and `environment.prod.ts`, and in `Solution/WeShopAlot..WebAPI/appsettings.json` (`StripeSettings:PublishableKey`). If you use your own Stripe account, replace it in all three places. For the WPF client, set the `WESHOPALOT_STRIPE_PK` environment variable. When the two keys come from different accounts, the payment step fails, and the checker warns about it.

Pay with Stripe's test card `4242 4242 4242 4242`, any future expiry date, and any CVC.

## 3. Set the secrets

No keys, passwords, or connection strings are committed. Each project keeps its own [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets), stored outside the repository in `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`.

**API** (`WeShopAlot..WebAPI`)

| Key | Required | Example | Notes |
|---|---|---|---|
| `Settings:DbServerType` | yes | `SQLServer` | `SQLServer` or `PostgresSql`, spelled exactly |
| `ConnectionStrings:WeShopAlotSQLConnection` | yes | `Server=(localdb)\MSSQLLocalDB;Database=WeShopAlot;Trusted_Connection=true;TrustServerCertificate=true` | Used for PostgreSQL too |
| `ConnectionStrings:Redis` | yes | `localhost:6379` | The basket and the cached product list fail without it |
| `Token:Key` | yes | 64+ random characters | Signs the JWTs with HMAC-SHA512, which needs at least 64 bytes; a shorter key makes sign-in fail with `IDX10720` |
| `StripeSettings:SecretKey` | yes | `sk_test_…` | Never a `sk_live_` key on a development machine |
| `StripeSettings:WhSecret` | for webhooks | `whsec_…` | Printed by `stripe listen` (step 4) |
| `AutoMapper:LicenseKey` | no | — | AutoMapper logs a license warning without it, but it works |

**PowerShell**: generate a token key and set everything:

```powershell
cd C:\projects\WeShopAlot\Solution\WeShopAlot..WebAPI
$bytes = New-Object byte[] 64; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
dotnet user-secrets set "Token:Key" ([Convert]::ToBase64String($bytes))
dotnet user-secrets set "Settings:DbServerType" "SQLServer"
dotnet user-secrets set "ConnectionStrings:WeShopAlotSQLConnection" "Server=(localdb)\MSSQLLocalDB;Database=WeShopAlot;Trusted_Connection=true;TrustServerCertificate=true"
dotnet user-secrets set "ConnectionStrings:Redis" "localhost:6379"
dotnet user-secrets set "StripeSettings:SecretKey" "sk_test_..."
```

**Database seeder** (`WeShopAlot.Data.ConsoleApp`) has its own secrets. It needs the same two database values, and the same connection string, so it seeds the database the API uses:

```powershell
cd C:\projects\WeShopAlot\Solution\WeShopAlot.Data.ConsoleApp
dotnet user-secrets set "Settings:DbServerType" "SQLServer"
dotnet user-secrets set "ConnectionStrings:WeShopAlotSQLConnection" "Server=(localdb)\MSSQLLocalDB;Database=WeShopAlot;Trusted_Connection=true;TrustServerCertificate=true"
```

`dotnet user-secrets list` shows what a project has.

## 4. One-time setup

Run these in **PowerShell**:

```powershell
# Redis (and Redis Commander on http://localhost:8081); Docker Desktop must be running
cd C:\projects\WeShopAlot\Solution
docker compose up -d

# Create the database, apply the migrations, and load the seed data (a few seconds; it exits when done).
# Running it again later rebuilds the database from scratch: see ../docs/Database.md
cd C:\projects\WeShopAlot\Solution
dotnet run --project WeShopAlot.Data.ConsoleApp

# Angular packages
cd C:\projects\WeShopAlot\Solution\Client
npm install
```

The first `npm start` also exports the HTTPS certificate to `Client\ssl` for the Angular dev server.

For payments to move orders from *Pending* to *Payment Received*, forward Stripe's webhooks in a separate PowerShell window and leave it running. Put the `whsec_…` secret it prints into the API's secrets:

```powershell
stripe login
stripe listen --forward-to https://localhost:7244/api/payment/webhook
```

```powershell
cd C:\projects\WeShopAlot\Solution\WeShopAlot..WebAPI
dotnet user-secrets set "StripeSettings:WhSecret" "whsec_..."
```

Run the checker again. When it says **Ready**, follow **Run it** in the [README](../README.md).

## Ports

| Port | What listens |
|---|---|
| 7244 | The API over HTTPS: Swagger at `/swagger`, and the built Angular app at `/` |
| 5094 | The API over HTTP (the `http` launch profile) |
| 4200 | The Angular dev server over HTTPS (`npm start`) |
| 6379 | Redis (Docker) |
| 8081 | Redis Commander, a browser view of Redis (Docker) |

## When something fails

| Symptom | Usual cause | Fix |
|---|---|---|
| API requests return 500 and the log says "No database server type was chosen" | `Settings:DbServerType` is not set | Set it in the API's user secrets |
| The shop or basket returns 500 with a `RedisConnectionException` in the log | Redis is not running | `docker compose up -d` in `Solution` |
| Sign-in returns 500 with `IDX10720` in the log | `Token:Key` is shorter than 64 bytes | Generate a longer one (step 3) |
| The browser shows "Your connection is not private" on localhost | The dev certificate isn't trusted | `dotnet dev-certs https --trust`, then restart the browser |
| The shop page is empty, or the API logs "Invalid object name 'WSA.Product'" | The database was never seeded, or the seeder used a different connection string | Run the seeder; check that both projects have the same connection string ([Database.md](Database.md)) |
| `dotnet test` reports the `SqlApiTests` as Inconclusive (skipped) | No SQL Server reachable: LocalDB is missing, or you are on macOS/Linux | Install LocalDB, or point `WESHOPALOT_TEST_SQLSERVER` at a SQL Server ([Database.md](Database.md), section 4) |
| Go to Payment fails | No `StripeSettings:SecretKey`, or it is from a different account than the publishable key | Step 2 |
| Orders stay *Pending* after payment | No webhook forwarding | `stripe listen` and `StripeSettings:WhSecret` (step 4) |
| `npm install` or `ng` refuses the Node version | Node.js is outside Angular's range | `winget upgrade OpenJS.NodeJS.LTS` |
| Running a `.ps1` from Command Prompt opens it in Notepad | Command Prompt can't run PowerShell scripts | Use `scripts\check-prerequisites.cmd`, or run the `.ps1` from PowerShell |

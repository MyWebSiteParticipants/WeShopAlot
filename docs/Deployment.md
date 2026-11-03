# Deploying WeShopAlot to a server

How to put one shared copy of WeShopAlot on a Windows server with IIS, so a whole class uses the same site, API and database. [Prerequisites.md](Prerequisites.md) is for a machine someone develops on; this guide is for a machine nobody develops on. Nothing is built on the server: you build a package on your own PC, copy it over, and the server only runs it.

## How it runs on a server

One IIS site does everything. The Web API serves the built Angular app from its `wwwroot` folder (`FallbackController` returns `index.html` for any route the API does not handle), the product images from `/Content`, Swagger at `/swagger`, and the API itself under `/api`. Because the browser loads the app and calls the API from the same address, CORS never comes into it.

| Piece | Where it runs | Talks to |
|---|---|---|
| Angular app | The user's browser, loaded from `https://<host>/` | `https://<host>/api/` (the relative `api/` in `environment.prod.ts`) |
| Web API (`WeShopAlot..WebAPI`) | IIS, in an application pool, through the ASP.NET Core Module | SQL Server, Redis, api.stripe.com |
| SQL Server | The same server (Express), or a database on a shared instance | — |
| Redis | The same server, as a Windows service | — |
| Seeder (`WeShopAlot.Data.ConsoleApp`) | Run once by hand on the server | SQL Server |

Settings come from **machine environment variables**, not from files. The API and the seeder both read them, so one set of values serves both, and no secret is ever written into the site folder or the repository.

## What to ask for

When you request the server, give IT this list. It saves the round of questions they would otherwise send back.

| Item | Ask for |
|---|---|
| Operating system | Windows Server 2022 or 2025 with the **Web Server (IIS)** role and the IIS Management Console |
| Size | 2 vCPU, 8 GB RAM, 60 GB disk is plenty for a class; it is a demo site, not a store |
| Name and certificate | A hostname (for example `weshopalot.example.edu`) with a DNS entry and a TLS certificate for it, installed in the server's certificate store. The API issues sign-in tokens with this name in them, so it should not change later |
| Network | Port 443 open to wherever the students are (campus, VPN, or everywhere). Port 80 only if you want it redirected to 443. Outbound HTTPS to `api.stripe.com` for payments. Outbound internet for the hour you install things |
| Access | Remote Desktop with local administrator rights for the instructor and, if a student is going to help run it, that student |
| Database | SQL Server Express on the same machine is simplest. If IT prefers a shared SQL Server, ask for a database named `WeShopAlot` and a SQL login that owns it |
| Patching and backups | Who installs Windows updates, and whether the VM is backed up. For a class site, no backups is an acceptable answer: the seeder rebuilds it in a minute |

If students should reach Stripe payments end to end, the server must be reachable **from the internet** (Stripe calls `https://<host>/api/payment/webhook` to confirm a payment). On a campus-only server, payments still go through, but orders stay *Pending*. That is fine for teaching the API; just say so in class.

## 1. Install on the server

Remote Desktop into the server and run these in **PowerShell as Administrator**. Install IIS first and the Hosting Bundle second; the bundle registers itself with whatever IIS it finds.

| What | Why | Install |
|---|---|---|
| IIS with the Management Console | Hosts the site | `Install-WindowsFeature Web-Server, Web-Mgmt-Console` (skip if IT already added the role) |
| ASP.NET Core Hosting Bundle 10 | The .NET and ASP.NET Core runtimes plus the IIS module that runs the API. No SDK is needed; nothing is compiled on the server | `winget install Microsoft.DotNet.HostingBundle.10`, or download **Hosting Bundle** from https://dotnet.microsoft.com/download/dotnet/10.0. Then run `iisreset` |
| SQL Server Express | The database | `winget install Microsoft.SQLServer.2025.Express`, or the installer from Microsoft. Choose **Mixed Mode** authentication when asked; the site signs in with a SQL login |
| Redis | The basket and the product cache; the shop returns 500 without it | Any Redis 6+ that runs as a Windows service. The usual choice is [Memurai](https://www.memurai.com) (Redis-compatible, installs as a service on port 6379; the Developer edition is free). Alternatives: Redis inside WSL, or a small Linux VM from IT with `ConnectionStrings__Redis` pointed at it |

`winget` may be missing on Windows Server; if so, use the download links. `sqlcmd` comes with SQL Server Express (or `winget install Microsoft.Sqlcmd`).

**The Windows firewall** on the server needs 443 open. IIS usually adds the rule when the Web Server role is installed; check with `Get-NetFirewallRule -DisplayName "World Wide Web Services (HTTPS Traffic-In)"`.

## 2. Create the database and its login

**PowerShell**, on the server. Replace the password with one of your own (and keep it; step 3 needs it).

```powershell
sqlcmd -S localhost\SQLEXPRESS -E -Q "CREATE DATABASE WeShopAlot;"
sqlcmd -S localhost\SQLEXPRESS -E -Q "CREATE LOGIN weshopalot WITH PASSWORD = 'ChangeMe-LongPassword-1', CHECK_POLICY = ON;"
sqlcmd -S localhost\SQLEXPRESS -E -d WeShopAlot -Q "CREATE USER weshopalot FOR LOGIN weshopalot; ALTER ROLE db_owner ADD MEMBER weshopalot;"
```

`db_owner` is deliberate: the seeder creates the `WSA` schema and all the tables, and the migrations alter them. If SQL Server lives on another machine, IT creates the database and login, and you skip this step.

If the second command fails with *"Only Windows authentication is enabled"*, Express was installed without Mixed Mode. Turn it on:

```powershell
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL*.SQLEXPRESS\MSSQLServer" -Name LoginMode -Value 2
Restart-Service 'MSSQL$SQLEXPRESS'
```

## 3. Set the settings

Every setting the API reads from user secrets on a development machine is read from an **environment variable** on the server, with `__` (two underscores) in place of `:`. The seeder reads the same variables. Set them at machine level so the IIS application pool and your own PowerShell window both see them.

| Setting | Variable | Value on the server | Notes |
|---|---|---|---|
| `Settings:DbServerType` | `Settings__DbServerType` | `SQLServer` | Spelled exactly |
| `ConnectionStrings:WeShopAlotSQLConnection` | `ConnectionStrings__WeShopAlotSQLConnection` | `Server=localhost\SQLEXPRESS;Database=WeShopAlot;User Id=weshopalot;Password=...;TrustServerCertificate=true` | `TrustServerCertificate` because Express uses a self-signed certificate |
| `ConnectionStrings:Redis` | `ConnectionStrings__Redis` | `localhost:6379` | Or the address of the Redis IT gave you |
| `Token:Key` | `Token__Key` | 64+ random characters | Signs the JWTs. Generate one as below; a shorter key makes sign-in fail with `IDX10720` |
| `Token:Issuer` | `Token__Issuer` | `https://weshopalot.example.edu` | The site's own address. **Required**: on a development machine it is in `appsettings.Development.json`, which a server never loads. Without it, every sign-in token is rejected |
| `ApiUrl` | `ApiUrl` | `https://weshopalot.example.edu/Content/` | Prefix for the product picture URLs the API returns, with the trailing slash. Same story: only `appsettings.Development.json` has it, so without it the shop shows no images |
| `StripeSettings:SecretKey` | `StripeSettings__SecretKey` | `sk_test_...` | Test mode only. It must be from the same Stripe account as the publishable key built into the client (see [Prerequisites.md](Prerequisites.md), step 2) |
| `StripeSettings:WhSecret` | `StripeSettings__WhSecret` | `whsec_...` | Only if you register a webhook endpoint at Stripe (step 7) |
| `AutoMapper:LicenseKey` | `AutoMapper__LicenseKey` | — | Optional; without it AutoMapper logs a license warning and works |

**PowerShell as Administrator**, on the server. Put your own hostname, password and Stripe key in:

```powershell
$bytes = New-Object byte[] 64; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
[Environment]::SetEnvironmentVariable('Token__Key', [Convert]::ToBase64String($bytes), 'Machine')
[Environment]::SetEnvironmentVariable('Token__Issuer', 'https://weshopalot.example.edu', 'Machine')
[Environment]::SetEnvironmentVariable('ApiUrl', 'https://weshopalot.example.edu/Content/', 'Machine')
[Environment]::SetEnvironmentVariable('Settings__DbServerType', 'SQLServer', 'Machine')
[Environment]::SetEnvironmentVariable('ConnectionStrings__WeShopAlotSQLConnection', 'Server=localhost\SQLEXPRESS;Database=WeShopAlot;User Id=weshopalot;Password=ChangeMe-LongPassword-1;TrustServerCertificate=true', 'Machine')
[Environment]::SetEnvironmentVariable('ConnectionStrings__Redis', 'localhost:6379', 'Machine')
[Environment]::SetEnvironmentVariable('StripeSettings__SecretKey', 'sk_test_...', 'Machine')
```

Machine variables reach a process only when it starts, so after changing any of them: open a **new** PowerShell window for yourself, and run `iisreset` for the site. Anyone who is an administrator on the server can read these values; on a class server that is the same set of people who could read the site folder anyway. If IT wants them hidden from other administrators, set them on the application pool instead (IIS Manager → the site → **Configuration Editor** → `system.webServer/aspNetCore` → `environmentVariables`), which stores them in the site's `web.config`.

Leave `ASPNETCORE_ENVIRONMENT` unset. The default is `Production`, which is what you want: `appsettings.Development.json` (with its `localhost` values) is then ignored, and the error pages hide stack traces.

## 4. Build the package on your PC

The package is built where the tools are: your development machine, with the same prerequisites as for running the solution. The script builds the Angular app into the API's `wwwroot`, publishes the API and the seeder, adds the product images, and zips the lot.

**PowerShell**

```powershell
cd C:\projects\WeShopAlot
.\scripts\Publish-WeShopAlot.ps1
```

**Command Prompt**

```cmd
cd C:\projects\WeShopAlot
scripts\publish-weshopalot.cmd
```

It writes `publish\WeShopAlot-<date>-<time>.zip` (the `publish` folder is ignored by git). Inside:

| Folder | Contents | Goes to |
|---|---|---|
| `Site\` | The API, its `web.config`, the Angular app in `wwwroot\`, the product images in `Content\` | The IIS site folder |
| `Seeder\` | `WeShopAlot.Data.ConsoleApp.exe` and its seed data | Anywhere on the server; it is run once and can then be deleted |

`-CleanInstall` runs `npm ci` first (a fresh `node_modules`, as the build gate does); by default the script installs packages only when `node_modules` is missing. `-NoZip` leaves the folders unzipped. `-OutputPath` changes where it writes.

Copy the zip to the server: through the Remote Desktop clipboard (copy the file on your PC, paste it in Explorer on the server), a mapped drive in the RDP session, or whatever file share IT gives you.

## 5. Create the IIS site

On the server, **PowerShell as Administrator**. This creates the folder, the application pool and the site, and binds the certificate. Replace the hostname, and use the certificate's thumbprint from the server's certificate store (`Get-ChildItem Cert:\LocalMachine\My` lists them).

```powershell
Import-Module WebAdministration
New-Item -ItemType Directory -Path C:\inetpub\WeShopAlot -Force
Expand-Archive -Path "$env:USERPROFILE\Desktop\WeShopAlot-*.zip" -DestinationPath C:\inetpub\WeShopAlot -Force

New-WebAppPool -Name WeShopAlot
Set-ItemProperty IIS:\AppPools\WeShopAlot -Name managedRuntimeVersion -Value ''      # "No Managed Code": the Hosting Bundle runs the API, not the .NET Framework
New-Website -Name WeShopAlot -PhysicalPath C:\inetpub\WeShopAlot\Site -ApplicationPool WeShopAlot -Port 443 -HostHeader weshopalot.example.edu -Ssl
$cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object Subject -like '*weshopalot.example.edu*' | Select-Object -First 1
New-Item -Path "IIS:\SslBindings\!443!weshopalot.example.edu" -Value $cert -SSLFlags 1

icacls C:\inetpub\WeShopAlot\Site /grant "IIS AppPool\WeShopAlot:(OI)(CI)RX" /T
Start-Website WeShopAlot
```

The same can be done by clicking in IIS Manager: **Application Pools → Add** (name `WeShopAlot`, .NET CLR version **No Managed Code**), then **Sites → Add Website** (physical path `C:\inetpub\WeShopAlot\Site`, binding `https`, the hostname, the certificate). If the server also has the IIS **Default Web Site** on port 443, stop it or give WeShopAlot a different hostname binding.

`web.config` is in the package; `dotnet publish` generated it, and it tells IIS to run the API in-process. If a request returns **500.30** or **500.31**, the Hosting Bundle is missing or was installed before IIS; reinstall it (or run its installer's *Repair*) and `iisreset`.

## 6. Seed the database

Run the seeder **once**, on the server, from a PowerShell window opened after step 3 (so it sees the variables).

> **The seeder drops every table in the `WSA` schema before it creates them.** On a server that has been in use, that means every account, basket and order is gone. Run it to create the database the first time, or when you deliberately want a fresh start, and at no other time. It prints what it did and exits; [Database.md](Database.md) describes each step.

```powershell
cd C:\inetpub\WeShopAlot\Seeder
.\WeShopAlot.Data.ConsoleApp.exe
```

It drops the `WSA` schema if there is one, applies the migrations, loads the product types, brands, delivery methods and products from the seed data embedded in `WeShopAlot.Data.dll`, prints the counts, and exits with code 0 (1 on failure, with the reason). On a brand-new database there is nothing to drop and the step is skipped.

## 7. Verify

Work down this list in a browser; each line checks a different piece.

| Open | Should show | Checks |
|---|---|---|
| `https://<host>/swagger` | The API's endpoints | IIS, the Hosting Bundle, the certificate |
| `https://<host>/` | The shop with products **and pictures** | The Angular build, the database, `ApiUrl`, the `Content` folder |
| Register, then sign in | A signed-in header | `Token:Key` and `Token:Issuer` |
| Add a product to the basket, reload | The basket survives the reload | Redis |
| Checkout, pay with `4242 4242 4242 4242` | Order created | `StripeSettings:SecretKey` and outbound access to api.stripe.com |
| The order's status, after a minute | *Payment Received* | The Stripe webhook (below); without it the order stays *Pending* |

For the webhook, the server must be reachable from the internet. In the Stripe dashboard (test mode) add an endpoint `https://<host>/api/payment/webhook` for the `payment_intent.succeeded` and `payment_intent.payment_failed` events, copy its signing secret into `StripeSettings__WhSecret` (step 3), and `iisreset`.

The API's log goes to the Windows Event Log (**Application**, source *IIS AspNetCore Module V2*) when the process fails to start, and otherwise nowhere unless you turn stdout logging on: in `Site\web.config`, set `stdoutLogEnabled="true"`, create `Site\logs`, give `IIS AppPool\WeShopAlot` write access to it, and read `logs\stdout_*.log`. Turn it off again when done.

## Updating the site

Build a new package (step 4), copy it over, and swap the site folder. Dropping a file named `app_offline.htm` into the site folder stops the API and releases its files; removing it starts the API again.

```powershell
Expand-Archive -Path "$env:USERPROFILE\Desktop\WeShopAlot-*.zip" -DestinationPath "$env:TEMP\WeShopAlot-new" -Force
Set-Content C:\inetpub\WeShopAlot\Site\app_offline.htm "WeShopAlot is being updated. Back in a minute."
Remove-Item C:\inetpub\WeShopAlot\Site\* -Recurse -Force -Exclude app_offline.htm
Copy-Item "$env:TEMP\WeShopAlot-new\Site\*" C:\inetpub\WeShopAlot\Site -Recurse -Force
Remove-Item C:\inetpub\WeShopAlot\Site\app_offline.htm
```

Settings live in environment variables, so a new package never overwrites them. If the application pool was given its own variables through `web.config` (the alternative in step 3), copy that `web.config` back after the swap.

**When a pull request added a migration**, the database needs it too, and the seeder is not the tool (it would wipe the data). Generate a SQL script on your PC that applies only the migrations the database does not have yet, and run it on the server:

```powershell
cd C:\projects\WeShopAlot\Solution
dotnet ef migrations script --idempotent --project WeShopAlot.Data --startup-project WeShopAlot.Data.ConsoleApp --output ..\publish\migrate.sql
```

```powershell
sqlcmd -S localhost\SQLEXPRESS -d WeShopAlot -U weshopalot -P "..." -i migrate.sql
```

(`dotnet ef` is the EF Core tool: `dotnet tool install --global dotnet-ef` once if you do not have it.)

## Students and the shared server

- **The site**: `https://<host>/` is the class's running copy. Swagger at `/swagger` is the fastest way to see what the API does before writing a client for it.
- **Angular development against the shared API**: in `Solution/Client/src/environments/environment.ts`, set `apiUrl` to `https://<host>/api/` and run `npm start` as usual. The API's CORS policy already allows `https://localhost:4200`, so the dev server can call it. No SQL Server, Redis or API secrets are needed on the student's machine for client-only work. Do not commit that change; the pull request's `environment.ts` keeps `localhost:7244`.
- **The WPF client**: point its API URL at `https://<host>/api/` the same way (see `App.xaml.cs`).
- **Accounts**: everyone registers their own; there is no admin role. The seeder is the only reset, and only the instructor runs it.

## When something fails

| Symptom | Usual cause | Fix |
|---|---|---|
| **500.30** or **500.31** on every page | Hosting Bundle missing, or installed before IIS | Install or repair the bundle, then `iisreset` |
| **500.19** | `web.config` can't be read, or the ASP.NET Core Module isn't registered | Same as above; also check `icacls` from step 5 |
| Swagger works, the shop list is empty or 500 | Database not seeded, or the connection string is wrong | Check the variables (step 3), then the seeder (step 6). The stdout log names the missing setting: "No database server type was chosen" or "No SQL server ... connection string" |
| The shop shows products without pictures | `ApiUrl` not set, or `Content\` missing from the site folder | Step 3 and step 4 |
| Registering works, signing in fails | `Token__Key` shorter than 64 bytes (`IDX10720`) or `Token__Issuer` missing | Step 3, then `iisreset` |
| Basket returns 500 (`RedisConnectionException`) | Redis isn't running, or `ConnectionStrings__Redis` is wrong | `Get-Service` for the Redis/Memurai service; step 3 |
| Everything works after setting a variable, but not in IIS | The application pool started before the variable existed | `iisreset` |
| Orders stay *Pending* | No webhook endpoint, or the server isn't reachable from the internet | Step 7, or accept it for a campus-only server |
| `Expand-Archive` says the file is in use, or a copy fails during an update | The API has the files open | `app_offline.htm` first (Updating the site) |
| "Login failed for user 'weshopalot'" | Mixed Mode is off, or the password in the connection string differs | Step 2 |

## The `Solution/Deployment` folder

It holds Azure Resource Manager templates and scripts from the project's original (2023) Azure App Service deployment: App Service plans, Key Vault, a storage account and a function app. Nothing in this guide uses them, and they are not maintained. They stay as an example of infrastructure-as-code; do not run them expecting a working site.

# Infra Health Check Windows Service

A .NET 8 Windows Service that, every 10 minutes:

1. **SQL Server** — opens a connection using your connection string and runs `SELECT 1`.
2. **IIS sites** — GETs each configured URL and expects HTTP 200. If it doesn't get 200,
   it recycles the site's application pool (via `appcmd.exe`) and checks once more.
3. **Windows services** — checks that each named service is `Running`. If it's `Stopped`
   (or `Paused`), it tries to start/continue it.

For **each individual check**, if it still fails after the self-heal attempt above, the whole
check is retried up to **5 times, 1 minute apart**. If it's still failing after the 5th attempt,
an **SMS is sent to the configured admin phone number(s)** describing the failure. All checks
run independently and concurrently, so one stuck check doesn't delay the others.

## Project layout

```
HealthCheckService.csproj
Program.cs                          # host/DI setup, Windows Service registration
Worker.cs                           # main loop: runs every N minutes, builds checkers from config
appsettings.json                    # ALL configuration lives here (see below)
Settings/AppOptions.cs              # strongly-typed config classes
Models/HealthCheckResult.cs
Services/
  IHealthChecker.cs                 # common interface
  SqlServerHealthChecker.cs         # check #1
  IisSiteHealthChecker.cs           # check #2 (+ app pool recycle)
  WindowsServiceHealthChecker.cs    # check #3 (+ service start)
  SmsNotificationService.cs         # sends the alert (Melipayamak "simple send" API)
  RetryingHealthCheckRunner.cs      # generic 5x/1-min retry + alert wrapper
```

## Configuration — `appsettings.json`

Everything is data-driven; add/remove sites or services without touching code.

```jsonc
{
  "HealthCheck": {
    "IntervalMinutes": 10,     // how often the full cycle runs
    "MaxRetries": 5,           // attempts before giving up and texting the admin
    "RetryDelayMinutes": 1     // wait between retries
  },
  "SqlServer": {
    "Name": "MainDatabase",
    "ConnectionString": "Server=...;Database=...;User Id=...;Password=...;TrustServerCertificate=True;Connection Timeout=10"
  },
  "IisSites": [
    { "Name": "PublicWebsite", "Url": "https://www.example.com/", "AppPoolName": "PublicWebsitePool", "TimeoutSeconds": 15 }
  ],
  "WindowsServices": [
    { "Name": "MyBackgroundWorkerService" }
  ],
  "Sms": {
    "BaseUrl": "https://console.melipayamak.com/",
    "ApiKey": "YOUR_MELIPAYAMAK_API_KEY",
    "ProviderPhone": "50002710XXXXX",
    "AdminPhones": [ "0912XXXXXXX" ]
  }
}
```

> The SMS integration follows the exact request shape from your reference
> `SmsService.cs` (Melipayamak "simple send" API: `POST api/send/simple/{ApiKey}` with
> `{ from, to, text }`). If you use a different SMS provider, only
> `Services/SmsNotificationService.cs` needs to change — nothing else depends on it.

### Protecting the connection string / API key

Don't commit real secrets into `appsettings.json`. On the server, either:
- Use **Windows environment variables** (config keys become `SqlServer__ConnectionString`, `Sms__ApiKey`, etc. — double underscore is the standard ASP.NET Core convention), or
- Use `dotnet user-secrets` during development, or
- Encrypt the file at rest / restrict its ACLs to the service account only.

## Build & publish

On a machine with the .NET 8 SDK:

```powershell
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o C:\Services\HealthCheckService
```

This produces a single `HealthCheckService.exe` (plus `appsettings.json`) in the output folder.

## Install as a Windows Service

Run PowerShell **as Administrator**:

```powershell
New-Service -Name "InfraHealthCheckService" `
  -BinaryPathName "C:\Services\HealthCheckService\HealthCheckService.exe" `
  -DisplayName "Infra Health Check Service" `
  -Description "Monitors SQL Server, IIS sites and Windows services; self-heals and SMS-alerts on failure." `
  -StartupType Automatic

# Create the Event Log source used for logging (one-time, also as Admin):
New-EventLog -LogName Application -Source "InfraHealthCheckService"

Start-Service -Name "InfraHealthCheckService"
```

To uninstall: `Stop-Service InfraHealthCheckService; Remove-Service InfraHealthCheckService` (or `sc.exe delete InfraHealthCheckService` on older Windows).

## Required permissions — important

The account the service runs as (by default `LocalSystem`, or a dedicated service account
if you configure one via `-Credential` on `New-Service`) needs:

- **SQL Server**: whatever login is in the connection string needs `db_datareader`/connect
  rights (or just enough to run `SELECT 1`).
- **IIS app pool recycle**: rights to run `%windir%\System32\inetsrv\appcmd.exe`. Running as
  `LocalSystem` on the same box as IIS works out of the box; if using a limited service
  account, add it to the local **IIS_IUSRS** / **Administrators** group, or grant it explicit
  rights via `icacls`/`appcmd` ACLs.
- **Windows service start/stop**: rights to control the target services
  (`sc.exe sdset <service> ...` or run as `LocalSystem`/an account in `Administrators`).
- **Outbound HTTPS**: to reach both the IIS sites being checked and the SMS provider's API
  (make sure the Windows Firewall / any proxy allows outbound 443 from this box).

Running as `LocalSystem` satisfies all of the above by default on a single-server setup;
use a dedicated least-privilege account only if you need tighter separation.

## Notes on the retry/self-heal behavior

- **SQL Server**: there's no "fix" action to take from this service's side — a failed
  connection just gets retried on the next attempt (up to 5x, 1 min apart) before alerting.
- **IIS site**: on failure, the app pool is recycled once *immediately* and rechecked
  15 seconds later; if it's still not 200, that whole attempt counts as failed and the
  5x/1-min outer retry takes over (so effectively the pool may get recycled again on
  each subsequent attempt too).
- **Windows service**: on failure, a start/continue is attempted immediately; if it's
  still not `Running`, that attempt counts as failed and the outer retry loop tries again.

## Extending

To monitor something new, implement `IHealthChecker` and add it to
`Worker.BuildCheckers()` (and, if it needs its own config section, add an options class
under `Settings/`). The retry, delay, and SMS-alert behavior is automatic for anything
implementing `IHealthChecker` — you don't need to touch `RetryingHealthCheckRunner`.

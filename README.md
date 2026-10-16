# Northwind Support Desk

Northwind Support Desk manages ticket creation, filtering, assignment, escalation, and tracking. It uses a .NET 10 API, React 19 with TypeScript, and SQL Server. Tasks 1–4 are implemented; see the [assignment requirements](ASSIGNMENT_CANDIDATE.md).

## Quick start

Prerequisites: .NET 10 SDK, Node.js 22 (20.19+), and Docker Compose. From the repository root:

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
notepad .env
```

In `.env`, set `MSSQL_SA_PASSWORD` to the SQL Server `sa` password you intend to use. Keep it identical to the password in `ConnectionStrings__Default`; the supplied fixture uses the same value in both. Compose builds the container API connection string from `MSSQL_SA_PASSWORD` and connects to `sqlserver`, while a host API uses `ConnectionStrings__Default` and connects to `localhost`. On the first Compose start, SQL Server initializes its data volume with `MSSQL_SA_PASSWORD`. An existing initialized volume keeps its original SQL password; changing `.env` does not change that database password. Do not reset or delete the volume as part of normal setup.

The seeded agent login is `alex.turner@northwind-support.example` with password `LocalDevOnly!2026`. This deliberately shared, development-only fixture is used by the seeder to hash the six seeded agents' passwords. It is not a production credential. Set `DevelopmentCredentials__Password` in `.env` to this value for both host API and Compose runs.

The API requires `Jwt__Issuer`, `Jwt__Audience`, `Jwt__SigningKey` and `Jwt__TokenLifetimeMinutes`. The example contains the issuer, audience, and a 60-minute lifetime, but its signing key is only a placeholder. Generate a random key of at least 32 bytes and copy it to the clipboard:

```powershell
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$bytes = New-Object byte[] 32
$rng.GetBytes($bytes)
$rng.Dispose()
$key = [Convert]::ToBase64String($bytes)
Set-Clipboard -Value $key
```

Paste the clipboard value into the untracked `.env` as `Jwt__SigningKey`, then clear the clipboard with `Set-Clipboard -Value ''`. Do not commit the key. The four `Jwt__*` values above are passed to the API by Compose. For host development, the API command below sets them in the current PowerShell process; `dotnet` does not load `.env` automatically.

After configuring `.env`, install the dependencies:

```powershell
npm.cmd ci
```

### Host-based development

Start SQL Server:

```powershell
npm.cmd run db:up
```

Then start the API in one PowerShell window. The API does not load `.env` automatically, so this reads the local connection string and development password into the current process:

```powershell
$envLines = Get-Content .env
function Get-LocalSetting([string]$name) {
  $entry = $envLines | Where-Object { $_.StartsWith("$name=") } | Select-Object -First 1
  if ($null -eq $entry) { throw "Missing $name in .env" }
  $entry.Substring($name.Length + 1)
}
$env:ConnectionStrings__Default = Get-LocalSetting 'ConnectionStrings__Default'
$env:DevelopmentCredentials__Password = Get-LocalSetting 'DevelopmentCredentials__Password'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Jwt__Issuer = Get-LocalSetting 'Jwt__Issuer'
$env:Jwt__Audience = Get-LocalSetting 'Jwt__Audience'
$env:Jwt__TokenLifetimeMinutes = Get-LocalSetting 'Jwt__TokenLifetimeMinutes'
$env:Jwt__SigningKey = Get-LocalSetting 'Jwt__SigningKey'
npm.cmd run api
```

This runs the API on the host; it connects to SQL Server at `localhost` and uses the HTTPS development URL. In a second window, run `npm.cmd run web` and open <http://localhost:5173>. The host API and Swagger are at <https://localhost:7043> and `/swagger`. Environment variables apply only to that API process. Sign in with the development credentials above.

### Docker Compose

To run SQL Server and the API in containers, ensure `.env` has the SQL password and JWT settings described above, then run:

```powershell
docker compose up --build
```

Compose starts SQL Server and then the API after SQL reports healthy. The API is published at <http://localhost:5080> (container port 8080). Development startup applies EF Core migrations and seeds data. Check the database-aware health endpoint at <http://localhost:5080/health>; it returns `200` when SQL Server is reachable and `503` otherwise. `apps/web/.env.example` defaults the Vite proxy to the host API at `https://localhost:7043`; to use the host frontend with the Compose API, set the proxy in that terminal:

```powershell
$env:VITE_API_PROXY_TARGET = 'http://localhost:5080'
npm.cmd run web
```

The named SQL volume persists across normal starts; this README does not ask you to reset or remove it. For a production deployment, run pending migrations as a controlled, one-time step before rolling out API instances, rather than having every instance migrate the database at startup.

## What was implemented

**Task 1 — filtering and DEFECT-117.** The ticket-list hook omitted non-search filters from its effect dependencies, so changing status could update state without an API request. The fix requests on filter changes and keeps search debounced. The regression test asserted the request; its recorded pre-fix result was zero calls where one was expected. The API composes exact-match, assignment, trimmed title/reference/customer search, and SLA filters in SQL before count and paging; filters use AND.

**Task 2 — triage, SLA, and escalation.** Creation applies priority, configured SLA, Premium adjustment, and specialist/capacity-aware assignment, returning the decisions and reasons. If no agent qualifies, the ticket is created unassigned. Escalation raises priority one level, recalculates the due date, retains or reselects an eligible agent, and records immutable history. The UI shows triage, a text-labelled SLA indicator, history, and a validated escalation form. A SQL Server application lock protects the capacity check during concurrent creates, which can wait up to its timeout.

**Task 3 — authentication.** Agent rows hold nullable password hashes created with ASP.NET Core Identity’s salted `PasswordHasher`. Login issues a configured JWT; fallback authorization protects API endpoints, with login, `/health`, and Development Swagger/OpenAPI left anonymous. Escalation actors come from the authenticated identity. The frontend provides login, protected routes with destination restoration, logout, and central 401 handling.

**Task 4 — Docker.** The multi-stage .NET 10 build restores before source copy and runs on the ASP.NET runtime as non-root `app`. Compose supplies runtime settings, waits for SQL health, and publishes API port 5080; `/health` is public. The React app remains on the host and can use the proxy command above.

## Design decisions

Priority, SLA, and assignment policies are shared by creation and escalation, keeping rules out of controllers and avoiding duplicate handler logic. Repository boundaries isolate persistence; separate command/query handlers and read projections keep writes distinct from UI reads. These policy objects make decisions independently testable.

Filters compose as `IQueryable` predicates and are counted and paged by SQL; loading all tickets into memory would waste work and give incorrect filtered pages. A new filter is another predicate in the same query. Substring search may need an indexed or full-text approach at scale.

Agent stores the hash, avoiding a second account table for the same identities. Central JWT authorization derives the escalation actor from the validated token. The browser uses `sessionStorage` to retain sign-in on tab reload, with the trade-off that JavaScript can read the token. Docker receives configuration at runtime rather than baking it into the image.

## Database and Docker details

EF Core migrations add escalation history and Agent password hashes. `TicketEscalations` has a Tickets foreign key and index `(TicketId, EscalatedAtUtc, Id)` for per-ticket newest-first history with a deterministic tie-break. Other ticket indexes cover common status/priority, customer, category, assignment, and due-date queries.

The final API image measured **454,867,645 bytes**; prior same-engine measurements recorded 340,011,267 bytes for the ASP.NET runtime base and 1,303,854,013 bytes for the SDK image. A `.cs`-only change also reused the restore cache. These checks were reported from earlier verification, not rerun for this update.

The candidate reported a separate fresh-volume check: migrations and seeding produced 5 categories, 6 customers, 6 agents, and 40 tickets; health, login, and authenticated ticket retrieval succeeded. The candidate also manually checked the runtime user, Swagger, browser flows, API authentication, and generated SQL. These are manual checks, not automated tests; the existing development volume was left in place.

## Authentication and security notes

JWT settings, database connection, and seed password are configuration values. `.env` is ignored; `.env.example` contains local fixtures and a signing-key placeholder. Never put real credentials in tracked files. Tokens last 60 minutes; logout clears the browser copy but cannot revoke a copied token. Agents share the same access, and ticket creation has no customer identity or ownership check.

## Tests and verification

Run the backend and frontend suites from the repository root:

```powershell
$env:NODE_ENV = 'test'
npm.cmd test
```

The recorded result was **187 passing tests**: 134 backend unit tests, 4 architecture tests, and 49 frontend tests. `NODE_ENV=test` avoids inheriting a production React build where Testing Library needs `React.act`. API/web builds, web lint, .NET formatting, and Git whitespace checks were also previously reported as passing; none were rerun for this update.

Test sources cover filtering and paging, DEFECT-117, SLA boundaries, assignment and escalation, login failures, authorization, frontend routes/401 handling, and health. The candidate’s manual checks used Swagger, Chrome DevTools, PowerShell, Docker, and SQL inspection. No production-scale load or performance test is claimed.

## AI usage

I divided the work into tasks and smaller implementation and verification phases. ChatGPT helped structure plans, clarify requirements, guide debugging, and prepare verification steps. OpenAI Codex generated or modified most implementation code and helped create and run tests. I inspected changes, followed relevant code paths to understand the architecture and behavior, investigated errors, and made focused commits; I did not accept every suggestion automatically.

I retained the centralized frontend authentication/session flow largely as generated after inspection, tests, and browser checks. During the Codex-assisted Phase 4 review, Codex identified that `appsettings.Development.json` was included in the published image. I reviewed the issue and why environment-specific configuration should not be baked into the image. Codex-assisted correction work excluded the file through the root `.dockerignore`; I reviewed the verification evidence and the rebuilt image was checked to confirm the file was absent. The final exclusion is visible in the repository; the intermediate state is from the development process, not a separate commit. A suggested PowerShell key example using `RandomNumberGenerator.Fill()` failed in my environment, so I used `RandomNumberGenerator.Create()` with `GetBytes()`. Codex helped diagnose that `NODE_ENV=production` caused a `React.act` test-environment failure; `NODE_ENV=test` resolved this environment issue, not an application bug. I reviewed changes and results and personally performed the Swagger, browser, PowerShell, Docker, and SQL checks above.

## Known limitations

- A token in `sessionStorage` is readable by JavaScript and remains valid until expiry if copied; there is no server-side revocation.
- Seeded credentials are development fixtures only. Production credential provisioning and a roles/permissions system are not included.
- Substring search may need a different indexing/search strategy for a very large ticket table.
- Migrations and demo seeding run automatically only in Development; production should apply migrations in a controlled step before API rollout.
- Customer-facing identity/ownership, registration, password reset, MFA, OAuth/SSO, refresh tokens, notifications, background jobs, real-time updates, and other deployment infrastructure were outside this assignment’s scope.

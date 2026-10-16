# Northwind Support Desk

A support ticket management system: raise tickets, search and filter them, assign them to agents,
and move them through their lifecycle.

**.NET 10 Web API** + **React 19 / TypeScript** + **SQL Server**, in one monorepo.

---

## The assignment

You are joining a team that already has this app working. Complete the tasks **in order**.
Full requirements are in **[`ASSIGNMENT_CANDIDATE.md`](ASSIGNMENT_CANDIDATE.md)**.

| # | Task | Done looks like | Effort |
| --- | --- | --- | --- |
| 1 | **Fix a defect, add server-side filtering** | Root cause fixed with a regression test; every list filter applied in the database with a correct count | ~2.5 h |
| 2 | **Ticket escalation & assignment** | Automatic triage on create, escalation with history, SLA status, UI for all of it | ~7 h |
| 3 | **Login and authentication** | Agents sign in; API rejects unauthenticated calls; login UI | ~4 h |
| 4 | **Dockerise the API** | `docker compose up` gives a migrated, seeded API with no baked-in secret | ~1.5 h |

### Guidance

- **Time: 2 days.** Honestly marking a task "not finished" beats rushing all of them.
- **Fork** this repo to your own GitHub account and work there. **Do not push to the original.**
- **AI tools are allowed.** You must understand and be able to defend every line you submit.
- **Commit as you go** and keep the real history. Commit the Task 1 defect fix and the filtering
  separately from each other and from the feature work. A single squashed commit is not accepted.
- **Where the spec is ambiguous**, make a decision, write it down in the README, and move on.
- **Out of scope:** registration, password reset, roles, OAuth/SSO, refresh tokens, MFA,
  notifications, CI, caching, visual redesign. Mention them under *Known limitations* instead.

### Submission

1. Update this **README** with what you built, your design decisions (chosen, rejected and why),
   the defect's root cause, dev login credentials, how to run and test it, AI usage, and known
   limitations. A reviewer must be able to run it without asking you anything.
2. Add an **APPROACH.md** explaining your approach and thought process for each task.
3. Commit both, then send us the **link to your fork**, the **dev login credentials**, and a
   **rough note of time spent** on what. (If the fork is private, add the reviewers as collaborators.)

---

## Quick start

**Prerequisites:** .NET SDK 10 (`global.json`), Node.js 22 (`.nvmrc`; 20.19+ works), Docker.

```bash
cp .env.example .env          # SQL Server container settings
npm install                   # installs the web app
npm run db:up                 # SQL Server 2022 in Docker on localhost:1433
npm run web                   # terminal 2: React dev server
```

| What | Where |
| --- | --- |
| Web app | http://localhost:5173 |
| API | https://localhost:7043 (also http://localhost:5043) |
| Swagger | https://localhost:7043/swagger |

In Development the API applies migrations and seeds demo data on startup. Before running `npm run api`,
set the seed password and JWT settings in that terminal. The `.env.example` file is for reference
and Docker Compose; `dotnet run` does not automatically load `.env` files. The API fails at startup
if required development credential or JWT configuration is missing or invalid. **Never commit real
credentials.** These PowerShell variables belong to the current terminal process, so run the setup
again whenever you open a new terminal before starting the API.

For PowerShell, use the fixture value from `.env.example` and generate a fresh local JWT signing key:

```powershell
$env:DevelopmentCredentials__Password = (Get-Content .env.example | Where-Object { $_ -like 'DevelopmentCredentials__Password=*' }).Split('=', 2)[1]
$env:Jwt__Issuer = "NorthwindSupportDesk"
$env:Jwt__Audience = "NorthwindSupportDesk"
$env:Jwt__TokenLifetimeMinutes = "60"
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
$keyBytes = New-Object byte[] 32
$random.GetBytes($keyBytes)
$env:Jwt__SigningKey = [Convert]::ToBase64String($keyBytes)
npm.cmd run api
```

On macOS or Linux, run this from the repository root:

```bash
export DevelopmentCredentials__Password="$(sed -n 's/^DevelopmentCredentials__Password=//p' .env.example)"
export Jwt__Issuer=NorthwindSupportDesk
export Jwt__Audience=NorthwindSupportDesk
export Jwt__TokenLifetimeMinutes=60
export Jwt__SigningKey="$(openssl rand -base64 32)"
npm run api
```

For Visual Studio, provide the same environment variables in your local process environment or
start Visual Studio from a terminal where they are set. Do not save a real signing key in the tracked
`launchSettings.json` file.

**Visual Studio 2026:** run `npm run db:up`, open `SupportDesk.sln`, set
**SupportDesk.Presentation** as the startup project, ensure the required environment variables are
available to it, and press F5 on the **https** profile.

## Docker API

Docker API development requires Docker Desktop or a compatible Docker Engine with Docker Compose,
along with the .NET 10 and Node.js prerequisites above for host-based development. The React app
remains outside Docker.

Create `.env` only if it does not already exist, then edit it locally:

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
```

Set a random JWT signing key of at least 32 bytes in `.env`. The required settings are
`MSSQL_SA_PASSWORD`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__SigningKey`,
`Jwt__TokenLifetimeMinutes`, and `DevelopmentCredentials__Password`. The example supplies local
development values for these settings. Do not commit `.env` or place real secrets in tracked files.
Compose passes configuration to the API at container startup, and environment variables take
precedence over appsettings files. The Docker build excludes `appsettings.Development.json`, which
retains the local connection fallback for host-based development. The image contains the general
`appsettings.json` settings only; the database connection and JWT configuration are supplied at
runtime. Compose supplies the API environment name through `ASPNETCORE_ENVIRONMENT`, defaulting to
`Development`.

For host-based `npm run api`, `ConnectionStrings__Default` uses `localhost,1433`. The API container
uses the Compose service hostname `sqlserver` for the same `SupportDesk` database. Compose starts the
API only after the SQL Server health check reports healthy. The API listens on port 8080 inside its
container and is published only on the local machine at `http://localhost:5080`.
SQL Server data remains in the existing persistent named volume across normal Compose starts.

Start SQL Server and the API with:

```powershell
docker compose up --build
```

The API runs in Development for this assessment. Its existing startup path applies EF Core migrations
before running the demo seeder. The seeder preserves existing data and skips demo ticket creation
when tickets already exist. Production deployments should apply migrations through a controlled
deployment step instead of having every API instance run migrations at startup.

Check database readiness without a token:

```powershell
Invoke-WebRequest http://localhost:5080/health
```

The endpoint checks database connectivity and returns HTTP 200 when it can connect or HTTP 503 when
the database is unavailable. It returns no connection details.

To obtain a token, use an active seeded agent email and the value of
`DevelopmentCredentials__Password` from `.env`. The fixture is development-only. For example, in
PowerShell:

```powershell
$password = Read-Host "Development seed password"
$body = @{ email = "alex.turner@northwind-support.example"; password = $password } | ConvertTo-Json
$session = Invoke-RestMethod -Method Post -Uri http://localhost:5080/api/auth/login `
  -ContentType "application/json" -Body $body
$headers = @{ Authorization = "Bearer $($session.accessToken)" }
Invoke-RestMethod -Uri http://localhost:5080/api/tickets -Headers $headers
Remove-Variable password, body, session, headers
```

To run the Vite frontend against the Docker API, keep the frontend on the host and set the proxy
target for that terminal:

```powershell
$env:VITE_API_PROXY_TARGET = "http://localhost:5080"
npm.cmd run web
```

The default in `apps/web/.env.example` remains `https://localhost:7043` for the existing host-based
API workflow. The Vite proxy forwards the normal `/api` paths, including login and bearer-authenticated
requests.

### Docker verification status

`apps/api/Dockerfile` uses a .NET 10 SDK stage to restore, build and publish, followed by an ASP.NET
10 runtime stage. The root `.dockerignore` excludes build output, `node_modules`, `.git`, `.env`
files, and the web app from the build context. `docker compose config --quiet` was checked during
configuration work with temporary, non-production values. For the cache check, a disposable build
context excluded `.env`, appsettings development configuration, `.git`, and local build output.
After a comment-only change to a `.cs` file in that context, Docker reported the `dotnet restore`
step as `CACHED`; source copy and publish reran.

Image sizes below are Docker's `Size` values in bytes, inspected on the same engine. The final API
image uses the ASP.NET runtime stage; it includes the application and is larger than that base image,
but smaller than the SDK image.

| Image | Size |
| --- | ---: |
| `northwind-support-api:latest` | 454,867,645 bytes |
| `mcr.microsoft.com/dotnet/aspnet:10.0` | 340,011,267 bytes |
| `mcr.microsoft.com/dotnet/sdk:10.0` | 1,303,854,013 bytes |

The Compose service check `docker compose exec -T api whoami` returned `app`. Image inspection also
reported user `app` and exposed port `8080/tcp`. To repeat these checks, use `docker compose exec api
whoami` and `docker image inspect northwind-support-api:latest --format '{{.Config.User}} {{json
.Config.ExposedPorts}}'`.

The following end-to-end results are **manual evidence reported by the user**, not independently run
by Codex in this verification. The existing SQL Server volume was left in place, with 47 tickets. The
API started at `http://localhost:5080`; anonymous `GET /health` returned 200 and anonymous
`GET /api/tickets` returned 401. Login as seeded Alex Turner and authenticated ticket requests worked.
The Vite frontend logged in through the Docker API and loaded tickets, detail, SLA, and escalation
history. For the isolated project `northwind-dk6`, the user reported successful migration and seed,
anonymous health 200, login, and authenticated ticket retrieval with 40 tickets (5 categories, 6
customers, 6 agents, and 40 tickets seeded).

The automated health endpoint tests also cover a reachable database returning 200, an unavailable
database returning 503, and anonymous access while another endpoint remains protected. Do not run
`docker compose down -v` against the normal development project or remove either database volume.

## Tests

```bash
npm test                      # dotnet test, then the web tests (Vitest)
```

At final Task 4 verification, all 187 tests passed: 134 backend unit tests, 4 architecture tests,
and 49 frontend tests. If Windows PowerShell has `NODE_ENV=production`, Vitest inherits that setting
and React's production build does not expose `React.act`, which Testing Library needs. Set
`NODE_ENV=test` in the current PowerShell session before running the suite:

```powershell
$env:NODE_ENV = 'test'
npm.cmd test
```

The API and web production build, web lint, `dotnet format --verify-no-changes --no-restore`, and
`git diff --check` also passed.

## Commands

| Command | What it does |
| --- | --- |
| `npm run db:up` / `db:down` / `db:reset` | Start / stop / wipe SQL Server |
| `npm run api` / `npm run web` | Run the API / the React app |
| `npm run build` | Build both |
| `npm run lint` / `typecheck` / `format` | Web lint and types / `dotnet format` |
| `npm run ef -- migrations add <Name>` | Add an EF Core migration |

## API at a glance

| Endpoint | Description |
| --- | --- |
| `GET /health` | Public database-connectivity check; returns 200 when reachable and 503 otherwise |
| `POST /api/auth/login` | Verify active agent credentials and return a signed JWT, expiry and agent details |
| `GET /api/auth/me` | Return the current agent from a validated bearer token |
| `GET /api/tickets` | Paged list with `search`, `status`, `priority`, `slaStatus`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `sortBy`, `sortDirection`. Supported filters are applied server-side before the filtered count, sort, and page are calculated. |
| `GET /api/tickets/{id}` | One ticket |
| `POST /api/tickets` | Create a ticket and return its automatic priority, SLA and assignment triage |
| `PATCH /api/tickets/{id}/status` · `/assignment` | Change status / assignee |
| `POST /api/tickets/{id}/escalate` | Escalate an open ticket by one priority level and recalculate its SLA and assignment |
| `GET /api/tickets/{id}/escalations` | Read immutable escalation history, newest first |
| `GET /api/customers` · `/api/agents` · `/api/categories` | Reference data |

Errors are `ProblemDetails`: `400` validation, `404` not found, `409` conflict or business rule.

Ticket list filtering is implemented in `TicketQueries.GetPagedAsync` and composed as
`IQueryable` predicates before `CountAsync`, `Skip`, and `Take`. Search trims surrounding
whitespace, ignores blank values, and matches title, reference, or customer name without
case sensitivity. Multiple filters use AND semantics; `assignedAgentId` and
`unassignedOnly` are also combined with AND when both are supplied. `slaStatus` accepts
`NotApplicable`, `Met`, `Breached`, `AtRisk`, or `WithinSla`; its SQL predicate is applied to
the same query before `CountAsync`, sorting, and pagination. Status is derived with one injected
UTC time per request. At-risk cutoffs use the configured priority window, Premium multiplier,
minimum window, and threshold; an escalation's current priority and recalculated due date define
its current SLA window without additional persisted status or window fields. The returned list
status uses the same evaluator and settings as the filter. A future filter can be added as another
predicate in `TicketQueries.ApplyFilters`, preserving the existing count and paging pipeline.

Ticket query tests use SQLite to exercise relational filtering, count, and pagination. Live SQL
Server verification returned 25 tickets for `slaStatus=Breached` and 1 for
`slaStatus=AtRisk`. The combined `status=New&priority=Medium&slaStatus=AtRisk` filter was also
checked. With `slaStatus=Breached&pageSize=2`, the filtered result contained 25 tickets across
13 pages. EF command logs showed the filters in SQL `WHERE` before `COUNT`, followed by stable
`ORDER BY` and SQL Server `OFFSET`/`FETCH` pagination. These checks cover the listed requests;
they do not establish that every possible filter combination was exercised.

### DEFECT-117: ticket filters refresh immediately

Changing a ticket filter previously did not always refresh the list. The `useTicketList` effect
tracked search and paging values but omitted other filters, so a status change could update the
filter state without requesting the matching tickets. Commit `1f65c9b` corrected the hook to
request immediately when a non-search filter changes while keeping search debounced. The
regression test passes with the fix; against the original hook it fails at the API-call assertion:
it expected one request and received zero.

**Seed data:** 5 categories, 6 customers (3 Premium), 6 agents (one inactive, one at their limit)
and 40 tickets across every status, priority and SLA state.

### Automatic triage on ticket creation

`POST /api/tickets` reuses the domain `PriorityPolicy`, `SlaCalculator` and
`AgentSelectionPolicy`. The handler loads the customer and category flags, applies the requested
priority (defaulting to Medium and allowing forced Critical categories), calculates the due date
from the injected UTC clock, counts current open tickets in SQL, and selects the eligible agent
with the lowest workload and deterministic ID tie-break. The applied priority, due date, derived
SLA status, assigned agent and a `triage` object containing the priority, SLA and assignment
reasons are returned in the `201 Created` response.

When no active, below-capacity specialist is eligible, creation still succeeds with
`assignedAgent: null` and the policy's unassigned reason. Ticket creation now runs under a
transaction-scoped SQL Server application lock, covering the workload read, deterministic
selection and persistence across API instances. The operation runs through EF Core's SQL
Server execution strategy so the user transaction is compatible with the configured
connection retries. This serializes ticket creation and prevents the create path from
assigning more open tickets than an agent's configured capacity. The trade-off is that
concurrent creates wait for the lock (up to 30 seconds) and the guarantee depends on all
ticket writers using this application path.

API requests now require an authenticated agent bearer token, but ticket creation does not have an
authenticated customer identity or ownership check. The create request's `customerId` is trusted
after existence validation; callers must not treat this development endpoint as an
access-controlled customer-facing API.

The six seeded development agents have hashes initialized from the local-only password fixture
documented in `.env.example`. Do not reuse this fixture outside a local development database.

The nullable `PasswordHash` is stored on `Agent` because agents are the identities this assessment
will authenticate, and a separate `Users` table would duplicate those identities without adding a
separate account lifecycle. Keeping the field nullable also lets existing agent rows be migrated
and backfilled safely.

### Agent login and JWTs

`POST /api/auth/login` accepts an email and password. It issues a signed bearer token only for an
active agent with a valid password hash. Unknown emails, incorrect passwords, inactive agents and
agents with missing or unusable hashes receive the same `401` response, without disclosing whether
an account exists. The response includes `accessToken`, `expiresAtUtc`, and the agent's ID, name and
email. Passwords and password hashes are never returned.

`GET /api/auth/me` requires a valid bearer token and loads the agent identified by its validated
`sub` claim. It returns only the agent's ID, name and email, or `401` if the token is missing,
invalid, expired or refers to an agent that no longer exists. Swagger's **Authorize** control can
send the returned token to this endpoint. The central fallback authorization policy requires a
valid bearer token for protected API endpoints. `POST /api/auth/login` and `GET /health` are
accessible anonymously; the health endpoint checks database connectivity and returns `200` when
available or `503` otherwise. In Development, Swagger UI and its OpenAPI document are also public.
The API uses ASP.NET Core's JWT bearer handler to validate the signature, issuer, audience and expiry.

To verify in Swagger, post the `alex.turner@northwind-support.example` email and the password fixture
from `.env.example` to `/api/auth/login`. Copy the returned token into **Authorize**, then call
`/api/auth/me`. Yuki's seeded account is inactive and should receive `401`; a wrong password and an
unknown email also return the same generic `401` message.

After authorizing in Swagger, verify that `GET /api/tickets` returns `200`; without a token it
returns `401`. Protected customer, agent, category, ticket, and escalation routes follow the same
rule. To record an escalation, send a valid reason such as
`{"reason":"Customer impact increased"}` to `POST /api/tickets/{id}/escalate` without an
`escalatedBy` property. A successful request returns `200`; the response and subsequent
`GET /api/tickets/{id}/escalations` history should name the signed-in agent as the actor. An
ineligible ticket continues to return `409`, and an unknown ticket returns `404`.

JWT issuer, audience, signing key and lifetime are configured with `Jwt__Issuer`,
`Jwt__Audience`, `Jwt__SigningKey` and `Jwt__TokenLifetimeMinutes`. The committed signing-key value
in `.env.example` is a placeholder and is rejected by the API until replaced. Generate a random
32-byte local key as shown in the setup instructions; never commit a real signing key. The documented
local configuration sets tokens to 60 minutes, limiting their exposure window while avoiding
refresh-token or session infrastructure that is outside this phase.

### Frontend authenticated sessions

The web app's `/login` page signs in through `POST /api/auth/login`. All other application routes
require a valid session; an unauthenticated visitor is sent to `/login`, and a successful sign-in
returns them to the requested path and query string. A direct login with no saved destination opens
the ticket list. On reload, the app checks a saved token with `GET /api/auth/me` before rendering
protected pages. The header shows the API-provided agent name, and **Log out** clears the session.

The frontend stores only the JWT in `sessionStorage`. It survives reloads in the same browser tab
and is removed when that tab's session ends. The API client adds it as a bearer token to protected
requests. A protected API `401` clears the token and agent state and redirects to login while keeping
the current destination; a login `401` remains an invalid-credentials message. The password is never
stored. Because JavaScript can read `sessionStorage`, a cross-site scripting vulnerability could
expose the token; this assessment does not add refresh tokens or an HttpOnly cookie session.

To try the web flow locally, start the API with the Development and JWT environment variables above,
then run `npm run web` and open `http://localhost:5173`. Sign in with an active seeded agent and the
development password fixture documented in `.env.example`.

For a manual check, open `/tickets` in a private browser tab and confirm it redirects to login,
then sign in and verify the ticket list loads with the agent name in the header. Reload the page to
check session restoration, escalate an eligible ticket to verify the actor field is absent, and log
out to confirm protected pages return to login.

### Ticket escalation and history

`POST /api/tickets/{id}/escalate` requires a 5-500 character `reason`; the caller does not supply
`escalatedBy`. The API reads the stable agent ID from the validated JWT subject, resolves the active
agent, and stores that agent's name as the escalation actor. Any `escalatedBy` property included in
the request body is ignored. It rejects Critical, Resolved and Closed tickets
with `409 Conflict`, raises priority exactly one level, recalculates the configured SLA from the
escalation time, and preserves or reselects an eligible agent using the same capacity and
specialization policy. Each successful escalation is saved with immutable before/after priority,
agent and due-date values. `GET /api/tickets/{id}/escalations` queries only that ticket and
returns the history newest first.

The `TicketEscalations` table is introduced by the `AddTicketEscalations` EF Core migration.
Apply it with `npm run db:migrate` before using the escalation endpoints against an existing
database.

The migration adds `IX_TicketEscalations_TicketId_EscalatedAtUtc_Id` on `TicketId`,
`EscalatedAtUtc`, and `Id`. `TicketId` comes first because history requests filter for one ticket;
the remaining columns support newest-first ordering, with `Id` providing a deterministic
tie-break when escalation timestamps match.

The ticket list and detail page share the same text-labelled SLA indicator. The list's SLA status
filter uses the existing `slaStatus` query parameter, combines with the other filters, resets to
the first page, and requests immediately while search remains debounced. The detail page shows
newest-first escalation history and provides a reason form with the API's 5-500 character
validation. The API records the authenticated agent as actor, and the history displays that
server-provided name. A successful escalation updates the ticket and reloads its history, while
server errors including conflicts are shown without applying a failed update. The create confirmation
displays the priority, due date, and assignment outcome returned by the API, including its reason
when the ticket is left unassigned.

## Known limitations

- A bearer token in `sessionStorage` is readable by JavaScript; an XSS flaw could expose it. Logout
  clears the browser's copy, but a copied JWT remains valid until its configured 60-minute expiry.
- All authenticated agents currently have the same API access. There is no roles or permissions
  matrix, registration, password reset, MFA, OAuth/SSO, refresh-token flow, account lockout or
  rate limiting.
- The seeded password is a local development fixture only. Production credential provisioning is
  outside this assessment implementation.

## AI usage

OpenAI Codex was used to inspect the assignment and existing application, implement the frontend
login/session integration, add focused regression tests, and review the final changes. The workflow
that was effective was to inspect the existing API client, routes and test patterns first, then make
targeted changes and verify them with automated tests, builds, lint, formatting and a Git diff.
Generated frontend changes were verified with 49 Vitest tests; the backend suite passed 134 unit
tests and 4 architecture tests (187 automated tests total). During the final audit, Codex identified
missing integrated coverage for a protected request returning 401 and redirecting back to login; a
regression test was added.
This audit did not find a production defect introduced by AI-generated code.

The candidate should add a personal example of one AI suggestion accepted unchanged and one changed
or rejected, with the reason, and any AI-introduced bug they observed. Those historical details are
not inferable from the repository or this verification run.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| API cannot reach the database | `npm run db:up`, wait ~20 s, check `docker compose ps` |
| Port 1433 already in use | Change the port in `docker-compose.yml` and `.env` |
| Browser warns about the certificate | `dotnet dev-certs https --trust`, or use the web app, which proxies |
| Web app shows "Unable to load tickets" | The API is not running, or `VITE_API_PROXY_TARGET` points at the wrong port |
| SQL Server slow on Apple Silicon | Enable Rosetta in Docker Desktop → Settings → General |

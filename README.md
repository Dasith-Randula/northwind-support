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
| 4 | **Dockerise the API** *(optional)* | `docker compose up` gives a migrated, seeded API with no baked-in secret | ~1.5 h |

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
npm run api
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

## Tests

```bash
npm test                      # dotnet test, then the web tests (Vitest)
```

Both pass on a clean checkout and neither needs a database.

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

Authentication is intentionally deferred to Task 3. Until an authenticated customer or staff
identity is required by the API, the create request's `customerId` is trusted after existence
validation; callers must not treat this development endpoint as an access-controlled
customer-facing API.

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
send the returned token to this endpoint. Every other `/api` endpoint also requires a valid bearer
token through a central fallback authorization policy. The login endpoint is the only public API
route. Swagger UI and its OpenAPI document remain public. The API uses ASP.NET Core's JWT bearer
handler to validate the signature, issuer, audience and expiry.

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
validation; until authentication is implemented, the form also asks for the actor's name. A
successful escalation updates the ticket and reloads its history, while server errors including
conflicts are shown without applying a failed update. The create confirmation displays the
priority, due date, and assignment outcome returned by the API, including its reason when the
ticket is left unassigned.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| API cannot reach the database | `npm run db:up`, wait ~20 s, check `docker compose ps` |
| Port 1433 already in use | Change the port in `docker-compose.yml` and `.env` |
| Browser warns about the certificate | `dotnet dev-certs https --trust`, or use the web app, which proxies |
| Web app shows "Unable to load tickets" | The API is not running, or `VITE_API_PROXY_TARGET` points at the wrong port |
| SQL Server slow on Apple Silicon | Enable Rosetta in Docker Desktop → Settings → General |

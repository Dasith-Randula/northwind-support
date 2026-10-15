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
cp .env.example .env          # optional, every value has a working default
npm install                   # installs the web app
npm run db:up                 # SQL Server 2022 in Docker on localhost:1433
npm run api                   # terminal 1: migrates, seeds, then serves the API
npm run web                   # terminal 2: React dev server
```

| What | Where |
| --- | --- |
| Web app | http://localhost:5173 |
| API | https://localhost:7043 (also http://localhost:5043) |
| Swagger | https://localhost:7043/swagger |

In Development the API applies migrations and seeds demo data on startup. **Never commit real
credentials.**

**Visual Studio 2026:** run `npm run db:up`, open `SupportDesk.sln`, set
**SupportDesk.Presentation** as the startup project, and press F5 on the **https** profile.

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
| `GET /api/tickets` | Paged list with `search`, `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `sortBy`, `sortDirection`. Supported filters are applied server-side before the filtered count, sort, and page are calculated. |
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
`unassignedOnly` are also combined with AND when both are supplied. The SLA status filter
remains part of the later escalation work rather than this Task 1 change.

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

### Ticket escalation and history

`POST /api/tickets/{id}/escalate` requires a 5-500 character `reason` and an
`escalatedBy` value (maximum 100 characters). It rejects Critical, Resolved and Closed tickets
with `409 Conflict`, raises priority exactly one level, recalculates the configured SLA from the
escalation time, and preserves or reselects an eligible agent using the same capacity and
specialization policy. Each successful escalation is saved with immutable before/after priority,
agent and due-date values. `GET /api/tickets/{id}/escalations` queries only that ticket and
returns the history newest first. The `escalatedBy` value is request-supplied until Task 3
authentication is implemented.

The `TicketEscalations` table is introduced by the `AddTicketEscalations` EF Core migration.
Apply it with `npm run db:migrate` before using the escalation endpoints against an existing
database.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| API cannot reach the database | `npm run db:up`, wait ~20 s, check `docker compose ps` |
| Port 1433 already in use | Change the port in `docker-compose.yml` and `.env` |
| Browser warns about the certificate | `dotnet dev-certs https --trust`, or use the web app, which proxies |
| Web app shows "Unable to load tickets" | The API is not running, or `VITE_API_PROXY_TARGET` points at the wrong port |
| SQL Server slow on Apple Silicon | Enable Rosetta in Docker Desktop → Settings → General |

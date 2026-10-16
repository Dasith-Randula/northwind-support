# Technical Approach

## 1. Overall approach

I began by reading the assignment and tracing how the existing ticket application handled requests, persistence, and the web interface. I organized the work into smaller phases, in priority order, and aimed to verify each before moving on. ChatGPT helped structure the plan and interpret requirements; the phased plan was mine. I kept the existing .NET, React, and SQL Server architecture, and used focused commits to make the work easier to review.

Codex generated or modified most implementation code and helped create and run tests. I inspected changes and followed important code paths, investigated failures before accepting corrections, and performed the reported manual checks. This document describes the approach and trade-offs; the README contains setup instructions and a fuller feature summary.

## 2. Task 1 — Defect investigation and server-side filtering

For DEFECT-117, I reproduced the stale-filter behavior and used browser network activity to check whether changes reached `GET /api/tickets`. The cause was in `useTicketList.ts`: its effect omitted non-search filters, so changing one could leave the request stale. The hook now tracks those dependencies and keeps the 300 ms search debounce; other changes request immediately. This fixes the request lifecycle without forcing a page reload or remount. The regression test in `useTicketList.test.ts` asserts the request after filters change. Prior records say the original code made zero requests where one was expected; I did not rerun that comparison for this document.

In `TicketQueries.cs`, filters are composed as `IQueryable` predicates, so SQL applies them before count and paging. Exact-match fields, unassigned-only, and trimmed title/reference/customer-name search combine with AND semantics; SLA uses the same pipeline. Client-side filtering would produce incomplete pages and incorrect totals, while loading everything into C# memory wastes work. Query tests cover combinations, search, counts, paging, and SLA states. Prior verification also reports inspecting generated SQL.

## 3. Task 2 — Automatic triage, SLA, and escalation

I kept priority, SLA calculation, and agent selection in reusable domain policies—`PriorityPolicy`, `SlaCalculator`, and `AgentSelectionPolicy`—and used `SlaEvaluator` to derive status from dates and current time. Creation and escalation share these policies, so handlers, controllers, and React do not maintain separate rules. Category flags force Critical priority for Security and Outage and require matching specialists for Billing, Outage, and Security. Configured base windows are Critical 4 hours, High 8, Medium 24, and Low 72; Premium applies a 0.5 multiplier with a one-hour floor. SLA status is derived as NotApplicable, Met, Breached, AtRisk, or WithinSla, using the configured 25% risk threshold. Selection considers active agents below capacity, chooses the lowest open workload with an ID tie-break, and explains unassigned outcomes.

Escalation raises priority one level, recalculates the due date from escalation time, retains or reselects an eligible agent, and appends a history record containing the reason and before/after values. The EF Core migration adds `TicketEscalations`; its `(TicketId, EscalatedAtUtc, Id)` index supports newest-first history for one ticket with a deterministic tie-break. SLA filtering remains SQL-side. Ticket creation also uses a transaction-scoped SQL Server application lock around capacity-sensitive selection. This coordinates competing creates through that path, at the cost of making a concurrent request wait up to the configured lock timeout.

The web interface has a reusable SLA indicator, triage results, an escalation form with validation and pending/error states, and history. Policy, handler, validator, evaluator, query, and component tests cover time boundaries, invalid escalation, assignment, and history. An injectable clock makes time-dependent rules deterministic in tests.

## 4. Task 3 — Authentication and security

Authentication uses the existing `Agent` identity, extended with a nullable password hash by migration. The assignment already treats agents as the users who act on tickets; using that entity avoids maintaining a second account record for the same people. `IdentityAgentPasswordHasher` adapts ASP.NET Core Identity’s password hasher, and Development seeding hashes the configured fixture password rather than storing plaintext.

`AuthController` issues JWTs and exposes the current-user flow. JWT validation and fallback authorization are configured centrally; inactive agents are rejected, and escalation actor identity comes from the authenticated claims. On the web side, the login page, protected routes, session restoration, logout, and API client’s bearer-token and 401 handling share the auth flow.

The browser stores the token in `sessionStorage`, which restores a session on tab reload without refresh-token infrastructure. JavaScript can read it, so an XSS flaw could expose the token; logout clears the browser copy but cannot revoke an issued token. Tokens expire, but there is no immediate server-side revocation. Prior records report Swagger/browser checks and automated auth tests; none were rerun during this documentation phase.

## 5. Task 4 — Dockerization

The API Dockerfile uses a multi-stage build: the .NET SDK stage restores, builds, and publishes the application, then the final ASP.NET runtime stage executes the published output. Project files are copied before source to reuse the restore layer. The runtime runs as non-root `app` on port 8080. Compose supplies settings at runtime, connects to SQL Server by service name, and waits for its health check. A database-aware `/health` endpoint is public. React remains on the host and Vite proxies API requests to the container.

Development startup applies migrations and seeds fixture data. AI-assisted review identified that the build context included `appsettings.Development.json`; `.dockerignore` now excludes it. The final state is in the repository, while the intermediate state and correction chronology come from my account, not separate commits. Prior manual verification reports a Docker build, restore-cache reuse, non-root runtime, health/authenticated API checks, frontend compatibility, and an isolated fresh-volume migration and seed with 40 tickets. The isolated database avoided deleting the existing volume. Production should apply migrations as a controlled step before API rollout.

## 6. Verification and debugging approach

Previously reported automated checks included 134 backend unit tests, 4 architecture tests, and 49 frontend tests: 187 total. Build, lint, formatting, and SQL/health checks are recorded in the README and audit. Sources include `TicketQueriesTests`, `RaiseTicketCommandHandlerTests`, `EscalateTicketCommandHandlerTests`, `SlaEvaluatorAtRiskTests`, and `AuthFlow.test.tsx`. I did not rerun them for this file.

My reported manual checks included Swagger, Chrome DevTools network activity, PowerShell API calls, UI flows, generated SQL review, and Docker Compose with a fresh database. These are historical, not new F2 results. A frontend test issue came from inheriting `NODE_ENV=production`, which left the environment without `React.act`; setting `NODE_ENV=test` fixed that environment mismatch, not an application defect. This reinforced checking an error’s cause before changing application code.

## 7. Trade-offs, limitations, and lessons learned

Limitations include JavaScript-readable session storage, no immediate token revocation, and substring search that may need another indexing strategy at scale. Seeded credentials are for Development, and production migrations need a controlled deployment step. Registration, refresh tokens, roles, notifications, background jobs, and the optional SLA summary report were outside the implemented scope.

The practical lesson was to trace each requirement to code and a check, and be clear about what was automated, manually verified, or AI-generated. AI sped up implementation and investigation, but evidence was still needed to decide whether a suggestion addressed the failure.


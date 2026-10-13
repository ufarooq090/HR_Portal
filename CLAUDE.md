# HR Portal

Internal HR system covering the employee lifecycle: requisitions, recruiting, resume bank,
onboarding, offboarding, attendance, leave, performance (incl. PIPs), email templates and
Outlook integration. Company: 250–1,000 employees, Pakistan and US, 3 offices, ~10 HR users.

The full specification is in `docs/requirements.md`. Every requirement has an ID
(EMP-01, CT-04, PIP-07, NFR-03, ...). Read the relevant section before building a feature,
and reference requirement IDs in test names and commit messages.

## Tech stack

- Backend: ASP.NET Core on .NET 10 (LTS), C#, minimal APIs grouped by module
- Database: PostgreSQL 16+ via EF Core 10 (Npgsql), code-first migrations
- Frontend: React + TypeScript (Vite), Fluent UI React v9, TanStack Query, React Router
- Auth: Microsoft Entra ID (Microsoft.Identity.Web on the API, MSAL in the SPA)
- Email/calendar: Microsoft Graph SDK, behind interfaces (see Integrations)
- Files: Azure Blob Storage (Azurite locally)
- Background jobs: Hangfire (PostgreSQL storage), hosted in the API process
- Tests: xUnit + Testcontainers (backend), Vitest (frontend), Playwright (E2E, TypeScript)
- Hosting: Azure App Service, Key Vault, Application Insights; CI/CD in GitHub Actions

## Repository layout

```
src/
  HrPortal.Api/            ASP.NET Core host: endpoints, auth, DI, Hangfire
  HrPortal.Domain/         Entities, enums, value objects, business rules (no EF, no HTTP)
  HrPortal.Infrastructure/ EF Core DbContext, migrations, Graph, Blob, PDF, encryption
web/                       React SPA
tests/
  HrPortal.UnitTests/
  HrPortal.IntegrationTests/  real PostgreSQL via Testcontainers
e2e/                       Playwright tests
docs/requirements.md
docker-compose.yml         postgres, azurite, mailpit
```

Organise code by module, not by technical layer, inside each project:
`Employees/`, `Organisation/`, `Requisitions/`, `Recruiting/` (candidates, applications,
interviews, scorecards, resume bank), `Onboarding/`, `Offboarding/`, `Attendance/`, `Leave/`,
`Performance/` (reviews, goals, PIPs), `Templates/`, `Notifications/`, `Audit/`.

## Non-negotiable rules

1. **Authorisation on the server, every time.** Every endpoint has an explicit policy.
   Data scoping (own / team / department / country) is applied in the query, not in the UI.
   Every new endpoint gets an integration test proving a user outside scope gets 403 or no data.
2. **Audit everything.** An EF Core SaveChanges interceptor writes an AuditEntry
   (actor, action, entity, old/new values, UTC timestamp) for every create/update/delete.
   Views of salary, PIP and performance data are audited explicitly. Audit rows are never updated or deleted.
3. **Sensitive fields encrypted at field level:** compensation, bank details, CNIC, SSN.
   Never log them, never return them to roles without access, never put them in URLs.
4. **No secrets in the repo.** Use `dotnet user-secrets` locally and Key Vault in Azure.
5. **Dates stored in UTC** (`DateTimeOffset`); converted to the user's time zone in the UI.
   Calendar-only dates (joining date, leave days) use `DateOnly`.
6. **Money = `decimal` + ISO currency code** (PKR, USD). Never a bare number.
7. **Country is a first-class setting.** Leave rules, holidays, probation (default 3 months),
   notice period (default 30 days), templates and document checklists are configured per
   country, never hard-coded.
8. **Termination emails and letters are never sent automatically** and require the HR Admin role.
9. One `Person` record per human. A candidate who is hired gets an `Employee` linked to the
   same `Person`; history is never duplicated.

## Roles

SystemAdmin, HrAdmin, HrExecutive, HiringManager, Interviewer, LineManager, Employee.
A user can hold several. The permission matrix is in `docs/requirements.md` ("Users and permissions").
Default approver for requisitions and offers is the CEO (configurable approval chain).

The access-by-module table is encoded once in `Domain/Security/PermissionMatrix.cs` (role → module →
capabilities + data scope); `PermissionMatrixTests` checks it cell by cell against the spec. For a new module:
protect endpoints with `Policies.Module(HrModule.X, Capability.Y)` and filter data with
`db.Employees.VisibleTo(db, access, HrModule.X, Capability.Y)` (join your records on the owning employee).
Team scope uses the `employee_report_ids` SQL function. HR country scope (MC-03) defaults to all countries for
HR Admins and the home country for HR Executives, overridable per person in `HrAccessSettings`.
DevAuth environments seed an organisation and an employee record per dev user (`DevDataSeeder`).

## Integrations

Wrap every external system in an interface so it can be faked locally and in tests:
`IEmailSender`, `ICalendarService`, `IFileStore`, `IPdfRenderer`, `IAttendanceImporter`.
- Local/dev: email goes to Mailpit, calendar calls are logged, files go to Azurite.
- Never call the company's live Microsoft 365 tenant from tests. Use a Microsoft 365 developer tenant.
- Biometric attendance arrives as export files from the devices (format still to be confirmed);
  build the importer against a parser interface so the format can be plugged in later.

## Local development login

Entra ID is used in every deployed environment. For local development and E2E tests only,
a `DevAuth` scheme logs in as seeded test users (one per role, both countries).
It must be impossible to enable outside the `Development` / `Test` environments:
fail application startup if `DevAuth` is configured in any other environment.

## Commands

```
docker compose up -d                       # postgres, azurite, mailpit (Mailpit UI: http://localhost:8025)
dotnet tool restore                        # dotnet-ef (local tool manifest)
# one-time: field-encryption key for local dev (never commit keys)
dotnet user-secrets set FieldEncryption:ActiveKeyId dev1 --project src/HrPortal.Api
dotnet user-secrets set FieldEncryption:Keys:dev1 "$(openssl rand -base64 32)" --project src/HrPortal.Api
dotnet build                               # warnings are errors (Directory.Build.props)
dotnet test
dotnet ef migrations add <Name> -p src/HrPortal.Infrastructure -s src/HrPortal.Api
dotnet run --project src/HrPortal.Api      # Development: DevAuth on, migrations applied on startup
                                           # DevAuth: send header X-Dev-User: dev-<pk|us>-<role>, e.g. dev-pk-hradmin
cd web && npm install && npm run dev       # http://localhost:5173, proxies /api to :5195; DevAuth user picker
cd web && npm run lint && npm test && npm run build   # oxlint, Vitest, tsc + vite build
cd e2e && npm install && npx playwright install chromium   # one-time
cd e2e && npx playwright test              # needs docker compose up; starts its own API (Test env, :5295,
                                           # fresh hrportal_e2e DB, throwaway key) and Vite (:5273)
```

CI (`.github/workflows/ci.yml`) runs the backend, frontend and e2e jobs on every push to main and every PR.

Keep this section up to date when commands change.

## Working conventions

- Build in the phase order from the spec. Current phase: **Phase 0 / Phase 1**.
- Small, reviewable steps: one module or one vertical slice at a time
  (entity → migration → endpoints → tests → UI → E2E).
- Run `dotnet build`, `dotnet test` and the frontend lint/tests before saying a step is done.
- Ask before adding a new NuGet or npm dependency not listed in this file.
- When a requirement is ambiguous, check `docs/requirements.md` ("Decisions and remaining
  questions"); if still unclear, ask rather than guess.

## Open items (do not hard-code; leave configurable)

- Leave types, accrual and carry-forward rules per country (HR to provide)
- Payroll system and export format
- Biometric export file format
- Rating scales and review forms per team
- US state(s) of employment
- Whether Pakistan and US are separate legal entities
- Current letter and email templates (HR to provide)

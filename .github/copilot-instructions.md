# Budgetin Check API Workspace Instructions

This repository is the `.NET` backend migration target for Budgetin Check.

- `src/BudgetinCheck.Api` is the active ASP.NET Core Web API project.
- The API is currently a staged replacement for the legacy Next.js BFF from the sibling `budget-app` repository.
- The long-term goal is to move all server-side BFF behavior into this repo and remove the legacy proxy.

## Instruction Map

- Use `.github/instructions/full-api-architecture.instructions.md` when a task needs an end-to-end understanding of how this API works, how it relates to the legacy Next.js BFF, or how new endpoints should be migrated.

## Current Architecture Priorities

- Treat this repo as the future server source of truth for BFF behavior.
- Preserve compatibility with existing mobile and web consumers while migration is in progress.
- Prefer porting behavior natively to C# instead of deepening dependency on the legacy proxy.
- Keep auth, budget-plan ownership checks, and response-shape discipline intact while migrating endpoints.

## How The API Works Today

- `Program.cs` wires the ASP.NET Core host, Swagger, `healthz`, native `/api/bff/*` endpoints, and a catch-all legacy proxy.
- `Features/*` contains endpoint implementations grouped by feature area.
- `Infrastructure/Auth/*` resolves the current session through the legacy `/api/bff/me` response until native auth is in place.
- `Infrastructure/Legacy/*` proxies unmigrated BFF routes to the legacy Next.js server.
- `Infrastructure/Data/BudgetDbConnectionFactory.cs` opens PostgreSQL connections for native C# handlers.
- Native handlers currently use `Dapper` and `Npgsql`, not EF Core.

## Migration Model

- A route should move from proxy mode to native mode only when the C# implementation preserves the legacy contract behavior.
- The migration order should favor shared foundations first:
  - auth/session resolution
  - owned budget-plan resolution
  - database access patterns
  - low-complexity read endpoints
  - higher-risk summary and mutation flows
- Do not remove the legacy proxy for a route until the native implementation is functionally ready.

## Data And Configuration Rules

- `ConnectionStrings:BudgetDb` or `DATABASE_URL` provides the PostgreSQL connection string.
- `LegacyNextJs:BaseUrl` points to the legacy Next.js BFF host used for fallback proxying.
- `BudgetData:SpendingDataRoot` points to legacy spending JSON data when a native endpoint still depends on it.
- Keep configuration names stable unless there is a deliberate migration plan.

## Coding Expectations

- Keep endpoint routing in `Program.cs` or small route modules, and keep feature logic inside focused feature classes.
- Prefer small, explicit services over large mixed utility classes.
- Keep endpoint responses compatible with current consumers.
- Preserve structured JSON errors for BFF routes.
- Keep migration changes narrow and behavior-focused.

## Validation

- Prefer `cd /Users/shakerhd/Documents/Developer/budgetin-check-api && ~/.dotnet/dotnet build ./src/BudgetinCheck.Api/BudgetinCheck.Api.csproj --nologo -v minimal` after changes.
- If routing or endpoint behavior changes, run the narrowest practical validation first.
- Call out any remaining dependency on the legacy proxy in the final summary.
---
description: Use when a task needs an end-to-end understanding of how the Budgetin Check .NET API works, how it relates to the legacy Next.js BFF, or how staged endpoint migration should be implemented.
applyTo: "src/BudgetinCheck.Api/**/*.cs"
---
# Full API Architecture

This repository is the staged `.NET` replacement for the Budgetin Check BFF.

## Repository Role

- This repo is not yet the complete backend.
- It currently hosts a mix of:
  - native C# BFF endpoints
  - infrastructure for auth and database access
  - a proxy to the legacy Next.js BFF for unmigrated routes
- The sibling `budget-app` repository still contains the legacy Next.js BFF implementation being replaced.

## Runtime Shape

- `src/BudgetinCheck.Api/Program.cs` is the composition root.
- Swagger is enabled in development.
- `/healthz` provides a simple health endpoint.
- `/api/bff/*` is the main migration surface.

## Current Endpoint Strategy

- Some routes are implemented natively in C#.
- Remaining BFF routes are forwarded through `LegacyProxyEndpoints`.
- The proxy is a migration tool, not the target architecture.

## Current Native Route Areas

- `Features/Subscription/*`
- `Features/Logo/*`
- `Features/BudgetPlans/*`
- `Features/BudgetSummary/*`
- `Features/Proxy/*`

When adding new native behavior, prefer expanding feature-local C# modules over putting more logic into the proxy layer.

## Auth Model

- `Infrastructure/Auth/CurrentSessionResolver.cs` currently resolves identity via the legacy `/api/bff/me` endpoint.
- This is a temporary bridge while native auth is being established.
- Auth-sensitive work must preserve the same effective user identity and owned budget-plan behavior expected by existing clients.

## Ownership And Scoping

- BFF endpoints must be user-scoped.
- Budget-plan endpoints must only operate on plans owned by the resolved user.
- `CurrentSessionContext.ResolveOwnedBudgetPlanId(...)` is the current ownership gate for native route work.

## Data Access

- Native routes currently use `Npgsql` and `Dapper`.
- `Infrastructure/Data/BudgetDbConnectionFactory.cs` is the canonical connection entrypoint.
- Keep SQL explicit, small, and local to the feature unless a shared repository abstraction becomes justified.
- Do not introduce EF Core by default unless there is a clear migration decision to do so.

## Legacy Integration

- `Infrastructure/Legacy/LegacyBffClient.cs` is the bridge to the old Next.js server.
- `LegacyNextJs:BaseUrl` must be configured for proxy-backed behavior.
- If a route is not fully ported, keep the proxy path working instead of partially breaking compatibility.

## Feature Layout

- `Features/<Area>/...` should contain small endpoint modules and services.
- Keep request handling narrow and feature-owned.
- Shared cross-feature behavior belongs in `Infrastructure/*` only when it is truly infrastructural.

## Migration Guidance

When porting a BFF route from Next.js to C#:

1. Preserve auth and owned budget-plan resolution.
2. Preserve response shape and error semantics.
3. Preserve pay-period and finance behavior rather than simplifying it.
4. Prefer a small, testable native route slice over a partial broad rewrite.
5. Leave the legacy proxy in place for unaffected routes.

## Configuration

- `ConnectionStrings:BudgetDb` or `DATABASE_URL`: PostgreSQL connection string.
- `LegacyNextJs:BaseUrl`: target for unmigrated BFF proxying and the current auth bridge.
- `BudgetData:SpendingDataRoot`: path to legacy spending JSON data used by some native summary logic.

## Validation Expectations

- Use `~/.dotnet/dotnet build ./src/BudgetinCheck.Api/BudgetinCheck.Api.csproj --nologo -v minimal` as the default focused validation.
- If a task changes routing or feature registration, validate through the API host entrypoint.
- If a task changes a summary or contract, call out what is now native versus what still depends on the legacy proxy.
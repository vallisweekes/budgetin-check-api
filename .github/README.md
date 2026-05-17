# Budgetin Check API AI Guidance

This `.github` folder contains repository-specific guidance for coding agents working in the `.NET` API migration repo.

## Layout

- `copilot-instructions.md`: workspace-wide guidance for the full API repository.
- `instructions/full-api-architecture.instructions.md`: the canonical architecture map for the staged BFF migration.
- `agents/develop.agent.md`: the default development agent profile for this repo.

## What This Repo Is

- `budgetin-check-api` is the `.NET` backend migration target for the Budgetin Check product.
- It is replacing server-side behavior that currently lives in the Next.js BFF in the sibling `budget-app` repository.
- The API is currently in staged migration mode: a small set of BFF endpoints are implemented natively in C#, and the remaining `/api/bff/*` routes proxy to the legacy Next.js backend.

## Current Architecture

- The entry point is `src/BudgetinCheck.Api/Program.cs`.
- Native endpoints currently include:
  - `/healthz`
  - `/api/bff/subscription`
  - `/api/bff/logo`
  - `/api/bff/budget-plans`
  - `/api/bff/budget-summary`
- Unmigrated `/api/bff/*` routes are forwarded by the legacy proxy.

## Key Principle

- New work in this repo should prefer moving behavior from the legacy Next.js BFF into native C# while preserving auth, owned budget-plan scoping, payload shape, and shared finance rules.
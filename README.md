# Budgetin Check API

Staged `.NET` backend migration workspace for Budgetin Check.

## Prerequisites

- .NET SDK installed in `~/.dotnet`
- Recommended VS Code extensions from `.vscode/extensions.json`

## Open In VS Code

Open the repository root in VS Code. The workspace is configured to:

- use `BudgetinCheck.sln` as the default solution
- add `~/.dotnet` to the integrated terminal path on macOS
- build with the default `build` task
- debug the API with the `BudgetinCheck.Api` launch profile

## Common Commands

```bash
~/.dotnet/dotnet restore
~/.dotnet/dotnet build
~/.dotnet/dotnet run --project src/BudgetinCheck.Api
```

The API exposes Swagger in development at `/swagger`.

## Local Migration Workflow

- Current production backend remains the Next.js BFF in the sibling `budget-app/web-client` repository.
- This `.NET` API is the staged replacement and should be kept aligned with server-side behavior changes made in the Next.js BFF.
- Run the current Next.js backend locally on `http://localhost:5537`.
- Run this `.NET` API locally on `http://localhost:5262`.
- The `.NET` API still proxies unmigrated `/api/bff/*` routes to the Next.js backend, so local `.NET` testing usually requires both servers running.

## Local Backend Switching

- Mobile local testing can switch between:
	- Next.js backend: `EXPO_PUBLIC_API_BASE_URL=http://localhost:5537`
	- `.NET` backend: `EXPO_PUBLIC_API_BASE_URL=http://localhost:5262`
- Keep production deployment settings pointing at the current Next.js/Vercel backend until the `.NET` backend is actually deployed and ready for cutover.
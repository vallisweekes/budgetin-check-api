# Budgetin Check API

Minimal .NET 8 Web API workspace configured for Visual Studio Code.

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

The generated API exposes Swagger in development at `/swagger`.
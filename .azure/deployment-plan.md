# Budgetin Check API Azure Deployment Plan

Status: Ready for Validation

## Goal

Create an Azure DevOps CI/CD pipeline for the `BudgetinCheck.Api` .NET 8 web API with:

- CI build validation on `main`.
- CI build validation on `dev`.
- Docker image builds for non-prod and prod.
- Automatic deployment from the `dev` branch to a non-prod Azure Web App for Containers.
- Controlled deployment to a prod Azure Web App for Containers through an Azure DevOps environment approval.
- Clear resource group and app configuration requirements for Azure.

## Current App

- Repository: `budgetin-check-api`
- Project: `src/BudgetinCheck.Api/BudgetinCheck.Api.csproj`
- Runtime: .NET 8
- Container image: built from `src/BudgetinCheck.Api/Dockerfile`
- Container port: `8080`
- Health endpoint: `/healthz`
- Required runtime configuration:
  - `ConnectionStrings__BudgetDb` or `DATABASE_URL`
  - `LegacyNextJs__BaseUrl`
  - `LegacyNextJs__TimeoutSeconds`
  - `BudgetData__SpendingDataRoot` when spending JSON data is needed

## Selected Azure Architecture

- Azure App Service for Containers for the API.
- Azure Container Registry for Docker images.
- Separate Azure subscriptions for non-prod and prod.
- Separate resource-group-scoped Azure DevOps service connections for non-prod and prod.
- Separate container registries in the non-prod and prod subscriptions.
- Separate App Service instances for non-prod and prod.
- Separate App Service Plans for non-prod and prod.
- Application settings configured per environment in Azure, not committed to source.

## Resource Group Requirements

Non-prod subscription:

- Subscription: `VW-online-DEV`
- Subscription ID: `4800dc47-952e-4b26-b167-fd8c671101e8`
- Resource group: `rg-vw-budgetapp-api-dev-uks-001`

Non-prod:

- Resource group: `rg-vw-budgetapp-api-dev-uks-001`
- Azure Container Registry: `crvwbudgetappapidevuks001`
- App Service: `app-budgetin-check-api-nonprod`
- App Service Plan: `asp-budgetin-check-api-nonprod`
- Runtime stack: Linux custom container
- Location: `uksouth`
- SKU: `B1`

Prod subscription:

- Subscription: `VW-online-PRD`
- Subscription ID: `366cfc3b-1e2c-4708-a880-35380b4202ba`
- Resource group: `rg-vw-budgetapp-api-prd-uks-001`

Prod:

- Resource group: `rg-vw-budgetapp-api-prd-uks-001`
- Azure Container Registry: `crvwbudgetappapiprduks001`
- App Service: `app-budgetin-check-api-prod`
- App Service Plan: `asp-budgetin-check-api-prod`
- Runtime stack: Linux custom container
- Location: `uksouth`
- SKU: `B1`

Shared pipeline requirements:

- Non-prod Azure DevOps service connection: `sc-budgetin-check-api-azure`
- Prod Azure DevOps service connection: `sc-budgetin-check-api-azure-prod`
- Non-prod environment: `budgetin-check-api-nonprod`
- Prod environment: `budgetin-check-api-prod`
- Runtime configuration is provided through Azure DevOps pipeline variables, not required variable groups.
- Each service connection needs `Contributor` on its environment resource group.
- Each service connection also needs `User Access Administrator` on its environment resource group or ACR scope to grant `AcrPull` to Web App managed identities.
- Both subscriptions must have these resource providers registered before first deployment: `Microsoft.ContainerRegistry`, `Microsoft.Web`, and `Microsoft.ManagedIdentity`.

## Pipeline Plan

Created `deployment/azure-pipelines.yml` with these stages:

1. Validate
   - Use .NET SDK 8.x.
   - Restore `BudgetinCheck.sln`.
   - Build `BudgetinCheck.sln` in Release.
2. Build_NonProd
   - Runs on the `dev` branch.
   - Creates the shared ACR if needed.
   - Builds and pushes the Docker image with `nonprod-$(Build.BuildId)` and `nonprod-latest` tags.
3. Deploy_NonProd
   - Runs automatically after non-prod image build on `dev`.
   - Creates or updates the non-prod Web App for Containers.
   - Deploys the non-prod container image.
4. Build_Prod
   - Runs on the `main` branch.
   - Builds and pushes the Docker image with `prod-$(Build.BuildId)` and `prod-latest` tags.
5. Deploy_Prod
   - Runs after prod image build succeeds.
   - Uses an Azure DevOps `production` environment so approvals can be configured in Azure DevOps.
   - Creates or updates the prod Web App for Containers.
   - Deploys the prod container image.

## Pipeline Decisions

- Azure target: Web App for Containers.
- Pipeline provisions missing ACR and App Service resources before deployment.
- Non-prod service connection name: `sc-budgetin-check-api-azure`.
- Prod service connection name: `sc-budgetin-check-api-azure-prod`.
- Prod deployment is gated by the Azure DevOps environment `budgetin-check-api-prod`.

## Generated Artifacts

- `src/BudgetinCheck.Api/Dockerfile`
- `.dockerignore`
- `deployment/azure-pipelines.yml`
- `deployment/templates/build-and-push-container.yml`
- `deployment/templates/provision-container-webapp.yml`
- `deployment/README.md`

## Validation

- Run `~/.dotnet/dotnet build ./src/BudgetinCheck.Api/BudgetinCheck.Api.csproj --nologo -v minimal` after adding pipeline/docs.
- Validate Dockerfile syntax with a local Docker build if Docker is available.
- Validate the YAML shape by inspection because Azure DevOps pipeline execution requires the remote service connection and variable groups.
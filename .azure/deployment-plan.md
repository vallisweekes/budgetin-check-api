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
- Separate resource groups for shared container registry, non-prod, and prod.
- Separate App Service instances for non-prod and prod.
- Separate App Service Plans for non-prod and prod.
- Application settings configured per environment in Azure, not committed to source.

## Resource Group Requirements

Shared:

- Resource group: `rg-budgetin-check-api-shared`
- Azure Container Registry: `crbudgetincheckapi`
- Registry SKU: `Basic`

Non-prod:

- Resource group: `rg-budgetin-check-api-nonprod`
- App Service: `app-budgetin-check-api-nonprod`
- App Service Plan: `asp-budgetin-check-api-nonprod`
- Runtime stack: Linux custom container
- Location: `uksouth`
- SKU: `B1`

Prod:

- Resource group: `rg-budgetin-check-api-prod`
- App Service: `app-budgetin-check-api-prod`
- App Service Plan: `asp-budgetin-check-api-prod`
- Runtime stack: Linux custom container
- Location: `uksouth`
- SKU: `B1`

Shared pipeline requirements:

- Azure DevOps service connection: `sc-budgetin-check-api-azure`
- Non-prod variable group: `budgetin-check-api-nonprod`
- Prod variable group: `budgetin-check-api-prod`
- Non-prod environment: `budgetin-check-api-nonprod`
- Prod environment: `budgetin-check-api-prod`
- If the pipeline creates resource groups, the service connection needs subscription-level `Contributor`.
- The service connection also needs `User Access Administrator` on the subscription or ACR scope to grant `AcrPull` to Web App managed identities.
- If the resource groups are created manually, the service connection needs `Contributor` on all three resource groups plus `User Access Administrator` on the ACR scope.

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
- Service connection name: `sc-budgetin-check-api-azure`.
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
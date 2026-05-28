# Azure DevOps Docker Deployment

This repo uses [azure-pipelines.yml](azure-pipelines.yml) for Docker-based CI/CD to Azure App Service for Containers.

## Pipeline Path

When creating the Azure DevOps pipeline, select this YAML path:

```text
deployment/azure-pipelines.yml
```

## Branch Flow

- Pull requests into `dev` or `main` run validation only.
- Pushes to `dev` run validation, build the non-prod Docker image, and deploy non-prod.
- Pushes to `main` run validation, build the prod Docker image, and deploy prod after the prod environment approval.
- The Dockerfile used by the pipeline is [../src/BudgetinCheck.Api/Dockerfile](../src/BudgetinCheck.Api/Dockerfile).

## Azure DevOps Names

Use these names so the YAML works without edits:

- Service connection: `sc-budgetin-check-api-azure`
- Non-prod variable group: `budgetin-check-api-nonprod`
- Prod variable group: `budgetin-check-api-prod`
- Non-prod environment: `budgetin-check-api-nonprod`
- Prod environment: `budgetin-check-api-prod`

## Azure Resources The Pipeline Creates

Your Azure DevOps service connection is scoped to this existing resource group:

- Resource group: `DefaultResourceGroup-SUK`

The pipeline creates all app resources inside that resource group.

Shared container registry:

- Resource group: `DefaultResourceGroup-SUK`
- Azure Container Registry: `crbudgetincheckapi`
- SKU: `Basic`

Non-prod:

- Resource group: `DefaultResourceGroup-SUK`
- App Service plan: `asp-budgetin-check-api-nonprod`
- Web App for Containers: `app-budgetin-check-api-nonprod`
- Location: `uksouth`
- SKU: `B1`

Prod:

- Resource group: `DefaultResourceGroup-SUK`
- App Service plan: `asp-budgetin-check-api-prod`
- Web App for Containers: `app-budgetin-check-api-prod`
- Location: `uksouth`
- SKU: `B1`

ACR names are globally unique. If `crbudgetincheckapi` is unavailable in Azure, change `containerRegistryName` and `containerRegistryLoginServer` in [azure-pipelines.yml](azure-pipelines.yml).

## Service Connection Permissions

Recommended setup:

1. In Azure DevOps, open Project settings > Service connections.
2. Create an Azure Resource Manager service connection using workload identity federation.
3. Name it `sc-budgetin-check-api-azure`.
4. Grant access permission to all pipelines.
5. Give the service principal `Contributor` on `DefaultResourceGroup-SUK` so it can create ACR, App Service plans, and Web Apps.
6. Give the service principal `User Access Administrator` on `DefaultResourceGroup-SUK` so it can assign `AcrPull` to each Web App managed identity.

The service principal needs access on:

- `DefaultResourceGroup-SUK`

It still needs `User Access Administrator` on the resource group or ACR scope to create the managed identity pull permission.

## Variable Groups

Create both variable groups in Azure DevOps Pipelines > Library and authorize them for this pipeline.

Each variable group needs these variables:

| Variable | Secret | Notes |
| --- | --- | --- |
| `BudgetDbConnectionString` | Yes | PostgreSQL connection string for `ConnectionStrings__BudgetDb`. |
| `LegacyNextJsBaseUrl` | No | Legacy BFF base URL used by the migration proxy and auth bridge. |
| `LegacyNextJsTimeoutSeconds` | No | Use `100` unless you need a different timeout. |
| `BudgetDataSpendingDataRoot` | No | Path for legacy spending JSON data. Use an empty value if not needed. |

## Production Approval

Create the `budgetin-check-api-prod` environment in Azure DevOps and add an approval check. The pipeline deploys prod only after non-prod deployment succeeds, the prod container image is built, and the environment approval is granted.

## Pipeline Flow

1. `Validate` restores and builds the .NET solution.
2. On `dev`, `Build_NonProd` creates the shared ACR if needed, builds the Docker image, and pushes `nonprod-$(Build.BuildId)` plus `nonprod-latest`.
3. On `dev`, `Deploy_NonProd` creates or updates the non-prod Web App for Containers and deploys the non-prod image.
4. On `main`, `Build_Prod` builds the prod Docker image from the same commit and pushes `prod-$(Build.BuildId)` plus `prod-latest`.
5. On `main`, `Deploy_Prod` creates or updates the prod Web App for Containers and deploys the prod image after environment approval.
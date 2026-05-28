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

- Non-prod service connection: `sc-budgetin-check-api-azure-dev`
- Prod service connection: `sc-budgetin-check-api-azure-prod`
- Non-prod environment: `budgetin-check-api-nonprod`
- Prod environment: `budgetin-check-api-prod`

## Azure Resources The Pipeline Creates

This deployment uses separate Azure subscriptions for non-prod and prod.

Non-prod subscription:

- Subscription: `VW-online-DEV`
- Subscription ID: `4800dc47-952e-4b26-b167-fd8c671101e8`
- Resource group: `rg-vw-budgetapp-api-dev-uks-001`

Prod subscription:

- Subscription: `VW-online-PRD`
- Subscription ID: `366cfc3b-1e2c-4708-a880-35380b4202ba`
- Resource group: `rg-vw-budgetapp-api-prd-uks-001`

The pipeline creates app resources inside each environment's API resource group.

Non-prod container registry:

- Resource group: `rg-vw-budgetapp-api-dev-uks-001`
- Azure Container Registry: `crvwbudgetappapidevuks001`
- SKU: `Basic`

Non-prod:

- Resource group: `rg-vw-budgetapp-api-dev-uks-001`
- App Service plan: `asp-budgetin-check-api-nonprod`
- Web App for Containers: `app-budgetin-check-api-nonprod`
- Location: `uksouth`
- SKU: `B1`

Prod container registry:

- Resource group: `rg-vw-budgetapp-api-prd-uks-001`
- Azure Container Registry: `crvwbudgetappapiprduks001`
- SKU: `Basic`

Prod:

- Resource group: `rg-vw-budgetapp-api-prd-uks-001`
- App Service plan: `asp-budgetin-check-api-prod`
- Web App for Containers: `app-budgetin-check-api-prod`
- Location: `uksouth`
- SKU: `B1`

ACR names are globally unique. If either registry name is unavailable in Azure, change the matching registry name and login server variables in [azure-pipelines.yml](azure-pipelines.yml).

## Service Connection Permissions

Recommended setup:

1. In Azure DevOps, open Project settings > Service connections.
2. Create an Azure Resource Manager service connection using workload identity federation.
3. Create one connection for `VW-online-DEV` named `sc-budgetin-check-api-azure-dev` scoped to `rg-vw-budgetapp-api-dev-uks-001`.
4. Create one connection for `VW-online-PRD` named `sc-budgetin-check-api-azure-prod` scoped to `rg-vw-budgetapp-api-prd-uks-001`.
5. Grant access permission to all pipelines on both connections.
6. Give each service principal `Contributor` on its resource group so it can create ACR, App Service plans, and Web Apps.
7. Give each service principal `User Access Administrator` on its resource group so it can assign `AcrPull` to each Web App managed identity.

The non-prod service principal needs access on:

- `rg-vw-budgetapp-api-dev-uks-001`

The prod service principal needs access on:

- `rg-vw-budgetapp-api-prd-uks-001`

It still needs `User Access Administrator` on the resource group or ACR scope to create the managed identity pull permission.

## Required Azure Resource Providers

Before the first deployment, register these providers on the subscription:

```text
Microsoft.ContainerRegistry
Microsoft.Web
Microsoft.ManagedIdentity
```

In Azure Portal, open Subscriptions > your subscription > Resource providers, search each provider, and select Register.

With Azure CLI, a subscription owner or contributor can run:

```bash
az provider register --namespace Microsoft.ContainerRegistry
az provider register --namespace Microsoft.Web
az provider register --namespace Microsoft.ManagedIdentity
```

Registration can take a few minutes. The pipeline includes preflight checks that stop with a clear message if a provider is still not registered.

## Pipeline Variables

The YAML includes empty defaults so the pipeline can compile before secrets are added. Add these variables in Azure DevOps from the pipeline page: Edit > Variables.

Non-prod variables:

| Variable | Secret | Notes |
| --- | --- | --- |
| `nonProdBudgetDbConnectionString` | Yes | PostgreSQL connection string for `ConnectionStrings__BudgetDb`. |
| `nonProdLegacyNextJsBaseUrl` | No | Legacy BFF base URL used by the migration proxy and auth bridge. |
| `nonProdLegacyNextJsTimeoutSeconds` | No | Use `100` unless you need a different timeout. |
| `nonProdBudgetDataSpendingDataRoot` | No | Path for legacy spending JSON data. Use an empty value if not needed. |

Prod variables:

| Variable | Secret | Notes |
| --- | --- | --- |
| `prodBudgetDbConnectionString` | Yes | PostgreSQL connection string for `ConnectionStrings__BudgetDb`. |
| `prodLegacyNextJsBaseUrl` | No | Legacy BFF base URL used by the migration proxy and auth bridge. |
| `prodLegacyNextJsTimeoutSeconds` | No | Use `100` unless you need a different timeout. |
| `prodBudgetDataSpendingDataRoot` | No | Path for legacy spending JSON data. Use an empty value if not needed. |

Variable groups are optional. If you prefer them later, create groups and re-add them to the deploy stages, but they are not required for this pipeline to be valid.

## Production Approval

Create the `budgetin-check-api-prod` environment in Azure DevOps and add an approval check. The pipeline deploys prod only after non-prod deployment succeeds, the prod container image is built, and the environment approval is granted.

## Pipeline Flow

1. `Validate` restores and builds the .NET solution.
2. On `dev`, `Build_NonProd` creates the shared ACR if needed, builds the Docker image, and pushes `nonprod-$(Build.BuildId)` plus `nonprod-latest`.
3. On `dev`, `Deploy_NonProd` creates or updates the non-prod Web App for Containers and deploys the non-prod image.
4. On `main`, `Build_Prod` builds the prod Docker image from the same commit and pushes `prod-$(Build.BuildId)` plus `prod-latest`.
5. On `main`, `Deploy_Prod` creates or updates the prod Web App for Containers and deploys the prod image after environment approval.
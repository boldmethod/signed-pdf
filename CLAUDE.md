# signed-pdf

## Project Overview
.NET 10 Web API service for PDF signing. Open source (AGPL-3.0).

## Architecture
- **Runtime:** .NET 10, ASP.NET Core minimal APIs
- **Deployment:** AWS ECS Fargate (ARM64) via GitHub Actions
- **Container:** `dotnet publish` with SDK container support, port 8080

## Project Structure
```
src/SignedPdf/          # Main API project
infrastructure/ecs/     # ECS task definitions and environment configs
.github/workflows/      # CI/CD pipelines
```

## CI/CD
- `deploy-to-ecs.yml` — Deploys on push to `development` branch
- `promote-to-prod.yml` — Manual promotion via workflow_dispatch
- All AWS identifiers (account IDs, ARNs, ECR registries, cluster/service names) are stored in GitHub Secrets/Variables — never hardcoded
- OIDC-based AWS auth (no long-lived credentials)

## Required GitHub Secrets
- `AWS_OIDC_ROLE_ARN` — IAM role ARN for OIDC federation

## Required GitHub Variables
- `AWS_REGION`, `AWS_ECR_REGISTRY`, `ECR_REPOSITORY`
- `ECS_TASK_FAMILY`, `PROD_TAG_PREFIX`
- `ECS_CLUSTER_DEV`, `ECS_SERVICE_DEV` (development environment)
- `ECS_CLUSTER_PROD`, `ECS_SERVICE_PROD` (production environment)

## Build & Run
```bash
dotnet restore src/SignedPdf/SignedPdf.csproj
dotnet build src/SignedPdf/SignedPdf.csproj
dotnet run --project src/SignedPdf
```

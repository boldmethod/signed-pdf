# signed-pdf

## Project Overview
.NET 10 Web API service for PDF signing. Open source (AGPL-3.0).

## Architecture
- **Runtime:** .NET 10, ASP.NET Core minimal APIs
- **PDF Library:** iText 9 (AGPL-3.0) — overlay rendering and signature appearance fields
- **Storage:** AWS S3 with presigned URLs for consumer retrieval
- **Deployment:** AWS ECS Fargate (ARM64) via GitHub Actions
- **Container:** `dotnet publish` with SDK container support, port 8080

## API
- `POST /api/sign` — accepts base64 PDF + overlay instructions, returns presigned S3 download URL
- `GET /health` — health check
- `GET /` — service status

## Project Structure
```
src/SignedPdf/
  Program.cs              # Entry point, DI, endpoint
  Models/                 # SignPdfRequest, SignPdfResponse
  Services/               # IPdfRenderer (iText), IS3Storage
  Configuration/          # ServiceConfiguration loader
infrastructure/ecs/       # ECS task definitions and environment configs
.github/workflows/        # CI/CD pipelines
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

## Environment Variables
- `AWS_REGION` — AWS region (set via ECS env config)
- `S3_BUCKET` — S3 bucket for signed PDFs (required)
- `S3_KEY_PREFIX` — Object key prefix (default: `signed-pdfs/`)
- `PRESIGNED_URL_TTL_MINUTES` — Download URL lifetime in minutes (default: `60`)

## Build & Run
```bash
dotnet restore src/SignedPdf/SignedPdf.csproj
dotnet build src/SignedPdf/SignedPdf.csproj
dotnet run --project src/SignedPdf
```

## ECS Task Role Permissions
The task role needs `s3:PutObject` and `s3:GetObject` on the configured S3 bucket/prefix.

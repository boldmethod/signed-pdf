# signed-pdf

## Project Overview
.NET 10 Web API service for PDF signing. Open source (AGPL-3.0).

## Architecture
- **Runtime:** .NET 10, ASP.NET Core minimal APIs
- **PDF Library:** iText 9 + iText pdfHTML 6 (AGPL-3.0) — HTML→PDF/A-3B conversion, deferred PAdES signing, associated file embedding, overlay rendering
- **Storage:** AWS S3 with presigned URLs for consumer retrieval (legacy `/api/sign` endpoint)
- **Deployment:** AWS ECS Fargate (ARM64) via GitHub Actions
- **Container:** `dotnet publish` with SDK container support, port 8080
- **Bundled assets:** Liberation Sans/Serif/Mono fonts (SIL OFL) and sRGB ICC profile in `src/SignedPdf/Resources/`

## API

### Legacy overlay endpoint (unauthenticated)
- `POST /api/sign` — accepts base64 PDF + overlay instructions, composites them onto the document, returns presigned S3 download URL.

### PAdES PDF/A-3 export (two-call, X-Service-Token required)
- `POST /api/render-signed/prepare` — accepts HTML + cert + attachments + visible block. Renders HTML→PDF/A-3B, embeds attachments as PDF/A-3 associated files, appends a visible signature block on a new page, reserves a PAdES signature placeholder, and returns the prepared PDF blob plus the digest the caller must ECDSA-sign.
- `POST /api/render-signed/finalize` — accepts the prepared PDF blob + the externally-computed CMS signature components. Builds the CMS SignedData (with optional RFC 3161 timestamp) and injects it into the reserved placeholder. Returns `application/pdf` bytes.

### System
- `GET /health` — health check
- `GET /` — service status
- `GET /openapi/v1.json` — OpenAPI 3.1 document

## Project Structure
```
src/SignedPdf/
  Program.cs              # Entry point, DI, endpoint mapping, BouncyCastle init
  Models/                 # Request/response records, ErrorResponse, AfRelationship
  Services/               # PAdES renderer + helpers, S3 storage, legacy iText overlay
  Configuration/          # ServiceConfiguration loader
  Endpoints/              # Static endpoint handlers (SignPdfEndpoint, RenderSignedEndpoint)
  Middleware/             # ServiceTokenAuthMiddleware
  Resources/Fonts/        # Liberation TTFs + LICENSE.OFL
  Resources/Profiles/     # sRGB2014.icc
infrastructure/ecs/       # ECS task definitions and environment configs
docs/openapi/             # Committed OpenAPI spec (verified by CI)
tools/openapi/            # generate.sh — build-time spec generator wrapper
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
- `S3_BUCKET` — S3 bucket for signed PDFs (required, used by `/api/sign`)
- `S3_KEY_PREFIX` — Object key prefix (default: `signed-pdfs/`)
- `PRESIGNED_URL_TTL_MINUTES` — Download URL lifetime in minutes (default: `60`)
- `PDF_API_SERVICE_TOKEN` — Shared secret required in the `X-Service-Token` header on `/api/render-signed/*` (required, injected from AWS Secrets Manager via the ECS task definition)

## Auth model
- `/health`, `/`, `/api/sign`, and `/openapi/v1.json` are unauthenticated.
- `/api/render-signed/prepare` and `/api/render-signed/finalize` require the `X-Service-Token` header. The middleware uses constant-time comparison and returns `401 Unauthorized` with an `ErrorResponse` body on missing/mismatched tokens.

## ECS env file convention
The `infrastructure/ecs/signed-pdf.env.{dev,prod}.json` files are used by both deploy workflows. Keys whose value is a string starting with `arn:aws:secretsmanager:` are emitted into the ECS task definition's `secrets:` array (resolved by ECS at task start via the execution role) instead of the plain `environment:` array. Everything else lands as a regular env var. This is how `PDF_API_SERVICE_TOKEN` is wired to AWS Secrets Manager. The execution role needs `secretsmanager:GetSecretValue` on the referenced secret ARNs.

## Build & Run
```bash
dotnet restore src/SignedPdf/SignedPdf.csproj
dotnet build src/SignedPdf/SignedPdf.csproj
dotnet run --project src/SignedPdf
```

## Regenerating the OpenAPI spec
After any change to endpoints, models, or XML doc comments, regenerate the committed spec via:
```bash
bash tools/openapi/generate.sh
```
The CI workflow `verify-openapi-spec.yml` rebuilds the spec on every PR and fails if the committed file is stale.

## ECS Task Role Permissions
The task role needs `s3:PutObject` and `s3:GetObject` on the configured S3 bucket/prefix (for the `/api/sign` overlay endpoint). The PAdES endpoints make no AWS calls — they're pure CPU/memory.

## Bundled fonts and ICC profile
- `src/SignedPdf/Resources/Fonts/Liberation*.ttf` — SIL Open Font License, see `LICENSE.OFL` in the same folder.
- `src/SignedPdf/Resources/Profiles/sRGB2014.icc` — public domain ICC profile from color.org, used as the PDF/A-3B output intent.

The Dockerfile copies the entire project tree, so these files travel through the publish step into the runtime container.

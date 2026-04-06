# signed-pdf

A .NET 10 service for PDF digital signing.

## Quick Start

```bash
dotnet run --project src/SignedPdf
```

The service starts on `http://localhost:5000` with a health check at `/health`.

## Deployment

This service deploys to AWS ECS Fargate (ARM64) via GitHub Actions. See [CLAUDE.md](CLAUDE.md) for CI/CD details and required secrets/variables configuration.

## License

[GNU Affero General Public License v3.0](LICENSE)

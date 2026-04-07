#!/usr/bin/env bash
set -euo pipefail

# Generate the OpenAPI spec for signed-pdf via build-time document generation.
#
# Usage:
#   bash tools/openapi/generate.sh
#
# How it works:
#   1. Run `dotnet build -p:GenerateSpec=true`. The csproj enables
#      Microsoft.Extensions.ApiDescription.Server when GenerateSpec is set,
#      which bootstraps the host after build and writes SignedPdf.json to
#      src/SignedPdf/bin/openapi/.
#   2. Read that file, inject info.x-spec-hash (sha256 of the canonical
#      sorted-key form), and write the result to docs/openapi/signed-pdf.json.
#
# Placeholder AWS env vars are exported because ServiceConfiguration.Load
# validates them at startup. The build-time generator never actually contacts
# AWS — it just enumerates endpoints and serializes the OpenAPI document.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PROJECT="$REPO_ROOT/src/SignedPdf/SignedPdf.csproj"
GENERATED_FILE="$REPO_ROOT/src/SignedPdf/bin/openapi/SignedPdf.json"
OUTPUT_DIR="$REPO_ROOT/docs/openapi"
OUTPUT_FILE="$OUTPUT_DIR/signed-pdf.json"

mkdir -p "$OUTPUT_DIR"

export ASPNETCORE_ENVIRONMENT=Development
export AWS_REGION="us-west-2"
export S3_BUCKET="openapi-generation-placeholder"
export S3_KEY_PREFIX="openapi/"
export PRESIGNED_URL_TTL_MINUTES="60"
export PDF_API_SERVICE_TOKEN="openapi-generation-placeholder"

echo "Building SignedPdf with -p:GenerateSpec=true..."
dotnet build "$PROJECT" -c Release -p:GenerateSpec=true --nologo -v quiet

if [[ ! -f "$GENERATED_FILE" ]]; then
  echo "ERROR: expected generated spec at $GENERATED_FILE but it was not produced." >&2
  exit 1
fi

echo "Normalizing and hashing spec..."
python3 - "$GENERATED_FILE" "$OUTPUT_FILE" <<'PY'
import hashlib
import json
import sys

generated_path, output_path = sys.argv[1], sys.argv[2]

with open(generated_path) as f:
    spec = json.load(f)

# Strip anything non-deterministic before hashing.
spec.pop("servers", None)
spec.get("info", {}).pop("x-spec-hash", None)

canonical = json.dumps(spec, indent=2, sort_keys=True) + "\n"
spec_hash = hashlib.sha256(canonical.encode("utf-8")).hexdigest()

spec.setdefault("info", {})["x-spec-hash"] = f"sha256:{spec_hash}"

with open(output_path, "w") as f:
    json.dump(spec, f, indent=2, sort_keys=True)
    f.write("\n")

print(f"  x-spec-hash: sha256:{spec_hash}")
PY

echo "OpenAPI spec written to: $OUTPUT_FILE"

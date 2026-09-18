#!/usr/bin/env bash
# Build the BudgetApp container image for linux/amd64 and push it to ACR.
# Tags: <short git sha> (or an explicit override: build-push.sh <tag>) and latest.
# Requires: docker (buildx), az login with push rights on the registry (admin
# user is disabled — ACR login uses the AAD identity).
set -euo pipefail

ACR_NAME="${ACR_NAME:-myacr}"
REGISTRY="${REGISTRY:-${ACR_NAME}.azurecr.io}"
REPOSITORY="${REPOSITORY:-budgetapp}"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GIT_SHA="$(git -C "$REPO_ROOT" rev-parse --short HEAD)"
TAG="${1:-$GIT_SHA}"
IMAGE="${REGISTRY}/${REPOSITORY}"

if [[ -n "$(git -C "$REPO_ROOT" status --porcelain)" ]]; then
  echo "WARNING: working tree is dirty — the image tagged '${TAG}' will include uncommitted changes." >&2
  if [[ "$TAG" == "$GIT_SHA" ]]; then
    echo "         Pass an explicit tag (build-push.sh <tag>) to avoid a misleading sha tag." >&2
  fi
fi

echo "==> az acr login ${ACR_NAME}"
az acr login --name "$ACR_NAME"

echo "==> docker buildx build linux/amd64 -> ${IMAGE}:${TAG} + :latest (push)"
docker buildx build \
  --platform linux/amd64 \
  --tag "${IMAGE}:${TAG}" \
  --tag "${IMAGE}:latest" \
  --push \
  "$REPO_ROOT"

echo "==> pushed ${IMAGE}:${TAG} and ${IMAGE}:latest"

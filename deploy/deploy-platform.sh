#!/usr/bin/env bash
# Deploy the BudgetApp Azure platform. Idempotent — safe to re-run.
# Creates: 2 subnets, 2 private DNS zones + VNet links,
# App Service plan (Linux B1) + web app (container, PE-only inbound),
# role assignments (AcrPull, Key Vault Secrets User), private endpoint.
# No other existing resource is modified.
#
# Usage: deploy/deploy-platform.sh [imageTag]
#        imageTag overrides the default in infra/main.bicepparam.

set -euo pipefail

RG="${AZURE_RESOURCE_GROUP:-my-resource-group}"
ACR_NAME="${ACR_NAME:-myacr}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
NAME="budgetapp-platform-$(date +%Y%m%d-%H%M%S)"

PARAMS=(--parameters "$ROOT/infra/main.bicepparam")
if [[ $# -ge 1 && -n "$1" ]]; then
  PARAMS+=(--parameters "imageTag=$1")
fi

# Does the web app already hold AcrPull? If so this is a re-run and the
# propagation wait + restart at the end can be skipped (a config change that
# needs a container recycle — e.g. a new image tag — restarts the site itself).
ACR_ID=$(az acr show -n "$ACR_NAME" --query id -o tsv)
EXISTING_PRINCIPAL=$(az webapp show -g "$RG" -n "${BUDGETAPP_APP_NAME:-budgetapp-app}" \
  --query 'identity.principalId' -o tsv 2>/dev/null || true)
ACRPULL_PRESENT=0
if [[ -n "$EXISTING_PRINCIPAL" ]]; then
  ACRPULL_PRESENT=$(az role assignment list --assignee "$EXISTING_PRINCIPAL" --scope "$ACR_ID" \
    --query "length([?roleDefinitionName=='AcrPull'])" -o tsv 2>/dev/null || echo 0)
fi

echo "==> az deployment group create ($NAME)"
az deployment group create -g "$RG" -n "$NAME" \
  --template-file "$ROOT/infra/main.bicep" "${PARAMS[@]}" \
  --query 'properties.outputs' -o jsonc

APP_NAME=$(az deployment group show -g "$RG" -n "$NAME" --query 'properties.outputs.appName.value' -o tsv)
PRINCIPAL_ID=$(az deployment group show -g "$RG" -n "$NAME" --query 'properties.outputs.principalId.value' -o tsv)
PE_NAME=$(az deployment group show -g "$RG" -n "$NAME" --query 'properties.outputs.peName.value' -o tsv)

echo "==> Enabling container stdout/stderr logging (for az webapp log tail)"
az webapp log config -g "$RG" -n "$APP_NAME" --docker-container-logging filesystem --only-show-errors >/dev/null

if [[ "$ACRPULL_PRESENT" == "1" ]]; then
  echo "==> AcrPull already assigned before this run — skipping propagation wait and restart."
else
  echo "==> Waiting for AcrPull on the web app identity to appear in ARM"
  for _ in $(seq 1 30); do
    COUNT=$(az role assignment list --assignee "$PRINCIPAL_ID" --scope "$ACR_ID" \
      --query "length([?roleDefinitionName=='AcrPull'])" -o tsv 2>/dev/null || echo 0)
    [[ "$COUNT" == "1" ]] && break
    sleep 10
  done

  # Propagation to the registry data plane lags behind ARM visibility, and the
  # app's first pull attempt has usually already failed by now — wait, restart.
  echo "==> Sleeping 120s for role propagation, then restarting $APP_NAME"
  sleep 120
  az webapp restart -g "$RG" -n "$APP_NAME"
fi

# Via the PE's NIC: customDnsConfigs is only populated while no DNS zone group
# manages the records, so it comes back empty on re-runs.
PE_NIC_ID=$(az network private-endpoint show -g "$RG" -n "$PE_NAME" \
  --query 'networkInterfaces[0].id' -o tsv)
PE_IP=$(az network nic show --ids "$PE_NIC_ID" \
  --query 'ipConfigurations[0].privateIPAddress' -o tsv)

cat <<EOF

==> Platform deployed. Private endpoint IP: $PE_IP

Gate check from the Mac (VPN up), after adding to /etc/hosts:
  $PE_IP $APP_NAME.azurewebsites.net
  $PE_IP $APP_NAME.scm.azurewebsites.net

  curl -s https://$APP_NAME.azurewebsites.net/healthz
  az webapp log tail -g $RG -n $APP_NAME
EOF

#!/usr/bin/env bash
# Upload the Cloudflare Origin CA certificate and key to Key Vault, then trigger
# fetch-origin-cert.sh on the LB VM so nginx switches from the temporary
# self-signed pair to the real one. Re-runnable.
#
# Usage: deploy/install-origin-cert.sh <origin-cert.pem> <origin-key.pem>

set -euo pipefail

RG="${AZURE_RESOURCE_GROUP:-my-resource-group}"
VAULT="${KEY_VAULT_NAME:-mykeyvault}"
VM="${LB_VM_NAME:-budgetapp-vm-lb-01}"
CERT_SECRET="${ORIGIN_CERT_SECRET:-cloudflare-origin-cert}"
KEY_SECRET="${ORIGIN_KEY_SECRET:-cloudflare-origin-key}"

[[ $# -eq 2 ]] || { echo "Usage: $0 <origin-cert.pem> <origin-key.pem>" >&2; exit 1; }
CERT_FILE="$1"
KEY_FILE="$2"

grep -q "BEGIN CERTIFICATE" "$CERT_FILE" || { echo "$CERT_FILE is not a PEM certificate" >&2; exit 1; }
grep -q "PRIVATE KEY" "$KEY_FILE" || { echo "$KEY_FILE is not a PEM private key" >&2; exit 1; }
openssl x509 -in "$CERT_FILE" -noout -checkend 86400 || { echo "$CERT_FILE is expired or unreadable" >&2; exit 1; }

echo "==> Uploading to $VAULT (values not shown)"
CERT_ID=$(az keyvault secret set --vault-name "$VAULT" --name "$CERT_SECRET" --file "$CERT_FILE" --query id -o tsv)
KEY_ID=$(az keyvault secret set --vault-name "$VAULT" --name "$KEY_SECRET" --file "$KEY_FILE" --query id -o tsv)
echo "    $CERT_ID"
echo "    $KEY_ID"

echo "==> Fetching onto $VM and reloading nginx"
az vm run-command invoke -g "$RG" -n "$VM" --command-id RunShellScript \
  --scripts "/usr/local/bin/fetch-origin-cert.sh" \
  --query 'value[0].message' -o tsv

echo "==> Done. Next: proxied A record budgetapp -> VM public IP, then Full (strict)."

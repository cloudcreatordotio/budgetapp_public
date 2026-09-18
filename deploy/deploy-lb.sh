#!/usr/bin/env bash
# Deploy the load-balancer VM. Idempotent — safe to re-run, and
# re-running is how the NSG's Cloudflare ranges get refreshed (they are fetched
# live from cloudflare.com/ips-v4 + /ips-v6 on every run; never hand-edit the
# rule). Creates: public IP, NSG, NIC (static 10.0.0.10), Ubuntu 24.04 VM with
# nginx via cloud-init, and the VM identity's Key Vault Secrets User role.
#
# Usage: deploy/deploy-lb.sh
#   BUDGETAPP_LB_SSH_KEY_FILE overrides the SSH public key (default ~/.ssh/id_rsa.pub).

set -euo pipefail

RG="${AZURE_RESOURCE_GROUP:-my-resource-group}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
NAME="budgetapp-lb-$(date +%Y%m%d-%H%M%S)"
SSH_KEY_FILE="${BUDGETAPP_LB_SSH_KEY_FILE:-$HOME/.ssh/id_rsa.pub}"

[[ -s "$SSH_KEY_FILE" ]] || { echo "SSH public key not found: $SSH_KEY_FILE" >&2; exit 1; }

echo "==> Fetching Cloudflare IP ranges"
V4=$(curl -fsS --max-time 30 https://www.cloudflare.com/ips-v4 | paste -sd, -)
V6=$(curl -fsS --max-time 30 https://www.cloudflare.com/ips-v6 | paste -sd, -)
[[ $(tr ',' '\n' <<<"$V4" | grep -c .) -ge 10 ]] || { echo "Cloudflare v4 fetch looks truncated: '$V4'" >&2; exit 1; }
[[ $(tr ',' '\n' <<<"$V6" | grep -c .) -ge 4 ]] || { echo "Cloudflare v6 fetch looks truncated: '$V6'" >&2; exit 1; }
echo "    v4: $V4"
echo "    v6: $V6"

# Consumed by infra/lb.bicepparam via readEnvironmentVariable.
export BUDGETAPP_CF_IPS_V4="$V4"
export BUDGETAPP_CF_IPS_V6="$V6"
BUDGETAPP_LB_SSH_PUBLIC_KEY="$(cat "$SSH_KEY_FILE")"
export BUDGETAPP_LB_SSH_PUBLIC_KEY

echo "==> az deployment group create ($NAME)"
az deployment group create -g "$RG" -n "$NAME" \
  --template-file "$ROOT/infra/lb.bicep" --parameters "$ROOT/infra/lb.bicepparam" \
  --query 'properties.outputs' -o jsonc

PUBLIC_IP=$(az deployment group show -g "$RG" -n "$NAME" --query 'properties.outputs.publicIp.value' -o tsv)
PRIVATE_IP=$(az deployment group show -g "$RG" -n "$NAME" --query 'properties.outputs.privateIp.value' -o tsv)

cat <<EOF

==> Load balancer deployed. Public IP: $PUBLIC_IP  Private IP: $PRIVATE_IP

Origin check from the Mac (VPN up; self-signed cert until the Origin CA cert
is installed, hence -k):
  ssh azureuser@$PRIVATE_IP cloud-init status --wait
  curl -k --resolve budgetapp.example.com:443:$PRIVATE_IP https://budgetapp.example.com/healthz

Cloudflare hand-off (in order):
  1. Create the Origin CA certificate for *.yourdomain.com, yourdomain.com; download PEM cert + key.
  2. deploy/install-origin-cert.sh <cert.pem> <key.pem>
  3. Add proxied A record: budgetapp -> $PUBLIC_IP (orange cloud).
  4. Zone: SSL/TLS Full (strict), Always Use HTTPS on, WebSockets on,
     no Rocket Loader / Auto Minify / email obfuscation.
EOF

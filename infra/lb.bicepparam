using 'lb.bicep'

// All three env vars are set by deploy/deploy-lb.sh on every run:
// the Cloudflare ranges from a live fetch of cloudflare.com/ips-v4 + /ips-v6
// (comma-joined), the SSH key from the operator's ~/.ssh. No defaults — a
// deploy without a fresh fetch must fail, never fall back to stale ranges.
param cfIpv4Ranges = split(readEnvironmentVariable('BUDGETAPP_CF_IPS_V4'), ',')
param cfIpv6Ranges = split(readEnvironmentVariable('BUDGETAPP_CF_IPS_V6'), ',')
param sshPublicKey = readEnvironmentVariable('BUDGETAPP_LB_SSH_PUBLIC_KEY')

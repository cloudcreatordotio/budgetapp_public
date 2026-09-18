// BudgetApp load balancer, deployed at resource-group scope by
// deploy/deploy-lb.sh — separate from main.bicep on purpose: the NSG's
// Cloudflare ranges must come from a live fetch on every deploy, and keeping
// the LB out of the platform template means a platform re-run can never write
// stale ranges. Idempotent, re-runnable.

param location string = resourceGroup().location

param vnetName string = 'budgetapp-vnet'
param subnetName string = 'default'
param privateIp string = '10.0.0.10'

param pipName string = 'budgetapp-pip-lb-01'
param nsgName string = 'budgetapp-nsg-lb-01'
param nicName string = 'budgetapp-nic-lb-01'
param vmName string = 'budgetapp-vm-lb-01'
param osDiskName string = 'budgetapp-osdisk-lb-01'
param vmSize string = 'Standard_B1s'
param adminUsername string = 'azureuser'

// No defaults: the deploy script must supply fresh Cloudflare ranges and the
// operator's SSH key (via lb.bicepparam readEnvironmentVariable).
param cfIpv4Ranges array
param cfIpv6Ranges array
param sshPublicKey string

param vpnSourcePrefixes array = ['10.0.0.4/32', '10.8.0.0/24']

param appFqdn string = 'budgetapp-app.azurewebsites.net'
param publicHost string = 'budgetapp.example.com'
param keyVaultName string = 'mykeyvault'
#disable-next-line no-hardcoded-env-urls
param keyVaultUri string = 'https://mykeyvault.vault.azure.net/'

module lb 'modules/lb.bicep' = {
  name: 'budgetapp-lb'
  params: {
    location: location
    vnetName: vnetName
    subnetName: subnetName
    privateIp: privateIp
    pipName: pipName
    nsgName: nsgName
    nicName: nicName
    vmName: vmName
    osDiskName: osDiskName
    vmSize: vmSize
    adminUsername: adminUsername
    sshPublicKey: sshPublicKey
    cfIpv4Ranges: cfIpv4Ranges
    cfIpv6Ranges: cfIpv6Ranges
    vpnSourcePrefixes: vpnSourcePrefixes
    appFqdn: appFqdn
    publicHost: publicHost
    keyVaultUri: keyVaultUri
  }
}

// Role assignment #3 — after the VM exists, on its managed identity.
module lbRole 'modules/lb-role.bicep' = {
  name: 'budgetapp-lb-role'
  params: {
    principalId: lb.outputs.principalId
    keyVaultName: keyVaultName
  }
}

output publicIp string = lb.outputs.publicIp
output privateIp string = lb.outputs.privateIp
output vmName string = lb.outputs.vmName
output principalId string = lb.outputs.principalId

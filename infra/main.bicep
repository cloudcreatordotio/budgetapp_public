// BudgetApp Azure platform: subnets → private DNS → plan/app +
// VNet integration → role assignments → private endpoint.
// Deployed at resource-group scope by deploy/deploy-platform.sh. Idempotent.

param location string = resourceGroup().location

param vnetName string = 'budgetapp-vnet'
param appsSubnetName string = 'budgetapp-snet-apps'
param appsSubnetPrefix string = '10.0.20.0/24'
param appsvcSubnetName string = 'budgetapp-snet-appsvc'
param appsvcSubnetPrefix string = '10.0.30.0/24'

param planName string = 'budgetapp-asp'
param planSku string = 'B1'
param appName string = 'budgetapp-app'
param peName string = 'budgetapp-pe'

param acrName string = 'myacr'
param acrLoginServer string = 'myacr.azurecr.io'
param imageRepository string = 'budgetapp'
param imageTag string = 'latest'

param keyVaultName string = 'mykeyvault'
#disable-next-line no-hardcoded-env-urls
param keyVaultUri string = 'https://mykeyvault.vault.azure.net/'
param keyVaultPrivateIp string = '10.0.10.5'

param githubClientId string
param allowedLogins string = 'your-github-username'
param knownProxies string = '10.0.0.10'
// App Service frontend (link-local) + VNet hops (PE NAT, nginx).
param knownNetworks string = '169.254.0.0/16,10.0.0.0/16'
param forwardLimit string = '4'

module network 'modules/network.bicep' = {
  name: 'budgetapp-network'
  params: {
    vnetName: vnetName
    appsSubnetName: appsSubnetName
    appsSubnetPrefix: appsSubnetPrefix
    appsvcSubnetName: appsvcSubnetName
    appsvcSubnetPrefix: appsvcSubnetPrefix
  }
}

module dns 'modules/dns.bicep' = {
  name: 'budgetapp-dns'
  params: {
    vnetName: vnetName
    keyVaultARecordName: keyVaultName
    keyVaultPrivateIp: keyVaultPrivateIp
  }
}

module appservice 'modules/appservice.bicep' = {
  name: 'budgetapp-appservice'
  params: {
    location: location
    planName: planName
    planSku: planSku
    appName: appName
    acrLoginServer: acrLoginServer
    imageRepository: imageRepository
    imageTag: imageTag
    appsvcSubnetId: network.outputs.appsvcSubnetId
    keyVaultUri: keyVaultUri
    githubClientId: githubClientId
    allowedLogins: allowedLogins
    knownProxies: knownProxies
    knownNetworks: knownNetworks
    forwardLimit: forwardLimit
  }
  // The vault DNS zone must exist before the app first boots, or its config
  // provider resolves the vault to the public IP and fails (Traps).
  dependsOn: [
    dns
  ]
}

module roles 'modules/roles.bicep' = {
  name: 'budgetapp-roles'
  params: {
    principalId: appservice.outputs.principalId
    acrName: acrName
    keyVaultName: keyVaultName
  }
}

module appPe 'modules/app-pe.bicep' = {
  name: 'budgetapp-app-pe'
  params: {
    location: location
    peName: peName
    appId: appservice.outputs.appId
    appsSubnetId: network.outputs.appsSubnetId
    sitesZoneId: dns.outputs.sitesZoneId
  }
  // Plan sequence: roles before PE.
  dependsOn: [
    roles
  ]
}

output appName string = appservice.outputs.appName
output defaultHostName string = appservice.outputs.defaultHostName
output principalId string = appservice.outputs.principalId
output peName string = appPe.outputs.peName

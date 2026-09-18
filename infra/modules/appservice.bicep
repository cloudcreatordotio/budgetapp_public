// App Service plan (Linux B1) + web app running the BudgetApp container from
// ACR via its system-assigned managed identity. Inbound is private-endpoint
// only (publicNetworkAccess Disabled); outbound goes through regional VNet
// integration so the vault PE and the SQL VM are reachable.

param location string
param planName string
param planSku string
param appName string
param acrLoginServer string
param imageRepository string
param imageTag string
param appsvcSubnetId string
param keyVaultUri string
param githubClientId string
param allowedLogins string
param knownProxies string
param knownNetworks string
param forwardLimit string

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: planName
  location: location
  kind: 'linux'
  sku: {
    name: planSku
  }
  properties: {
    reserved: true
  }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: location
  kind: 'app,linux,container'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    publicNetworkAccess: 'Disabled'
    virtualNetworkSubnetId: appsvcSubnetId
    siteConfig: {
      linuxFxVersion: 'DOCKER|${acrLoginServer}/${imageRepository}:${imageTag}'
      acrUseManagedIdentityCreds: true
      alwaysOn: true
      webSocketsEnabled: true
      healthCheckPath: '/healthz'
      vnetRouteAllEnabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        { name: 'WEBSITES_PORT', value: '8080' }
        { name: 'WEBSITE_VNET_ROUTE_ALL', value: '1' }
        { name: 'WEBSITES_ENABLE_APP_SERVICE_STORAGE', value: 'false' }
        { name: 'KeyVault__Uri', value: keyVaultUri }
        { name: 'Authentication__GitHub__ClientId', value: githubClientId }
        { name: 'Authentication__GitHub__AllowedLogins', value: allowedLogins }
        { name: 'Database__MigrateOnStartup', value: 'true' }
        { name: 'ForwardedHeaders__KnownProxies', value: knownProxies }
        // The container's TCP peer is the App Service frontend (link-local),
        // not nginx — KnownProxies alone would leave X-Forwarded-* untrusted
        // and OAuth redirects on the wrong host. KnownNetworks trusts the
        // frontend + VNet hops; ForwardLimit covers the whole chain.
        { name: 'ForwardedHeaders__KnownNetworks', value: knownNetworks }
        { name: 'ForwardedHeaders__ForwardLimit', value: forwardLimit }
        // Database__SeedSampleData intentionally absent → false (prod stays clean).
        // Authentication__GitHub__ClientSecret comes from the vault at runtime.
      ]
    }
  }
}

output appId string = app.id
output appName string = app.name
output principalId string = app.identity.principalId
output defaultHostName string = app.properties.defaultHostName

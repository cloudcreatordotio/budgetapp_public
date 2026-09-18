// Role assignments for the web app's system-assigned managed identity:
//   #1 AcrPull on the container registry (image pull — must exist before the
//      first pull or the app crash-loops; the deploy script restarts the app
//      after propagation)
//   #2 Key Vault Secrets User on the vault (config secrets at runtime)
// guid() names are deterministic, so re-runs are idempotent.

param principalId string
param acrName string
param keyVaultName string

var acrPullRoleDefId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
var kvSecretsUserRoleDefId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: acrName
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, principalId, acrPullRoleDefId)
  scope: acr
  properties: {
    roleDefinitionId: acrPullRoleDefId
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

resource kvSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, principalId, kvSecretsUserRoleDefId)
  scope: keyVault
  properties: {
    roleDefinitionId: kvSecretsUserRoleDefId
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

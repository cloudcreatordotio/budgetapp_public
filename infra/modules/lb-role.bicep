// Role assignment #3: Key Vault Secrets User for the LB VM's managed identity,
// so fetch-origin-cert.sh can read the Origin CA cert secrets through the
// vault's private endpoint. Separate module because a role-assignment name
// must be deployment-time-calculable — the principal id crosses a module
// boundary, same as roles.bicep in Session 5. Deterministic guid → idempotent.

param principalId string
param keyVaultName string

var kvSecretsUserRoleDefId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
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

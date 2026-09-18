// Adds the two BudgetApp subnets to the existing VNet.
// Subnets are child resources of the *existing* VNet — the VNet resource
// itself is never redeployed (that would drop subnets this template doesn't
// know about).

param vnetName string
param appsSubnetName string
param appsSubnetPrefix string
param appsvcSubnetName string
param appsvcSubnetPrefix string

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' existing = {
  name: vnetName
}

resource appsSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' = {
  parent: vnet
  name: appsSubnetName
  properties: {
    addressPrefix: appsSubnetPrefix
    privateEndpointNetworkPolicies: 'Disabled'
  }
}

resource appsvcSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-05-01' = {
  parent: vnet
  name: appsvcSubnetName
  properties: {
    addressPrefix: appsvcSubnetPrefix
    // Matches what the original deploy stored (older API default). Inert on a
    // delegated integration subnet, but pinned so re-runs are true no-ops.
    privateEndpointNetworkPolicies: 'Disabled'
    delegations: [
      {
        name: 'appsvc-delegation'
        properties: {
          serviceName: 'Microsoft.Web/serverFarms'
        }
      }
    ]
  }
  // Parallel subnet writes to one VNet fail with AnotherOperationInProgress —
  // serialize them.
  dependsOn: [
    appsSubnet
  ]
}

output appsSubnetId string = appsSubnet.id
output appsvcSubnetId string = appsvcSubnet.id

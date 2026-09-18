// Private DNS for everything inside the VNet:
//   privatelink.vaultcore.azure.net  — so VNet-integrated apps resolve the
//     vault to its existing private endpoint. That PE is read-only to us
//     (no zone group may be added), so the A record is created manually here.
//   privatelink.azurewebsites.net    — records for the web app's own private
//     endpoint are added automatically by the PE's zone group (app-pe.bicep).

param vnetName string
param keyVaultARecordName string
param keyVaultPrivateIp string

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' existing = {
  name: vnetName
}

resource vaultZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: 'privatelink.vaultcore.azure.net'
  location: 'global'
}

resource sitesZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: 'privatelink.azurewebsites.net'
  location: 'global'
}

resource vaultZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: vaultZone
  name: '${vnetName}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

resource sitesZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: sitesZone
  name: '${vnetName}-link'
  location: 'global'
  properties: {
    registrationEnabled: false
    virtualNetwork: {
      id: vnet.id
    }
  }
}

resource vaultARecord 'Microsoft.Network/privateDnsZones/A@2020-06-01' = {
  parent: vaultZone
  name: keyVaultARecordName
  properties: {
    ttl: 3600
    aRecords: [
      {
        ipv4Address: keyVaultPrivateIp
      }
    ]
  }
}

output sitesZoneId string = sitesZone.id

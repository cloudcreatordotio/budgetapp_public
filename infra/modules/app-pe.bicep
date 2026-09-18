// Private endpoint for the web app (groupId 'sites') in the apps subnet.
// The zone group auto-registers the A records for both <app> and <app>.scm
// in privatelink.azurewebsites.net.

param location string
param peName string
param appId string
param appsSubnetId string
param sitesZoneId string

resource pe 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: peName
  location: location
  properties: {
    subnet: {
      id: appsSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: peName
        properties: {
          privateLinkServiceId: appId
          groupIds: [
            'sites'
          ]
        }
      }
    ]
  }
}

resource peDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2024-05-01' = {
  parent: pe
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'privatelink-azurewebsites-net'
        properties: {
          privateDnsZoneId: sitesZoneId
        }
      }
    ]
  }
}

output peName string = pe.name

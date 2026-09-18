// Load-balancer VM (Session 6): public IP, NSG (80/443 from Cloudflare's
// published ranges only, 22 + web from the WireGuard client range for VPN
// testing), NIC static 10.0.0.10 in the existing default subnet, Ubuntu 24.04
// B1s VM with a system-assigned managed identity, nginx configured by
// cloud-init (deploy/cloud-init.yaml, rendered here with the app FQDN, public
// host, and vault URI). Role assignment #3 (Key Vault Secrets User for the VM
// identity) lives in lb-role.bicep — the assignment name needs the principal
// id across a module boundary.
//
// The Cloudflare ranges arrive as parameters and are fetched live by
// deploy/deploy-lb.sh on every run — never hand-edited (Traps).

param location string

param vnetName string
param subnetName string
param privateIp string

param pipName string
param nsgName string
param nicName string
param vmName string
param osDiskName string
param vmSize string
param adminUsername string
param sshPublicKey string

param cfIpv4Ranges array
param cfIpv6Ranges array
param vpnSourcePrefixes array

param appFqdn string
param publicHost string
param keyVaultUri string

var cloudInit = replace(replace(replace(
  loadTextContent('../../deploy/cloud-init.yaml'),
  '__APP_FQDN__', appFqdn),
  '__PUBLIC_HOST__', publicHost),
  '__VAULT_URI__', keyVaultUri)

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' existing = {
  name: vnetName

  resource subnet 'subnets' existing = {
    name: subnetName
  }
}

resource pip 'Microsoft.Network/publicIPAddresses@2024-05-01' = {
  name: pipName
  location: location
  sku: {
    name: 'Standard'
  }
  properties: {
    publicIPAllocationMethod: 'Static'
    publicIPAddressVersion: 'IPv4'
  }
}

resource nsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: nsgName
  location: location
  properties: {
    securityRules: [
      {
        // SSH plus direct web access for testing before the Cloudflare
        // record exists.
        name: 'Allow-WireGuard-Clients'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefixes: vpnSourcePrefixes
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRanges: ['22', '80', '443']
        }
      }
      {
        name: 'Allow-Web-Cloudflare-v4'
        properties: {
          priority: 200
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefixes: cfIpv4Ranges
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRanges: ['80', '443']
        }
      }
      {
        name: 'Allow-Web-Cloudflare-v6'
        properties: {
          priority: 210
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefixes: cfIpv6Ranges
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRanges: ['80', '443']
        }
      }
      {
        // Everything else is denied explicitly, including default-rule VNet traffic.
        name: 'Deny-All-Inbound'
        properties: {
          priority: 4096
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '*'
        }
      }
    ]
  }
}

resource nic 'Microsoft.Network/networkInterfaces@2024-05-01' = {
  name: nicName
  location: location
  properties: {
    networkSecurityGroup: {
      id: nsg.id
    }
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          subnet: {
            id: vnet::subnet.id
          }
          privateIPAllocationMethod: 'Static'
          privateIPAddress: privateIp
          publicIPAddress: {
            id: pip.id
          }
        }
      }
    ]
  }
}

resource vm 'Microsoft.Compute/virtualMachines@2024-07-01' = {
  name: vmName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    hardwareProfile: {
      vmSize: vmSize
    }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: 'ubuntu-24_04-lts'
        sku: 'server'
        version: 'latest'
      }
      osDisk: {
        name: osDiskName
        createOption: 'FromImage'
        diskSizeGB: 30
        managedDisk: {
          storageAccountType: 'StandardSSD_LRS'
        }
      }
    }
    osProfile: {
      computerName: vmName
      adminUsername: adminUsername
      customData: base64(cloudInit)
      linuxConfiguration: {
        disablePasswordAuthentication: true
        ssh: {
          publicKeys: [
            {
              path: '/home/${adminUsername}/.ssh/authorized_keys'
              keyData: sshPublicKey
            }
          ]
        }
      }
    }
    // TrustedLaunch security profile.
    securityProfile: {
      securityType: 'TrustedLaunch'
      uefiSettings: {
        secureBootEnabled: true
        vTpmEnabled: true
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: nic.id
        }
      ]
    }
    diagnosticsProfile: {
      bootDiagnostics: {
        enabled: true
      }
    }
  }
}

output publicIp string = pip.properties.ipAddress
output privateIp string = privateIp
output vmName string = vm.name
output principalId string = vm.identity.principalId

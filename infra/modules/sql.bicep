// Azure SQL: one logical server and one free-offer database per environment.
// - Entra-only authentication: no SQL logins or passwords exist. The migrator's managed identity
//   is the server admin, so the migrator connects as the schema owner (dbo).
// - The database uses the free offer (100,000 vCore-seconds, 32 GB a month) and auto-pauses
//   until next month at the limit instead of billing.

param serverName string
param databaseName string
param location string
@description('Name of the managed identity that administers the server.')
param adminName string
@description('Client (application) ID of that identity; Azure SQL uses it as the admin SID.')
param adminClientId string
param tags object

resource server 'Microsoft.Sql/servers@2023-08-01' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      principalType: 'Application'
      login: adminName
      sid: adminClientId
      tenantId: tenant().tenantId
    }
  }
}

// Container Apps have no fixed outbound address on the free consumption plan, so the server
// accepts connections from Azure services; Entra-only authentication is what keeps it closed.
// A private network would need paid resources.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: server
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5' // with capacity: 2, the form Azure stores (avoids what-if noise)
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368 // 32 GB, the free data allowance
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name

// One managed identity per workload, so the database can tell them apart:
// - the migrator is the SQL server's Entra admin, the schema owner;
// - the API gets only the app_data role, granted by the migrator.

param apiName string
param migratorName string
param location string
param tags object

resource api 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: apiName
  location: location
  tags: tags
}

resource migrator 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: migratorName
  location: location
  tags: tags
}

output apiId string = api.id
output apiName string = api.name
output apiClientId string = api.properties.clientId
output migratorId string = migrator.id
output migratorName string = migrator.name
output migratorClientId string = migrator.properties.clientId

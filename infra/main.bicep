// FinanceGoals: everything one environment (test or production) runs on, in its own resource group.
// Free tiers only; docs/free-tier.md lists the allowances and the limit behaviour. No Key Vault
// and no passwords: the API and the migrator sign in to Azure SQL with their own managed identities.
//
// Deploy: az deployment group create -g rg-financegoals-<env> -p infra/<env>.bicepparam

targetScope = 'resourceGroup'

@description('test or production.')
@allowed(['test', 'production'])
param environmentName string

@description('Region for everything except the Static Web App. In 2026-11 norwayeast, westeurope and northeurope refused new SQL servers for this subscription, so this is not the location of the resource group. Keep everything in one region; traffic between regions is billed.')
param location string = 'swedencentral'

@description('Static Web Apps (Free) is offered in a few regions only, and westeurope refused new customers in 2026-11. The region holds only the settings of the app; its content is served globally.')
@allowed(['westeurope', 'eastus2', 'centralus', 'westus2', 'eastasia'])
param staticWebAppLocation string = 'eastus2'

@description('API image. The placeholder runs until the first real deploy (#20, #21).')
param apiImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Migrator image. The placeholder is never run; the job only runs when triggered.')
param migratorImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Daily Log Analytics ingestion cap in GB. Two environments at 0.05 GB stay well under the free 5 GB a month.')
param logDailyCapGb string = '0.05'

var app = 'financegoals'
// Short form for resource names, matching the resource groups (rg-financegoals-test, -prod).
var env = environmentName == 'production' ? 'prod' : 'test'
// Must match the apiImage default; the API runs it on port 80 without health probes.
var placeholderImage = 'mcr.microsoft.com/k8se/quickstart:latest'
var tags = {
  app: app
  environment: environmentName
  'managed-by': 'infra/main.bicep'
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    name: 'log-${app}-${env}'
    location: location
    dailyCapGb: logDailyCapGb
    tags: tags
  }
}

module identities 'modules/identities.bicep' = {
  name: 'identities'
  params: {
    apiName: 'id-${app}-api-${env}'
    migratorName: 'id-${app}-migrator-${env}'
    location: location
    tags: tags
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    // The region is part of the suffix: a server name stays tied to its region in Azure for a while,
    // even after a failed create, so moving region needs a new name.
    serverName: 'sql-${app}-${env}-${uniqueString(resourceGroup().id, location)}'
    databaseName: '${app}-${env}'
    location: location
    adminName: identities.outputs.migratorName
    adminClientId: identities.outputs.migratorClientId
    tags: tags
  }
}

module containerApps 'modules/container-apps.bicep' = {
  name: 'container-apps'
  params: {
    environmentName: 'cae-${app}-${env}'
    apiName: 'ca-${app}-api-${env}'
    migratorJobName: 'caj-${app}-migrator-${env}'
    location: location
    logAnalyticsWorkspaceName: monitoring.outputs.name
    apiImage: apiImage
    migratorImage: migratorImage
    placeholderImage: placeholderImage
    aspNetCoreEnvironment: environmentName == 'production' ? 'Production' : 'Test'
    apiIdentityId: identities.outputs.apiId
    apiIdentityName: identities.outputs.apiName
    apiClientId: identities.outputs.apiClientId
    migratorIdentityId: identities.outputs.migratorId
    migratorClientId: identities.outputs.migratorClientId
    sqlServerFqdn: sql.outputs.serverFqdn
    webOrigin: web.outputs.url
    databaseName: sql.outputs.databaseName
    tags: tags
  }
}

module web 'modules/static-web-app.bicep' = {
  name: 'static-web-app'
  params: {
    name: 'stapp-${app}-${env}'
    location: staticWebAppLocation
    tags: tags
  }
}

output apiUrl string = containerApps.outputs.apiUrl
output webUrl string = web.outputs.url
output staticWebAppName string = web.outputs.name
output apiName string = containerApps.outputs.apiName
output sqlServerFqdn string = sql.outputs.serverFqdn
output databaseName string = sql.outputs.databaseName
output migratorJobName string = containerApps.outputs.migratorJobName
output apiIdentityName string = identities.outputs.apiName
output apiIdentityClientId string = identities.outputs.apiClientId

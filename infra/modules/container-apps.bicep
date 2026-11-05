// Container Apps on the free consumption plan: the API scales to zero, and the migrator is a job
// that runs only when triggered (#21 triggers it before each API deploy).

param environmentName string
param apiName string
param migratorJobName string
param location string
param logAnalyticsWorkspaceName string
param apiImage string
param migratorImage string
@description('Until #21 deploys real images, the API runs this placeholder, which listens on port 80 and has no health endpoints.')
param placeholderImage string
param aspNetCoreEnvironment string
param apiIdentityId string
param apiIdentityName string
param apiClientId string
param migratorIdentityId string
param migratorClientId string
param sqlServerFqdn string
param databaseName string
param tags object

var isPlaceholder = apiImage == placeholderImage
var apiPort = isPlaceholder ? 80 : 8080 // ASP.NET Core images listen on 8080

// Both sign in to Azure SQL with their own managed identity; there is no password anywhere.
var sqlConnection = 'Server=tcp:${sqlServerFqdn},1433;Database=${databaseName};Encrypt=True;Authentication=Active Directory Managed Identity'

// Probes call /health/live only, which never touches the database: probing /health/ready every
// few seconds would keep the free database awake and use up its monthly allowance in days.
// /health/ready is for the post-deploy smoke test (#21). See docs/free-tier.md.
var apiProbes = [
  {
    type: 'Liveness'
    httpGet: {
      path: '/health/live'
      port: apiPort
    }
    periodSeconds: 30
    failureThreshold: 3
  }
  {
    type: 'Readiness'
    httpGet: {
      path: '/health/live'
      port: apiPort
    }
    periodSeconds: 10
    failureThreshold: 3
  }
]

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: logAnalyticsWorkspaceName
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentName
  location: location
  tags: tags
  properties: {
    // No workloadProfiles: consumption only, which has the monthly free grant.
    zoneRedundant: false
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
  }
}

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: apiPort
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: aspNetCoreEnvironment }
            { name: 'ConnectionStrings__App', value: '${sqlConnection};User Id=${apiClientId}' }
          ]
          probes: isPlaceholder ? [] : apiProbes
        }
      ]
      scale: {
        minReplicas: 0 // scale to zero: nothing is used while idle
        maxReplicas: 1
      }
    }
  }
}

resource migrator 'Microsoft.App/jobs@2024-03-01' = {
  name: migratorJobName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${migratorIdentityId}': {}
    }
  }
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
    }
    template: {
      containers: [
        {
          name: 'migrator'
          image: migratorImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'ConnectionStrings__SchemaOwner', value: '${sqlConnection};User Id=${migratorClientId}' }
            { name: 'Migrator__AppUser', value: apiIdentityName }
            { name: 'Migrator__AppUserKind', value: 'entra' }
          ]
        }
      ]
    }
  }
}

output apiUrl string = 'https://${api.properties.configuration.ingress.fqdn}'
output migratorJobName string = migrator.name

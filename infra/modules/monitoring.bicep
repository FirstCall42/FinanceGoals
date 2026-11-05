// Log Analytics for the Container Apps environment. The first 5 GB of ingestion a month are free;
// the daily cap stops ingestion for the rest of the day instead of billing more.

param name string
param location string
@description('Daily ingestion cap in GB, as a string because Bicep has no decimal literals.')
param dailyCapGb string
param tags object

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    // 30 days is within the free 31 days of retention.
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: json(dailyCapGb)
    }
  }
}

output name string = workspace.name

// The Angular app on Static Web Apps Free: 100 GB of bandwidth a month, and no overage (traffic
// beyond it is refused, never billed). Not linked to the repository: #21 deploys the built app
// with a deployment token it reads at deploy time through its OIDC sign-in, so no token is stored.

param name string
param location string
param tags object

resource site 'Microsoft.Web/staticSites@2023-12-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    allowConfigFileUpdates: true
  }
}

output url string = 'https://${site.properties.defaultHostname}'

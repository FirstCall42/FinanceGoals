using './main.bicep'

param environmentName = 'production'

// The subscription allows one Container Apps environment per region, and test uses swedencentral.
// Production keeps everything in one region of its own.
param location = 'germanywestcentral'

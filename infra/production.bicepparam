using './main.bicep'

// NOT DEPLOYED. Production is deferred to #86; until then the test environment is the live one.
// On the Free Trial subscription the free SQL offer could only be created in swedencentral (Germany
// West Central and France Central failed), and swedencentral's one Container Apps environment is
// used by test. #86 picks the region after upgrading to Pay-As-You-Go and sets `location` here.
param environmentName = 'production'

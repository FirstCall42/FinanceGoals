using './main.bicep'

param environmentName = 'test'

// Google sign-in (#24): the OAuth client in the owner's Google Cloud project. Public, not a secret.
param googleClientId = '595594110205-6u9t88uhcmnlq3j3gq72grisc1qpocf4.apps.googleusercontent.com'

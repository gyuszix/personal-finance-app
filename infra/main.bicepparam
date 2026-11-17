// Parameters for infra/main.bicep. Secrets are read from environment
// variables at deploy time, so nothing secret is ever written to this file
// or the repo - see docs/DEPLOYMENT.md for how to set them.
using './main.bicep'

param namePrefix = 'pfapp'
param containerImage = readEnvironmentVariable('PF_IMAGE', 'ghcr.io/OWNER/personalfinance-api:0.3.0')
param plaidEnvironment = 'sandbox'
param encryptionKeyId = 'k1'

param postgresAdminPassword = readEnvironmentVariable('PF_POSTGRES_ADMIN_PASSWORD')
param appRolePassword = readEnvironmentVariable('PF_APP_ROLE_PASSWORD')
param jwtKey = readEnvironmentVariable('PF_JWT_KEY')
param plaidClientId = readEnvironmentVariable('PF_PLAID_CLIENT_ID')
param plaidSecret = readEnvironmentVariable('PF_PLAID_SECRET')
param encryptionKey = readEnvironmentVariable('PF_ENCRYPTION_KEY')

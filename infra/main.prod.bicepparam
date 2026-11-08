using './main.bicep'

// Production/demo environment. Deploy with:
//   POSTGRES_ADMIN_PASSWORD=... JWT_SIGNING_KEY=... \
//   az deployment sub create --name solarops-prod --location westeurope \
//     --parameters infra/main.prod.bicepparam
//
// Before the final demo set apiMinReplicas = 1 to avoid the first-request cold start,
// then return it to 0 to save the student credit.

param environment = 'prod'
param location = 'westeurope'
param namePrefix = 'solarops'

param postgresAdminLogin = 'solaropsadmin'
param postgresAdminPassword = readEnvironmentVariable('POSTGRES_ADMIN_PASSWORD')
param postgresBackupRetentionDays = 14

param jwtSigningKey = readEnvironmentVariable('JWT_SIGNING_KEY')

// Placeholders until the deploy workflows (#161, #164) publish the real images.
param apiImage = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
param mlImage = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

param apiMinReplicas = 0
param apiMaxReplicas = 5
param mlMinReplicas = 0
param mlMaxReplicas = 5

param logRetentionDays = 30

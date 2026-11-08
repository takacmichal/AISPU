using './main.bicep'

// Development environment. Deploy with:
//   POSTGRES_ADMIN_PASSWORD=... JWT_SIGNING_KEY=... \
//   az deployment sub create --name solarops-dev --location westeurope \
//     --parameters infra/main.dev.bicepparam

param environment = 'dev'
param location = 'westeurope'
param namePrefix = 'solarops'

param postgresAdminLogin = 'solaropsadmin'
param postgresAdminPassword = readEnvironmentVariable('POSTGRES_ADMIN_PASSWORD')
param postgresBackupRetentionDays = 7

param jwtSigningKey = readEnvironmentVariable('JWT_SIGNING_KEY')

// Placeholders until the deploy workflows (#161, #164) publish the real images.
param apiImage = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'
param mlImage = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

param apiMinReplicas = 0
param apiMaxReplicas = 3
param mlMinReplicas = 0
param mlMaxReplicas = 3

param logRetentionDays = 30

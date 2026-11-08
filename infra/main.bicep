targetScope = 'subscription'

@description('Deployment environment. Also used in resource names and tags.')
@allowed([
  'dev'
  'prod'
])
param environment string

@description('Azure region for every resource. West Europe is the region used in the cost estimate (ADR #215).')
param location string = 'westeurope'

@description('Prefix for resource names.')
@minLength(3)
@maxLength(20)
param namePrefix string = 'solarops'

@description('Suffix that keeps globally unique names (PostgreSQL, Static Web Apps) unique. Defaults to a hash of the subscription and environment.')
@minLength(3)
@maxLength(20)
param nameSuffix string = uniqueString(subscription().subscriptionId, environment)

@description('PostgreSQL administrator login. The application uses the same login until a read-only ML user exists.')
@minLength(1)
@maxLength(63)
param postgresAdminLogin string = 'solaropsadmin'

@description('PostgreSQL administrator password. Supply it through POSTGRES_ADMIN_PASSWORD, never commit it.')
@secure()
@minLength(16)
param postgresAdminPassword string

@description('Backup retention in days. Dev keeps the minimum, prod keeps two weeks.')
@minValue(1)
@maxValue(35)
param postgresBackupRetentionDays int = 7

@description('JWT signing key (at least 32 bytes). Supply it through JWT_SIGNING_KEY, never commit it.')
@secure()
@minLength(32)
param jwtSigningKey string

@description('API container image. Placeholder until the API deploy workflow (#161) pushes the real image.')
param apiImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('ML container image. Placeholder until the ML deploy workflow (#164) pushes the real image.')
param mlImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Minimum API replicas. 0 uses scale-to-zero; set 1 before a demo to avoid a cold start.')
@minValue(0)
@maxValue(10)
param apiMinReplicas int = 0

@description('Maximum API replicas.')
@minValue(1)
@maxValue(30)
param apiMaxReplicas int = 3

@description('Minimum ML replicas. 0 keeps the ML service stopped until the API calls it.')
@minValue(0)
@maxValue(10)
param mlMinReplicas int = 0

@description('Maximum ML replicas.')
@minValue(1)
@maxValue(30)
param mlMaxReplicas int = 3

@description('Log Analytics retention in days. Keep it short to stay inside the student budget.')
@minValue(30)
@maxValue(730)
param logRetentionDays int = 30

var tags = {
  project: 'SolarOps'
  environment: environment
  managedBy: 'Bicep'
  owner: 'solarops-team'
  workload: 'solarops'
  costCenter: 'azure-for-students'
}

var resourceGroupName = 'rg-${namePrefix}-${environment}'

resource rg 'Microsoft.Resources/resourceGroups@2022-09-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module resources './resources.bicep' = {
  name: 'solarops-${environment}-resources'
  scope: resourceGroup(rg.name)
  params: {
    environment: environment
    location: location
    namePrefix: namePrefix
    nameSuffix: nameSuffix
    tags: tags
    postgresAdminLogin: postgresAdminLogin
    postgresAdminPassword: postgresAdminPassword
    postgresBackupRetentionDays: postgresBackupRetentionDays
    jwtSigningKey: jwtSigningKey
    apiImage: apiImage
    mlImage: mlImage
    apiMinReplicas: apiMinReplicas
    apiMaxReplicas: apiMaxReplicas
    mlMinReplicas: mlMinReplicas
    mlMaxReplicas: mlMaxReplicas
    logRetentionDays: logRetentionDays
  }
}

@description('Name of the created resource group.')
output resourceGroupName string = rg.name

@description('Public HTTPS URL of the API.')
output apiUrl string = 'https://${resources.outputs.apiFqdn}'

@description('Internal FQDN of the ML service (only reachable from the Container Apps environment).')
output mlFqdn string = resources.outputs.mlFqdn

@description('Default hostname of the Static Web App (the frontend URL).')
output staticWebAppHostname string = resources.outputs.staticWebAppHostname

@description('Static Web App resource name (for fetching the deployment token).')
output staticWebAppName string = resources.outputs.staticWebAppName

@description('PostgreSQL server FQDN. Do not use it directly from the internet; the API connects with SSL.')
output postgresFqdn string = resources.outputs.postgresFqdn

@description('PostgreSQL server resource name.')
output postgresName string = resources.outputs.postgresName

@description('Application database name.')
output databaseName string = resources.outputs.databaseName

@description('Container Apps environment name.')
output containerAppsEnvironmentName string = resources.outputs.containerAppsEnvironmentName

@description('Log Analytics workspace resource ID.')
output logAnalyticsWorkspaceId string = resources.outputs.logAnalyticsWorkspaceId

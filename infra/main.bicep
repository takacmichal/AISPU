// Azure environment of SolarOps (#157; ADR #211, #213, #215 and #277).
// Deploy with infra/deploy.sh; parameters come from main.bicepparam, secrets from environment variables, never from git.
targetScope = 'resourceGroup'

@description('Azure region. The Azure for Students subscription allows only a few regions (ADR #277).')
param location string = resourceGroup().location

@description('Prefix of all resource names.')
@minLength(3)
@maxLength(12)
param prefix string = 'solarops'

@description('API image. The jobs run the same image with `--job <name>`.')
param apiImage string

@description('ML service image.')
param mlImage string

@description('Frontend image. Until the first frontend build any nginx serving on port 8080 works as a placeholder.')
param webImage string

@description('Minimum replicas of the API and the frontend: 0 scales to zero, 1 avoids cold starts (before the defence).')
@minValue(0)
@maxValue(1)
param minReplicas int = 0

@secure()
@description('Password of the PostgreSQL administrator. Must not contain ";" because it is part of the connection string.')
param postgresAdminPassword string

@description('Region of PostgreSQL. Azure for Students gets SkuNotAvailable for Burstable servers in some regions (main.bicepparam).')
param postgresLocation string = location

@secure()
@description('JWT signing key, at least 32 bytes.')
param jwtSigningKey string

@secure()
@description('Password of the first administrator admin@solarops.local (ADR #277).')
param adminPassword string

@description('GitHub repository (owner/name) whose CD pipeline may sign in to Azure through OIDC (#161).')
param githubRepository string = 'takacmichal/AISPU'

@description('Container registry of the images.')
param registryServer string = 'ghcr.io'

@description('Registry user; empty for public images.')
param registryUsername string = ''

@secure()
@description('Registry password or token; empty for public images.')
param registryPassword string = ''

var suffix = take(uniqueString(resourceGroup().id), 8)
var postgresLogin = 'solarops'
var databaseName = 'solarops'
var names = {
  logs: '${prefix}-logs'
  identity: '${prefix}-identity'
  deployIdentity: '${prefix}-deploy'
  keyVault: '${prefix}-kv-${suffix}'
  postgres: '${prefix}-db-${postgresLocation}-${suffix}'
  environment: '${prefix}-env'
  api: '${prefix}-api'
  ml: '${prefix}-ml'
  web: '${prefix}-web'
  migrateJob: '${prefix}-migrate'
  mlJob: '${prefix}-ml-analysis'
  weatherJob: '${prefix}-weather'
}

// ---------------------------------------------------------------- logs, identity, secrets

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: names.logs
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    // Cost cap: ingestion stops for the day after 1 GB.
    workspaceCapping: { dailyQuotaGb: 1 }
  }
}

// Identity of the container apps and jobs, used to read the Key Vault secrets.
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: names.identity
  location: location
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: names.keyVault
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

var keyVaultSecretsUserRole = '4633458b-17de-408a-b874-0445c86b69e6'

resource secretsReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, identity.id, keyVaultSecretsUserRole)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRole)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------- CD pipeline (#161, #162)

// GitHub Actions signs in as this identity through OIDC, so no Azure password is stored in GitHub.
// Only jobs of the `production` environment of the repository get a token for it.
resource deployIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: names.deployIdentity
  location: location
}

resource githubCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: deployIdentity
  name: 'github-production'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubRepository}:environment:production'
    audiences: ['api://AzureADTokenExchange']
  }
}

// Contributor on this resource group only: update images and start the migration job.
var contributorRole = 'b24988ac-6180-42a0-ab88-20f7382dd24c'

resource deployContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deployIdentity.id, contributorRole)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', contributorRole)
    principalId: deployIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------- database (ADR #213)

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: names.postgres
  location: postgresLocation
  sku: { name: 'Standard_B1ms', tier: 'Burstable' }
  properties: {
    version: '16'
    administratorLogin: postgresLogin
    administratorLoginPassword: postgresAdminPassword
    storage: { storageSizeGB: 32, autoGrow: 'Disabled' }
    backup: { backupRetentionDays: 7, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
    network: { publicNetworkAccess: 'Enabled' }
    authConfig: { activeDirectoryAuth: 'Disabled', passwordAuth: 'Enabled' }
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: databaseName
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}

// Container Apps (Consumption) have no fixed outbound IP. The 0.0.0.0 rule admits connections from Azure services only;
// closing the server to one environment would need a VNet (ADR #277, risks).
resource allowAzureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgres
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
  // The server accepts one change at a time.
  dependsOn: [database]
}

resource connectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'postgres-connection'
  properties: {
    value: 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=${databaseName};Username=${postgresLogin};Password=${postgresAdminPassword};SslMode=Require'
  }
}

resource jwtSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'jwt-signing-key'
  properties: { value: jwtSigningKey }
}

resource adminSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'admin-password'
  properties: { value: adminPassword }
}

// ---------------------------------------------------------------- container apps (ADR #211, #277)

// Without an explicit mode Azure creates an Express environment, which has no jobs, Key Vault references,
// internal service names or insecure ingress. The Consumption profile keeps pay-per-use and scale to zero.
resource environment 'Microsoft.App/managedEnvironments@2026-07-01' = {
  name: names.environment
  location: location
  properties: {
    environmentMode: 'WorkloadProfiles'
    workloadProfiles: [{ name: 'Consumption', workloadProfileType: 'Consumption' }]
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

var apiFqdn = '${names.api}.${environment.properties.defaultDomain}'
var webFqdn = '${names.web}.${environment.properties.defaultDomain}'
var userIdentity = { type: 'UserAssigned', userAssignedIdentities: { '${identity.id}': {} } }

var usesRegistryLogin = !empty(registryUsername)
var registries = usesRegistryLogin ? [{ server: registryServer, username: registryUsername, passwordSecretRef: 'registry-password' }] : []
var registrySecrets = usesRegistryLogin ? [{ name: 'registry-password', value: registryPassword }] : []

// Container Apps read the newest version of these Key Vault secrets when a revision or job execution starts.
var connectionSecretRef = { name: 'postgres-connection', keyVaultUrl: connectionSecret.properties.secretUri, identity: identity.id }
var backendSecrets = concat([connectionSecretRef], registrySecrets)
var apiSecrets = concat([
  connectionSecretRef
  { name: 'jwt-signing-key', keyVaultUrl: jwtSecret.properties.secretUri, identity: identity.id }
  { name: 'admin-password', keyVaultUrl: adminSecret.properties.secretUri, identity: identity.id }
], registrySecrets)

// Shared by the API and the jobs that run its image.
var backendEnv = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'ConnectionStrings__Postgres', secretRef: 'postgres-connection' }
  { name: 'Ml__BaseUrl', value: 'http://${names.ml}' }
  // The ML service scales to zero and needs time to start.
  { name: 'Ml__TimeoutSeconds', value: '60' }
  // The schedules run as the ml-analysis and weather jobs, not inside the API (ADR #277).
  { name: 'Ml__AnalysisEnabled', value: 'false' }
  { name: 'Weather__Enabled', value: 'false' }
]
var backendResources = { cpu: json('0.5'), memory: '1Gi' }

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: names.api
  location: location
  identity: userIdentity
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: true, targetPort: 8080, transport: 'auto' }
      secrets: apiSecrets
      registries: registries
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: backendResources
          env: concat(backendEnv, [
            { name: 'Jwt__SigningKey', secretRef: 'jwt-signing-key' }
            { name: 'Admin__Password', secretRef: 'admin-password' }
            { name: 'Cors__AllowedOrigins__0', value: 'https://${webFqdn}' }
          ])
          probes: [
            { type: 'Readiness', httpGet: { path: '/health', port: 8080 }, initialDelaySeconds: 5, periodSeconds: 10 }
          ]
        }
      ]
      // One replica: the rate limiter and the startup seeding are per process.
      scale: { minReplicas: minReplicas, maxReplicas: 1 }
    }
  }
  dependsOn: [secretsReader]
}

// Internal ingress: only apps in the environment (the API and the jobs) reach it, as http://<name> (ADR #196).
resource ml 'Microsoft.App/containerApps@2024-03-01' = {
  name: names.ml
  location: location
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: false, targetPort: 8000, transport: 'auto', allowInsecure: true }
      secrets: registrySecrets
      registries: registries
    }
    template: {
      containers: [
        {
          name: 'ml'
          image: mlImage
          resources: backendResources
          probes: [
            { type: 'Readiness', httpGet: { path: '/health', port: 8000 }, initialDelaySeconds: 5, periodSeconds: 10 }
          ]
        }
      ]
      scale: { minReplicas: 0, maxReplicas: 1 }
    }
  }
}

resource web 'Microsoft.App/containerApps@2024-03-01' = {
  name: names.web
  location: location
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: { external: true, targetPort: 8080, transport: 'auto' }
      secrets: registrySecrets
      registries: registries
    }
    template: {
      containers: [
        {
          name: 'web'
          image: webImage
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          // Exact API origin for the CSP connect-src (frontend/nginx/default.conf.template).
          env: [{ name: 'API_ORIGIN', value: 'https://${apiFqdn}' }]
          // "/" and not "/healthz", so the placeholder image passes as well.
          probes: [
            { type: 'Readiness', httpGet: { path: '/', port: 8080 }, initialDelaySeconds: 2, periodSeconds: 10 }
          ]
        }
      ]
      scale: { minReplicas: minReplicas, maxReplicas: 1 }
    }
  }
}

// ---------------------------------------------------------------- jobs (ADR #277)

// Started by the deployment before a new API revision; the API migrates by itself only in Development.
resource migrateJob 'Microsoft.App/jobs@2024-03-01' = {
  name: names.migrateJob
  location: location
  identity: userIdentity
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 0
      manualTriggerConfig: { parallelism: 1, replicaCompletionCount: 1 }
      secrets: backendSecrets
      registries: registries
    }
    template: {
      containers: [
        { name: 'migrate', image: apiImage, args: ['--job', 'migrate'], env: backendEnv, resources: backendResources }
      ]
    }
  }
  dependsOn: [secretsReader]
}

// Hourly analysis of the monitored installations (alarms and tickets, #33).
resource mlJob 'Microsoft.App/jobs@2024-03-01' = {
  name: names.mlJob
  location: location
  identity: userIdentity
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Schedule'
      replicaTimeout: 900
      replicaRetryLimit: 0
      scheduleTriggerConfig: { cronExpression: '5 * * * *', parallelism: 1, replicaCompletionCount: 1 }
      secrets: backendSecrets
      registries: registries
    }
    template: {
      containers: [
        { name: 'ml-analysis', image: apiImage, args: ['--job', 'ml-analysis'], env: backendEnv, resources: backendResources }
      ]
    }
  }
  dependsOn: [secretsReader]
}

// Daily 48-hour weather forecast (#104), before the morning production.
resource weatherJob 'Microsoft.App/jobs@2024-03-01' = {
  name: names.weatherJob
  location: location
  identity: userIdentity
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Schedule'
      replicaTimeout: 600
      replicaRetryLimit: 1
      scheduleTriggerConfig: { cronExpression: '30 3 * * *', parallelism: 1, replicaCompletionCount: 1 }
      secrets: backendSecrets
      registries: registries
    }
    template: {
      containers: [
        { name: 'weather-refresh', image: apiImage, args: ['--job', 'weather-refresh'], env: backendEnv, resources: backendResources }
      ]
    }
  }
  dependsOn: [secretsReader]
}

output apiUrl string = 'https://${apiFqdn}'
output webUrl string = 'https://${webFqdn}'
output keyVaultName string = keyVault.name
output postgresHost string = postgres.properties.fullyQualifiedDomainName
output migrateJobName string = migrateJob.name
// GitHub variable AZURE_CLIENT_ID of the CD pipeline.
output deployClientId string = deployIdentity.properties.clientId

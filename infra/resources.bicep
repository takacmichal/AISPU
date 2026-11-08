// Resources live in the resource group created by main.bicep.
// Architecture: ADR #211 (Container Apps + Static Web Apps) and ADR #213 (PostgreSQL Flexible Server).

param environment string
param location string
param namePrefix string
param nameSuffix string
param tags object

param postgresAdminLogin string
@secure()
param postgresAdminPassword string
param postgresBackupRetentionDays int

@secure()
param jwtSigningKey string

param apiImage string
param mlImage string
param apiMinReplicas int
param apiMaxReplicas int
param mlMinReplicas int
param mlMaxReplicas int
param logRetentionDays int

var logAnalyticsName = 'log-${namePrefix}-${environment}-${nameSuffix}'
var containerAppsEnvironmentName = 'cae-${namePrefix}-${environment}-${nameSuffix}'
var apiAppName = 'ca-${namePrefix}-api-${environment}'
var mlAppName = 'ca-${namePrefix}-ml-${environment}'
var postgresName = 'psql-${namePrefix}-${environment}-${nameSuffix}'
var staticWebAppName = 'swa-${namePrefix}-${environment}-${nameSuffix}'
var databaseName = 'solarops'

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: logRetentionDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerAppsEnvironmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

resource staticWebApp 'Microsoft.Web/staticSites@2023-12-01' = {
  name: staticWebAppName
  location: location
  tags: tags
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {}
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: postgresName
  location: location
  tags: tags
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    storage: {
      storageSizeGB: 32
      autoGrow: 'Enabled'
    }
    backup: {
      backupRetentionDays: postgresBackupRetentionDays
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

// Container Apps has no stable outbound IP, so the student setup allows Azure services.
// A private endpoint or VNet integration is the production alternative; track it in a follow-up.
resource allowAzureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgres
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource requireSecureTransport 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: postgres
  name: 'require_secure_transport'
  properties: {
    value: 'ON'
    source: 'user-override'
  }
}

resource mlApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: mlAppName
  location: location
  tags: tags
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 8000
        transport: 'http'
        // Internal traffic only; HTTP avoids an extra certificate hop between the two apps.
        allowInsecure: true
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
    }
    template: {
      containers: [
        {
          name: 'ml'
          image: mlImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'PORT'
              value: '8000'
            }
          ]
        }
      ]
      scale: {
        minReplicas: mlMinReplicas
        maxReplicas: mlMaxReplicas
      }
    }
  }
}

var apiConnectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Port=5432;Database=${databaseName};Username=${postgresAdminLogin};Password=${postgresAdminPassword};SSL Mode=Require;Trust Server Certificate=false'
var mlBaseUrl = 'http://${mlApp.properties.configuration.ingress.fqdn}'
var staticWebAppOrigin = 'https://${staticWebApp.properties.defaultHostname}'

resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiAppName
  location: location
  tags: tags
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'http'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      secrets: [
        {
          name: 'postgres-connection'
          value: apiConnectionString
        }
        {
          name: 'jwt-signing-key'
          value: jwtSigningKey
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ConnectionStrings__Postgres'
              secretRef: 'postgres-connection'
            }
            {
              name: 'Jwt__SigningKey'
              secretRef: 'jwt-signing-key'
            }
            {
              // Container Apps ingress adds X-Forwarded-For from its private 10.x network.
              // Narrow this range when the environment moves behind a VNet.
              name: 'ForwardedHeaders__KnownNetworks__0'
              value: '10.0.0.0/8'
            }
            {
              name: 'Ml__BaseUrl'
              value: mlBaseUrl
            }
            {
              name: 'Cors__AllowedOrigins__0'
              value: staticWebAppOrigin
            }
          ]
        }
      ]
      scale: {
        minReplicas: apiMinReplicas
        maxReplicas: apiMaxReplicas
      }
    }
  }
  dependsOn: [
    database
    requireSecureTransport
  ]
}

output apiFqdn string = apiApp.properties.configuration.ingress.fqdn
output mlFqdn string = mlApp.properties.configuration.ingress.fqdn
output staticWebAppHostname string = staticWebApp.properties.defaultHostname
output staticWebAppName string = staticWebApp.name
output postgresFqdn string = postgres.properties.fullyQualifiedDomainName
output postgresName string = postgres.name
output databaseName string = database.name
output containerAppsEnvironmentName string = containerAppsEnvironment.name
output logAnalyticsWorkspaceId string = logAnalytics.id

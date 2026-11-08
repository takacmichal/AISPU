using './main.bicep'

// Images and secrets come from environment variables: infra/deploy.sh loads them from infra/.env (ignored by git)
// and generates the secrets on the first run. Nothing secret is stored in this file.
param location = 'polandcentral'
// Azure for Students gets SkuNotAvailable for Burstable PostgreSQL in Poland Central and Sweden Central (November 2026).
param postgresLocation = 'italynorth'

param apiImage = readEnvironmentVariable('SOLAROPS_API_IMAGE')
param mlImage = readEnvironmentVariable('SOLAROPS_ML_IMAGE')
param webImage = readEnvironmentVariable('SOLAROPS_WEB_IMAGE')

param postgresAdminPassword = readEnvironmentVariable('SOLAROPS_POSTGRES_PASSWORD')
param jwtSigningKey = readEnvironmentVariable('SOLAROPS_JWT_SIGNING_KEY')
param adminPassword = readEnvironmentVariable('SOLAROPS_ADMIN_PASSWORD')

param alertEmail = readEnvironmentVariable('SOLAROPS_ALERT_EMAIL', '')

param registryUsername = readEnvironmentVariable('SOLAROPS_REGISTRY_USERNAME', '')
param registryPassword = readEnvironmentVariable('SOLAROPS_REGISTRY_PASSWORD', '')

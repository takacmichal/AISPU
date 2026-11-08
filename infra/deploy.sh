#!/usr/bin/env bash
# Deploys the SolarOps Azure environment (#157, ADR #277): resource group, Bicep template, database migration.
# Needs `az login`. Secrets live only in infra/.env (ignored by git) and are generated on the first run.
#
#   SOLAROPS_API_IMAGE=ghcr.io/<owner>/aispu-api:<tag> SOLAROPS_ML_IMAGE=ghcr.io/<owner>/aispu-ml:<tag> infra/deploy.sh
#
# On later runs the images may be left out: the ones currently deployed are kept. See infra/README.md.
set -euo pipefail
cd "$(dirname "$0")"

RESOURCE_GROUP="${SOLAROPS_RESOURCE_GROUP:-solarops-rg}"
LOCATION="${SOLAROPS_LOCATION:-polandcentral}"
DEPLOYMENT=solarops
ENV_FILE=.env
PLACEHOLDER_WEB_IMAGE=docker.io/nginxinc/nginx-unprivileged:1.30-alpine
# The containerapp commands come from an az extension; install it without asking.
export AZURE_EXTENSION_USE_DYNAMIC_INSTALL=yes_without_prompt

die() { echo "error: $*" >&2; exit 1; }

# ---------------------------------------------------------------- secrets

touch "$ENV_FILE"
chmod 600 "$ENV_FILE"

# Hex only: safe inside the connection string and the shell.
new_hex() { openssl rand -hex "$1"; }
# Meets the Identity password rules (length, upper and lower case, digit).
new_admin_password() { echo "Admin-$(openssl rand -hex 8)7"; }

ensure_secret() {
  local name=$1 value
  if ! grep -q "^${name}=" "$ENV_FILE"; then
    value=$("${@:2}")
    echo "${name}=${value}" >> "$ENV_FILE"
    echo "Generated ${name} in infra/${ENV_FILE}"
  fi
}

ensure_secret SOLAROPS_POSTGRES_PASSWORD new_hex 24
ensure_secret SOLAROPS_JWT_SIGNING_KEY new_hex 32
ensure_secret SOLAROPS_ADMIN_PASSWORD new_admin_password

set -a
# shellcheck disable=SC1090
source "./$ENV_FILE"
set +a

# ---------------------------------------------------------------- images

current_image() {
  az containerapp show -g "$RESOURCE_GROUP" -n "$1" --query "properties.template.containers[0].image" -o tsv 2>/dev/null || true
}

: "${SOLAROPS_API_IMAGE:=$(current_image solarops-api)}"
: "${SOLAROPS_ML_IMAGE:=$(current_image solarops-ml)}"
: "${SOLAROPS_WEB_IMAGE:=$(current_image solarops-web)}"
: "${SOLAROPS_WEB_IMAGE:=$PLACEHOLDER_WEB_IMAGE}"
[[ -n "$SOLAROPS_API_IMAGE" ]] || die "set SOLAROPS_API_IMAGE (first deployment)"
[[ -n "$SOLAROPS_ML_IMAGE" ]] || die "set SOLAROPS_ML_IMAGE (first deployment)"
export SOLAROPS_API_IMAGE SOLAROPS_ML_IMAGE SOLAROPS_WEB_IMAGE

echo "Subscription: $(az account show --query name -o tsv)"
echo "Resource group: $RESOURCE_GROUP ($LOCATION)"
echo "Images: api=$SOLAROPS_API_IMAGE ml=$SOLAROPS_ML_IMAGE web=$SOLAROPS_WEB_IMAGE"

# ---------------------------------------------------------------- deployment

az group create -n "$RESOURCE_GROUP" -l "$LOCATION" -o none
az deployment group create -g "$RESOURCE_GROUP" -n "$DEPLOYMENT" --parameters main.bicepparam -o none

output() { az deployment group show -g "$RESOURCE_GROUP" -n "$DEPLOYMENT" --query "properties.outputs.$1.value" -o tsv; }
API_URL=$(output apiUrl)
WEB_URL=$(output webUrl)
MIGRATE_JOB=$(output migrateJobName)
DEPLOY_CLIENT_ID=$(output deployClientId)

# ---------------------------------------------------------------- migrations

echo "Running database migrations ($MIGRATE_JOB)"
EXECUTION=$(az containerapp job start -g "$RESOURCE_GROUP" -n "$MIGRATE_JOB" --query name -o tsv)
for _ in $(seq 1 60); do
  STATUS=$(az containerapp job execution show -g "$RESOURCE_GROUP" -n "$MIGRATE_JOB" --job-execution-name "$EXECUTION" \
    --query properties.status -o tsv)
  case "$STATUS" in
    Succeeded) break ;;
    Failed | Stopped | Degraded) die "migration $EXECUTION ended with status $STATUS (logs: infra/README.md)" ;;
  esac
  sleep 10
done
[[ "$STATUS" == Succeeded ]] || die "migration $EXECUTION did not finish in 10 minutes"

# The API seeds the roles and the first admin at startup, so it restarts once the schema exists.
REVISION=$(az containerapp show -g "$RESOURCE_GROUP" -n solarops-api --query properties.latestRevisionName -o tsv)
az containerapp revision restart -g "$RESOURCE_GROUP" -n solarops-api --revision "$REVISION" -o none

echo
echo "Frontend: $WEB_URL"
echo "API:      $API_URL (docs: $API_URL/scalar/v1)"
echo "Admin:    admin@solarops.local, password SOLAROPS_ADMIN_PASSWORD in infra/$ENV_FILE"
echo
echo "GitHub repository variables for the CD pipeline (.github/workflows/deploy.yml):"
echo "  AZURE_CLIENT_ID=$DEPLOY_CLIENT_ID"
echo "  AZURE_TENANT_ID=$(az account show --query tenantId -o tsv)"
echo "  AZURE_SUBSCRIPTION_ID=$(az account show --query id -o tsv)"

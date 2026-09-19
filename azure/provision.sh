#!/usr/bin/env bash
#
# One-time Azure provisioning for the JoinEvents API.
#
# Creates a resource group, a Container Apps environment, the Container App itself,
# and wires the secrets it needs. Safe to re-run: every step is idempotent.
#
# The DATABASE is deliberately NOT created here — the Azure SQL free offer is a
# checkbox in the portal's create flow, and the CLI flag names for it have changed
# between az versions. See AZURE_DEPLOY.md step 2.
#
# Usage:
#   export SQL_CONNECTION_STRING='Server=tcp:...;Connect Timeout=60;...'
#   export JWT_KEY="$(openssl rand -base64 48)"
#   export BOOTSTRAP_ADMIN_EMAIL='you@example.com'
#   export BOOTSTRAP_ADMIN_PASSWORD='...'
#   export IMAGE='ghcr.io/<owner>/joinevents-backend:latest'
#   ./azure/provision.sh
#
set -euo pipefail

LOCATION="${LOCATION:-centralindia}"
RESOURCE_GROUP="${RESOURCE_GROUP:-joinevents-rg}"
ENVIRONMENT="${ENVIRONMENT:-joinevents-env}"
APP_NAME="${APP_NAME:-joinevents-api}"
IMAGE="${IMAGE:-}"
ALLOWED_ORIGINS="${ALLOWED_ORIGINS:-https://joinevents.netlify.app}"

require() {
  local name="$1"
  if [[ -z "${!name:-}" ]]; then
    echo "error: $name is required. See the header of this script." >&2
    exit 1
  fi
}

require SQL_CONNECTION_STRING
require JWT_KEY
require IMAGE

if [[ ${#JWT_KEY} -lt 32 ]]; then
  echo "error: JWT_KEY must be at least 32 characters — the API refuses to start otherwise." >&2
  exit 1
fi

echo "==> Registering the providers this needs (no-op if already registered)"
az provider register --namespace Microsoft.App --wait
az provider register --namespace Microsoft.OperationalInsights --wait

echo "==> Resource group: $RESOURCE_GROUP ($LOCATION)"
az group create --name "$RESOURCE_GROUP" --location "$LOCATION" --output none

echo "==> Container Apps environment: $ENVIRONMENT"
if ! az containerapp env show --name "$ENVIRONMENT" --resource-group "$RESOURCE_GROUP" --output none 2>/dev/null; then
  az containerapp env create \
    --name "$ENVIRONMENT" \
    --resource-group "$RESOURCE_GROUP" \
    --location "$LOCATION" \
    --output none
else
  echo "    already exists"
fi

# Secrets are stored on the app, not baked into the image or the workflow.
SECRETS=(
  "sql-connection=$SQL_CONNECTION_STRING"
  "jwt-key=$JWT_KEY"
)
if [[ -n "${BOOTSTRAP_ADMIN_PASSWORD:-}" ]]; then
  SECRETS+=("bootstrap-admin-password=$BOOTSTRAP_ADMIN_PASSWORD")
fi

# Environment variables. Note what is NOT set:
#   Database__MigrateOnStartup stays false — migrations run as a release step.
#   Payments__AllowSimulator stays false — startup fails in Production rather than
#   pretending the simulator gateway is a payment integration.
ENV_VARS=(
  "ASPNETCORE_ENVIRONMENT=Production"
  "ConnectionStrings__DefaultConnection=secretref:sql-connection"
  "Jwt__Key=secretref:jwt-key"
  "AllowedOrigins__0=$ALLOWED_ORIGINS"
)
if [[ -n "${BOOTSTRAP_ADMIN_EMAIL:-}" ]]; then
  ENV_VARS+=("Bootstrap__AdminEmail=$BOOTSTRAP_ADMIN_EMAIL")
fi
if [[ -n "${BOOTSTRAP_ADMIN_PASSWORD:-}" ]]; then
  ENV_VARS+=("Bootstrap__AdminPassword=secretref:bootstrap-admin-password")
fi

echo "==> Container app: $APP_NAME"
if ! az containerapp show --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" --output none 2>/dev/null; then
  # min-replicas 0 lets the app scale to zero, which is what keeps it inside the
  # monthly free grant. The cost is a cold start on the first request after idle.
  #
  # No HTTP health probe is configured on purpose: the readiness endpoint opens a
  # database connection, and a probe polling it would keep the serverless database
  # permanently awake and burn its monthly compute allowance. The default TCP probe
  # is enough here.
  az containerapp create \
    --name "$APP_NAME" \
    --resource-group "$RESOURCE_GROUP" \
    --environment "$ENVIRONMENT" \
    --image "$IMAGE" \
    --target-port 8080 \
    --ingress external \
    --min-replicas 0 \
    --max-replicas 2 \
    --cpu 0.5 \
    --memory 1.0Gi \
    --secrets "${SECRETS[@]}" \
    --env-vars "${ENV_VARS[@]}" \
    --output none
else
  echo "    already exists — updating secrets and configuration"
  az containerapp secret set \
    --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" \
    --secrets "${SECRETS[@]}" --output none
  az containerapp update \
    --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" \
    --image "$IMAGE" \
    --set-env-vars "${ENV_VARS[@]}" \
    --output none
fi

FQDN=$(az containerapp show --name "$APP_NAME" --resource-group "$RESOURCE_GROUP" \
  --query properties.configuration.ingress.fqdn --output tsv)

echo
echo "================================================================"
echo "API base URL:  https://$FQDN"
echo "Liveness:      https://$FQDN/health/live   <- point uptime monitors HERE"
echo "Readiness:     https://$FQDN/health        <- touches the database, do not poll"
echo
echo "Next:"
echo "  1. Add https://$FQDN/api/v1 to the frontend's environment.ts"
echo "  2. Add the frontend's own origin to ALLOWED_ORIGINS and re-run this script"
echo "================================================================"

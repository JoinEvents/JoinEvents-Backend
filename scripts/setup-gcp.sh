#!/bin/bash

# GCP Setup Script for EventEase API
# This script automates the initial setup process for deploying to Google Cloud Run

set -e

echo "🚀 EventEase API - GCP Cloud Run Setup Script"
echo "=================================================="
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check prerequisites
echo "📋 Checking prerequisites..."
command -v gcloud &> /dev/null || { echo -e "${RED}❌ gcloud CLI not installed. Please install it first.${NC}"; exit 1; }
command -v docker &> /dev/null || { echo -e "${RED}❌ Docker not installed. Please install it first.${NC}"; exit 1; }
echo -e "${GREEN}✅ Prerequisites met${NC}"
echo ""

# Get user inputs
echo "📝 Please provide the following information:"
read -p "GitHub Repository Owner (e.g., FestMela): " GITHUB_OWNER
read -p "GitHub Repository Name (e.g., EventEase): " GITHUB_REPO
read -p "GCP Project ID (e.g., eventeaseapi-prod): " GCP_PROJECT_ID
read -p "GCP Region (default: us-central1): " GCP_REGION
GCP_REGION=${GCP_REGION:-us-central1}

echo ""
echo "📍 Configuration Summary:"
echo "  GitHub Repo: $GITHUB_OWNER/$GITHUB_REPO"
echo "  GCP Project: $GCP_PROJECT_ID"
echo "  Region: $GCP_REGION"
echo ""
read -p "Continue with this configuration? (y/n) " -n 1 -r
echo
if [[ ! $REPLY =~ ^[Yy]$ ]]; then
  echo "Cancelled."
  exit 1
fi

# Set project
echo ""
echo "🔧 Setting up GCP Project..."
gcloud config set project $GCP_PROJECT_ID
PROJECT_NUMBER=$(gcloud projects describe $GCP_PROJECT_ID --format='value(projectNumber)')
echo -e "${GREEN}✅ Project set to: $GCP_PROJECT_ID (Number: $PROJECT_NUMBER)${NC}"

# Enable APIs
echo ""
echo "🔌 Enabling required GCP APIs..."
apis=(
  "run.googleapis.com"
  "cloudbuild.googleapis.com"
  "containerregistry.googleapis.com"
  "secretmanager.googleapis.com"
  "sqladmin.googleapis.com"
  "artifactregistry.googleapis.com"
  "iam.googleapis.com"
)

for api in "${apis[@]}"; do
  echo "  Enabling $api..."
  gcloud services enable $api --quiet
done
echo -e "${GREEN}✅ APIs enabled${NC}"

# Create Service Accounts
echo ""
echo "👤 Creating Service Accounts..."

gcloud iam service-accounts create eventeaseapi-sa \
  --display-name="EventEase API Service Account" \
  --quiet 2>/dev/null || echo "  Service account eventeaseapi-sa already exists"

gcloud iam service-accounts create eventeaseapi-github \
  --display-name="EventEase API GitHub Actions" \
  --quiet 2>/dev/null || echo "  Service account eventeaseapi-github already exists"

echo -e "${GREEN}✅ Service accounts created${NC}"

# Set up Workload Identity Federation
echo ""
echo "🔐 Setting up Workload Identity Federation..."

WORKLOAD_IDENTITY_POOL="github-pool"
WORKLOAD_IDENTITY_PROVIDER="github-provider"

# Create Workload Identity Pool
gcloud iam workload-identity-pools create $WORKLOAD_IDENTITY_POOL \
  --project=$GCP_PROJECT_ID \
  --location=global \
  --display-name="GitHub Pool" \
  --quiet 2>/dev/null || echo "  Workload Identity Pool already exists"

# Create Workload Identity Provider
gcloud iam workload-identity-providers create-oidc $WORKLOAD_IDENTITY_PROVIDER \
  --project=$GCP_PROJECT_ID \
  --location=global \
  --display-name="GitHub Provider" \
  --attribute-mapping="google.subject=assertion.sub,attribute.actor=assertion.actor,attribute.repository=assertion.repository,attribute.repository_owner=assertion.repository_owner" \
  --issuer-uri="https://token.actions.githubusercontent.com" \
  --attribute-condition="assertion.repository_owner == '$GITHUB_OWNER'" \
  --quiet 2>/dev/null || echo "  Workload Identity Provider already exists"

echo -e "${GREEN}✅ Workload Identity Federation configured${NC}"

# Configure IAM roles
echo ""
echo "👮 Configuring IAM roles..."

GITHUB_SA="eventeaseapi-github@$GCP_PROJECT_ID.iam.gserviceaccount.com"
RUN_SA="eventeaseapi-sa@$GCP_PROJECT_ID.iam.gserviceaccount.com"

# Grant roles to GitHub Actions service account
roles=(
  "roles/run.admin"
  "roles/storage.admin"
  "roles/artifactregistry.writer"
)

for role in "${roles[@]}"; do
  echo "  Granting $role to GitHub Actions service account..."
  gcloud projects add-iam-policy-binding $GCP_PROJECT_ID \
    --member="serviceAccount:$GITHUB_SA" \
    --role=$role \
    --quiet
done

# Create Workload Identity binding
gcloud iam service-accounts add-iam-policy-binding $GITHUB_SA \
  --project=$GCP_PROJECT_ID \
  --role="roles/iam.workloadIdentityUser" \
  --member="principalSet://iam.googleapis.com/projects/$PROJECT_NUMBER/locations/global/workloadIdentityPools/$WORKLOAD_IDENTITY_POOL/attribute.repository/$GITHUB_OWNER/$GITHUB_REPO" \
  --quiet

echo -e "${GREEN}✅ IAM roles configured${NC}"

# Display GitHub Secrets information
echo ""
echo "🔑 GitHub Secrets to Add:"
echo "================================================"

WIF_PROVIDER="projects/$PROJECT_NUMBER/locations/global/workloadIdentityPools/$WORKLOAD_IDENTITY_POOL/providers/$WORKLOAD_IDENTITY_PROVIDER"

echo ""
echo "Add these secrets to your GitHub repository:"
echo "  Settings → Secrets and variables → Actions"
echo ""
echo "GCP_PROJECT_ID:"
echo "  $GCP_PROJECT_ID"
echo ""
echo "WIF_PROVIDER:"
echo "  $WIF_PROVIDER"
echo ""
echo "WIF_SERVICE_ACCOUNT:"
echo "  $GITHUB_SA"
echo ""
echo "CLOUD_RUN_SERVICE_ACCOUNT:"
echo "  $RUN_SA"
echo ""

# Database setup prompt
echo ""
read -p "Do you want to set up Cloud SQL database now? (y/n) " -n 1 -r
echo
if [[ $REPLY =~ ^[Yy]$ ]]; then
  read -p "Database instance name (default: eventeasedb-prod): " DB_INSTANCE
  DB_INSTANCE=${DB_INSTANCE:-eventeasedb-prod}

  echo ""
  echo "📊 Creating Cloud SQL instance..."
  gcloud sql instances create $DB_INSTANCE \
    --database-version=MYSQL_8_0 \
    --tier=db-f1-micro \
    --region=$GCP_REGION \
    --backup \
    --quiet 2>/dev/null || echo "  Database instance already exists"

  echo ""
  echo "📂 Creating database..."
  gcloud sql databases create EventEaseDb \
    --instance=$DB_INSTANCE \
    --quiet 2>/dev/null || echo "  Database already exists"

  echo ""
  read -p "Create database user (name, default: eventeaseadmin): " DB_USER
  DB_USER=${DB_USER:-eventeaseadmin}
  read -sp "Database password: " DB_PASSWORD
  echo ""

  gcloud sql users create $DB_USER \
    --instance=$DB_INSTANCE \
    --password=$DB_PASSWORD \
    --quiet 2>/dev/null || echo "  User already exists"

  CLOUDSQL_CONNECTION_NAME=$(gcloud sql instances describe $DB_INSTANCE \
    --format='value(connectionName)')

  echo ""
  echo -e "${GREEN}✅ Cloud SQL configured${NC}"
  echo ""
  echo "Cloud SQL Connection Name (for Cloud Run):"
  echo "  $CLOUDSQL_CONNECTION_NAME"
fi

echo ""
echo "=================================================="
echo -e "${GREEN}✅ GCP Setup Complete!${NC}"
echo ""
echo "📚 Next Steps:"
echo "  1. Add the secrets listed above to your GitHub repository"
echo "  2. Update appsettings.Production.json with your configuration"
echo "  3. Push changes to main branch to trigger CI/CD"
echo "  4. Check GitHub Actions for deployment status"
echo ""
echo "📖 For detailed information, see GCP_DEPLOYMENT_GUIDE.md"

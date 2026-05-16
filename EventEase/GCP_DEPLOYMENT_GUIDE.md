# GCP Cloud Run Deployment Guide for EventEase API

## Table of Contents
1. [Prerequisites](#prerequisites)
2. [GCP Setup](#gcp-setup)
3. [Authentication & Keys](#authentication--keys)
4. [Database Configuration](#database-configuration)
5. [Secret Management](#secret-management)
6. [CI/CD Pipeline](#cicd-pipeline)
7. [Deployment Steps](#deployment-steps)
8. [Monitoring & Troubleshooting](#monitoring--troubleshooting)

---

## Prerequisites

### Required Tools
- Google Cloud SDK (gcloud CLI)
- Docker (for local testing)
- .NET 8 SDK
- Git

### Install Google Cloud SDK

**Windows (PowerShell):**
```powershell
# Download and run the installer
(New-Object Net.WebClient).DownloadFile("https://dl.google.com/dl/cloudsdk/channels/rapid/GoogleCloudSDKInstaller.exe", "$env:Temp\GoogleCloudSDKInstaller.exe")
& $env:Temp\GoogleCloudSDKInstaller.exe
```

**Or via Chocolatey:**
```powershell
choco install google-cloud-sdk
```

---

## GCP Setup

### Step 1: Create a GCP Project

```bash
# Create a new project
gcloud projects create eventeaseapi-prod --name="EventEase API Production"

# Set the project as default
gcloud config set project eventeaseapi-prod

# Get your Project ID
gcloud config get-value project
```

### Step 2: Enable Required APIs

```bash
# Enable Cloud Run
gcloud services enable run.googleapis.com

# Enable Cloud Build
gcloud services enable cloudbuild.googleapis.com

# Enable Container Registry
gcloud services enable containerregistry.googleapis.com

# Enable Secret Manager
gcloud services enable secretmanager.googleapis.com

# Enable Cloud SQL (if using managed database)
gcloud services enable sqladmin.googleapis.com

# Enable Artifact Registry
gcloud services enable artifactregistry.googleapis.com

# Enable Identity and Access Management
gcloud services enable iam.googleapis.com
```

### Step 3: Create Service Accounts

```bash
# Create service account for Cloud Run
gcloud iam service-accounts create eventeaseapi-sa \
  --display-name="EventEase API Service Account"

# Create service account for GitHub Actions (CI/CD)
gcloud iam service-accounts create eventeaseapi-github \
  --display-name="EventEase API GitHub Actions"
```

---

## Authentication & Keys

### Step 4: Set Up Workload Identity Federation (For GitHub Actions)

This allows GitHub Actions to authenticate with GCP without storing long-lived keys.

```bash
# Set variables
export PROJECT_ID=$(gcloud config get-value project)
export PROJECT_NUMBER=$(gcloud projects describe $PROJECT_ID --format='value(projectNumber)')
export GITHUB_REPO="FestMela/EventEase"
export WORKLOAD_IDENTITY_POOL="github-pool"
export WORKLOAD_IDENTITY_PROVIDER="github-provider"

# Create Workload Identity Pool
gcloud iam workload-identity-pools create $WORKLOAD_IDENTITY_POOL \
  --project=$PROJECT_ID \
  --location=global \
  --display-name="GitHub Pool"

# Create Workload Identity Provider
gcloud iam workload-identity-providers create-oidc $WORKLOAD_IDENTITY_PROVIDER \
  --project=$PROJECT_ID \
  --location=global \
  --display-name="GitHub Provider" \
  --attribute-mapping="google.subject=assertion.sub,attribute.actor=assertion.actor,attribute.repository=assertion.repository,attribute.repository_owner=assertion.repository_owner" \
  --issuer-uri="https://token.actions.githubusercontent.com" \
  --attribute-condition="assertion.repository_owner == 'FestMela'"
```

### Step 5: Configure GitHub Actions Service Account

```bash
# Grant necessary permissions to GitHub Actions service account
gcloud projects add-iam-policy-binding $PROJECT_ID \
  --member="serviceAccount:eventeaseapi-github@$PROJECT_ID.iam.gserviceaccount.com" \
  --role="roles/run.admin"

gcloud projects add-iam-policy-binding $PROJECT_ID \
  --member="serviceAccount:eventeaseapi-github@$PROJECT_ID.iam.gserviceaccount.com" \
  --role="roles/storage.admin"

gcloud projects add-iam-policy-binding $PROJECT_ID \
  --member="serviceAccount:eventeaseapi-github@$PROJECT_ID.iam.gserviceaccount.com" \
  --role="roles/artifactregistry.writer"

# Create Workload Identity binding
gcloud iam service-accounts add-iam-policy-binding eventeaseapi-github@$PROJECT_ID.iam.gserviceaccount.com \
  --project=$PROJECT_ID \
  --role="roles/iam.workloadIdentityUser" \
  --member="principalSet://iam.googleapis.com/projects/$PROJECT_NUMBER/locations/global/workloadIdentityPools/$WORKLOAD_IDENTITY_POOL/attribute.repository/FestMela/EventEase"
```

### Step 6: Get WIF Provider URL for GitHub Secrets

```bash
export WIF_PROVIDER=$(gcloud iam workload-identity-pools providers describe $WORKLOAD_IDENTITY_PROVIDER \
  --project=$PROJECT_ID \
  --location=global \
  --format='value(name)')

export WIF_SERVICE_ACCOUNT="eventeaseapi-github@$PROJECT_ID.iam.gserviceaccount.com"

echo "WIF Provider: $WIF_PROVIDER"
echo "Service Account: $WIF_SERVICE_ACCOUNT"
```

---

## Secret Management

### Step 7: Store Secrets in Google Secret Manager

```bash
# Store database connection string
echo -n "Server=your-cloudsql-ip;Database=EventEaseDb;User=your-user;Password=your-password;TrustServerCertificate=False;" | \
  gcloud secrets create db-connection-string --data-file=-

# Store JWT Key
echo -n "your-jwt-key-here" | \
  gcloud secrets create jwt-key --data-file=-

# Store Redis connection
echo -n "redis-instance.redislabs.com:port,password=your-password" | \
  gcloud secrets create redis-connection --data-file=-

# Store Azure Storage connection (if still used)
echo -n "DefaultEndpointsProtocol=https;..." | \
  gcloud secrets create azure-storage-connection --data-file=-

# Grant Cloud Run service account access to secrets
gcloud secrets add-iam-policy-binding db-connection-string \
  --member="serviceAccount:eventeaseapi-sa@$PROJECT_ID.iam.gserviceaccount.com" \
  --role="roles/secretmanager.secretAccessor"

gcloud secrets add-iam-policy-binding jwt-key \
  --member="serviceAccount:eventeaseapi-sa@$PROJECT_ID.iam.gserviceaccount.com" \
  --role="roles/secretmanager.secretAccessor"

gcloud secrets add-iam-policy-binding redis-connection \
  --member="serviceAccount:eventeaseapi-sa@$PROJECT_ID.iam.gserviceaccount.com" \
  --role="roles/secretmanager.secretAccessor"
```

---

## Database Configuration

### Step 8: Set Up Cloud SQL (PostgreSQL or MySQL)

```bash
# Create a Cloud SQL instance
gcloud sql instances create eventeasedb-prod \
  --database-version=MYSQL_8_0 \
  --tier=db-f1-micro \
  --region=us-central1 \
  --backup

# Create database
gcloud sql databases create EventEaseDb \
  --instance=eventeasedb-prod

# Create user
gcloud sql users create eventeaseadmin \
  --instance=eventeasedb-prod \
  --password

# Get the Cloud SQL instance connection name
gcloud sql instances describe eventeasedb-prod \
  --format='value(connectionName)'
```

### Step 9: Update Connection String

Your Cloud SQL connection string should follow this format for Cloud Run:
```
Server=/cloudsql/PROJECT_ID:REGION:INSTANCE_NAME;Database=EventEaseDb;Uid=eventeaseadmin;Pwd=YOUR_PASSWORD;
```

---

## CI/CD Pipeline

### Step 10: Add GitHub Secrets

Go to your GitHub repository → Settings → Secrets and variables → Actions

Add these secrets:

```
GCP_PROJECT_ID: your-project-id
WIF_PROVIDER: projects/PROJECT_NUMBER/locations/global/workloadIdentityPools/github-pool/providers/github-provider
WIF_SERVICE_ACCOUNT: eventeaseapi-github@your-project-id.iam.gserviceaccount.com
CLOUD_RUN_SERVICE_ACCOUNT: eventeaseapi-sa@your-project-id.iam.gserviceaccount.com
```

### Step 11: Environment Variables for Cloud Run

Update your `appsettings.Production.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=/cloudsql/PROJECT_ID:REGION:INSTANCE_NAME;Database=EventEaseDb;Uid=user;Pwd=${DB_PASSWORD};"
  },
  "Redis": {
    "Connection": "${REDIS_CONNECTION}"
  },
  "Jwt": {
    "Issuer": "EventEase",
    "Audience": "EventEaseClients",
    "Key": "${JWT_KEY}",
    "AccessTokenMinutes": 30,
    "RefreshTokenDays": 7
  },
  "AllowedHosts": "*",
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

---

## Deployment Steps

### Step 12: Manual Test Deployment

```bash
# Build Docker image locally
docker build -t gcr.io/your-project-id/eventeaseapi:latest .

# Test locally
docker run -p 8080:8080 \
  -e "ASPNETCORE_ENVIRONMENT=Production" \
  gcr.io/your-project-id/eventeaseapi:latest

# Push to Google Container Registry
docker tag gcr.io/your-project-id/eventeaseapi:latest gcr.io/your-project-id/eventeaseapi:v1.0.0
docker push gcr.io/your-project-id/eventeaseapi:latest
docker push gcr.io/your-project-id/eventeaseapi:v1.0.0

# Deploy to Cloud Run
gcloud run deploy eventeaseapi \
  --image=gcr.io/your-project-id/eventeaseapi:latest \
  --region=us-central1 \
  --platform=managed \
  --allow-unauthenticated \
  --memory=1Gi \
  --cpu=1 \
  --timeout=3600 \
  --service-account=eventeaseapi-sa@your-project-id.iam.gserviceaccount.com \
  --set-env-vars="ASPNETCORE_ENVIRONMENT=Production,CLOUDSQL_CONNECTION_NAME=your-project-id:us-central1:eventeasedb-prod"
```

### Step 13: Automated Deployment with GitHub Actions

```bash
# Push changes to main branch
git add .
git commit -m "Add GCP deployment configuration"
git push origin main

# GitHub Actions will automatically:
# 1. Build the Docker image
# 2. Push to Google Container Registry
# 3. Deploy to Cloud Run
```

---

## Monitoring & Troubleshooting

### Step 14: View Logs

```bash
# View Cloud Run logs
gcloud run logs read eventeaseapi --limit=50 --region=us-central1

# Stream logs in real-time
gcloud run logs read eventeaseapi --region=us-central1 --follow

# View build logs
gcloud builds log --stream LATEST --region=us-central1
```

### Step 15: Common Issues & Solutions

**Issue: "Permission denied" when accessing secrets**
```bash
# Grant secret access to Cloud Run service account
gcloud secrets add-iam-policy-binding jwt-key \
  --member="serviceAccount:eventeaseapi-sa@your-project-id.iam.gserviceaccount.com" \
  --role="roles/secretmanager.secretAccessor"
```

**Issue: Database connection timeout**
```bash
# Verify Cloud SQL connection
gcloud sql instances describe eventeasedb-prod --format='value(connectionName)'

# Test connectivity
gcloud sql connect eventeasedb-prod --user=eventeaseadmin
```

**Issue: Docker image too large**
```bash
# Use .dockerignore to exclude unnecessary files
# Create .dockerignore file with:
# bin/
# obj/
# .vs/
# .git/
# node_modules/
```

### Step 16: Monitor Service Health

```bash
# Get service details
gcloud run services describe eventeaseapi --region=us-central1

# View traffic metrics
gcloud monitoring time-series list \
  --filter='metric.type="run.googleapis.com/request_count"'

# Set up alerts
gcloud alpha monitoring policies create \
  --notification-channels=YOUR_CHANNEL_ID \
  --display-name="EventEase API High Error Rate" \
  --condition-display-name="Error rate > 5%"
```

---

## Security Best Practices

1. ✅ Use Workload Identity Federation instead of service account keys
2. ✅ Store secrets in Secret Manager, not in code
3. ✅ Use least-privilege IAM roles
4. ✅ Enable VPC Service Controls for additional security
5. ✅ Set up CORS properly for frontend access
6. ✅ Use HTTPS only (Cloud Run enforces this)
7. ✅ Regularly update dependencies and Docker base images
8. ✅ Enable audit logging for all deployments

---

## Useful Commands Cheat Sheet

```bash
# Project management
gcloud projects list
gcloud config set project PROJECT_ID

# Cloud Run
gcloud run services list --region=us-central1
gcloud run services delete eventeaseapi --region=us-central1
gcloud run services update eventeaseapi --region=us-central1 --set-env-vars=KEY=VALUE

# Cloud SQL
gcloud sql instances list
gcloud sql instances delete eventeasedb-prod
gcloud sql backups create --instance=eventeasedb-prod

# Secrets
gcloud secrets list
gcloud secrets versions access latest --secret=jwt-key
gcloud secrets delete jwt-key

# IAM
gcloud iam service-accounts list
gcloud iam roles list
gcloud projects get-iam-policy PROJECT_ID
```

---

## Next Steps

1. Create GCP project and enable APIs
2. Set up Workload Identity Federation
3. Create Service Accounts
4. Store secrets in Secret Manager
5. Set up Cloud SQL database
6. Add GitHub secrets
7. Update appsettings.Production.json
8. Test deployment manually
9. Push to main branch to trigger CI/CD
10. Monitor logs and metrics

For questions or issues, check GCP documentation: https://cloud.google.com/run/docs

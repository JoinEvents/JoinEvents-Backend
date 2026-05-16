# GCP & CI/CD Quick Start Guide

## 📋 Overview

This guide provides quick steps to deploy EventEase API to Google Cloud Run with automated CI/CD using GitHub Actions.

---

## 🚀 Quick Start (5 Steps)

### Step 1: Run the Setup Script

**For Windows (PowerShell):**
```powershell
cd scripts
.\setup-gcp.ps1 -GithubOwner "FestMela" -GithubRepo "EventEase" -GcpProjectId "eventeaseapi-prod"
```

**For Linux/Mac (Bash):**
```bash
chmod +x scripts/setup-gcp.sh
./scripts/setup-gcp.sh
```

The script will:
- ✅ Enable all required GCP APIs
- ✅ Create service accounts
- ✅ Set up Workload Identity Federation
- ✅ Configure IAM roles
- ✅ Display GitHub secrets to add

### Step 2: Add GitHub Secrets

Go to: **GitHub Repo → Settings → Secrets and variables → Actions**

Add these 4 secrets (from Step 1 output):
```
GCP_PROJECT_ID
WIF_PROVIDER
WIF_SERVICE_ACCOUNT
CLOUD_RUN_SERVICE_ACCOUNT
```

### Step 3: Create Production Configuration

Update your environment variables in Cloud Run deployment or create `appsettings.Production.json`.

Required environment variables:
```
ASPNETCORE_ENVIRONMENT=Production
CLOUDSQL_CONNECTION_NAME=project:region:instance
DB_USER=your-db-user
DB_PASSWORD=your-db-password
JWT_KEY=your-jwt-key
REDIS_CONNECTION=your-redis-connection
```

### Step 4: Push to Main Branch

```bash
git add .
git commit -m "Add GCP deployment configuration"
git push origin main
```

GitHub Actions will automatically:
1. Build Docker image
2. Push to Google Container Registry
3. Deploy to Cloud Run

### Step 5: Verify Deployment

```bash
# Check deployment status
gcloud run services describe eventeaseapi --region=us-central1

# View logs
gcloud run logs read eventeaseapi --limit=50

# Get service URL
gcloud run services describe eventeaseapi \
  --region=us-central1 \
  --format='value(status.url)'
```

---

## 🔑 Environment Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `ASPNETCORE_ENVIRONMENT` | Environment mode | `Production` |
| `CLOUDSQL_CONNECTION_NAME` | Cloud SQL connection | `project:region:instance` |
| `DB_USER` | Database username | `eventeaseadmin` |
| `DB_PASSWORD` | Database password | `password123` |
| `JWT_KEY` | JWT signing key | `your-secret-key` |
| `REDIS_CONNECTION` | Redis connection string | `redis:6379` |
| `GCP_PROJECT_ID` | GCP Project ID | `eventeaseapi-prod` |

---

## 📊 Database Setup

### Create Cloud SQL Instance

```bash
# Create MySQL instance
gcloud sql instances create eventeasedb-prod \
  --database-version=MYSQL_8_0 \
  --tier=db-f1-micro \
  --region=us-central1

# Get connection name
gcloud sql instances describe eventeasedb-prod \
  --format='value(connectionName)'
```

### Create Database & User

```bash
# Create database
gcloud sql databases create EventEaseDb --instance=eventeasedb-prod

# Create user
gcloud sql users create eventeaseadmin \
  --instance=eventeasedb-prod \
  --password

# Get Cloud SQL Proxy connection string for Cloud Run
# Format: /cloudsql/PROJECT:REGION:INSTANCE
```

---

## 🐳 Docker Build & Test

### Build Locally

```bash
# Build image
docker build -t gcr.io/your-project/eventeaseapi:latest .

# Run locally
docker run -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  gcr.io/your-project/eventeaseapi:latest
```

### Push to Google Container Registry

```bash
# Configure Docker auth
gcloud auth configure-docker gcr.io

# Push image
docker push gcr.io/your-project/eventeaseapi:latest
```

---

## 📚 Useful Commands

### View Logs
```bash
# Real-time logs
gcloud run logs read eventeaseapi --follow --region=us-central1

# Last 50 lines
gcloud run logs read eventeaseapi --limit=50 --region=us-central1

# Filter by severity
gcloud run logs read eventeaseapi --region=us-central1 --limit=100 | grep ERROR
```

### Manage Service
```bash
# Get service details
gcloud run services describe eventeaseapi --region=us-central1

# Update environment variables
gcloud run services update eventeaseapi \
  --region=us-central1 \
  --set-env-vars=KEY=VALUE

# View revisions
gcloud run revisions list --region=us-central1

# Traffic management
gcloud run services update-traffic eventeaseapi \
  --to-revisions=LATEST=100 \
  --region=us-central1
```

### Database Management
```bash
# Connect to database
gcloud sql connect eventeasedb-prod --user=eventeaseadmin

# Backup database
gcloud sql backups create --instance=eventeasedb-prod

# List backups
gcloud sql backups list --instance=eventeasedb-prod

# Restore from backup
gcloud sql backups restore BACKUP_ID \
  --backup-instance=eventeasedb-prod
```

---

## 🔐 Security Best Practices

✅ **Do's:**
- Use Workload Identity Federation (no long-lived keys)
- Store secrets in Secret Manager
- Use service account per environment
- Enable VPC connectors for private databases
- Set memory and CPU limits
- Use least-privilege IAM roles
- Enable audit logging
- Use HTTPS only (enforced by Cloud Run)

❌ **Don'ts:**
- Don't store secrets in code
- Don't use JSON key files for CI/CD
- Don't run containers as root
- Don't expose databases to public internet
- Don't skip CORS configuration

---

## 🐛 Troubleshooting

### Issue: "Permission denied" when accessing secrets

**Solution:**
```bash
# Grant secret access to Cloud Run service account
gcloud secrets add-iam-policy-binding jwt-key \
  --member="serviceAccount:eventeaseapi-sa@PROJECT_ID.iam.gserviceaccount.com" \
  --role="roles/secretmanager.secretAccessor"
```

### Issue: Database connection timeout

**Solution:**
```bash
# Verify Cloud SQL connection name
gcloud sql instances describe eventeasedb-prod --format='value(connectionName)'

# Use Cloud SQL Proxy in Cloud Run deployment
gcloud run services update eventeaseapi \
  --add-cloudsql-instances=PROJECT:REGION:INSTANCE \
  --region=us-central1
```

### Issue: Docker image too large

**Solution:**
Create `.dockerignore` file to exclude unnecessary files:
```
bin/
obj/
.vs/
.git/
node_modules/
```

### Issue: GitHub Actions fails to authenticate

**Solution:**
1. Verify WIF_PROVIDER and WIF_SERVICE_ACCOUNT secrets are correct
2. Check that workload identity binding exists:
```bash
gcloud iam service-accounts get-iam-policy eventeaseapi-github@PROJECT_ID.iam.gserviceaccount.com
```

---

## 📈 Monitoring & Metrics

### View Cloud Run Metrics
```bash
# CPU usage
gcloud monitoring time-series list \
  --filter='metric.type="run.googleapis.com/request_count"' \
  --format=table
```

### Set Up Alerts
```bash
# Create alert for high error rate
gcloud alpha monitoring policies create \
  --notification-channels=CHANNEL_ID \
  --display-name="EventEase High Error Rate" \
  --condition-display-name="Error rate > 5%"
```

---

## 📖 Full Documentation

For more detailed information:
- [GCP_DEPLOYMENT_GUIDE.md](./GCP_DEPLOYMENT_GUIDE.md) - Complete setup guide
- [Google Cloud Run Documentation](https://cloud.google.com/run/docs)
- [Workload Identity Federation](https://cloud.google.com/iam/docs/workload-identity-federation)
- [Cloud SQL Documentation](https://cloud.google.com/sql/docs)

---

## 🆘 Need Help?

1. Check logs: `gcloud run logs read eventeaseapi --follow`
2. Verify configuration: `gcloud run services describe eventeaseapi`
3. Check GitHub Actions: Go to Actions tab in your repo
4. Review error messages in Cloud Run dashboard

---

## ✅ Deployment Checklist

- [ ] Ran setup script successfully
- [ ] Added 4 GitHub secrets
- [ ] Created Cloud SQL instance and database
- [ ] Updated appsettings.Production.json
- [ ] Dockerfile is in repository root
- [ ] .dockerignore file exists
- [ ] GitHub Actions workflow configured
- [ ] Pushed to main branch
- [ ] GitHub Actions job completed successfully
- [ ] Cloud Run service is accessible
- [ ] Logs show no errors
- [ ] API endpoints responding correctly

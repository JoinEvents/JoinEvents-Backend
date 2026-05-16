# GCP & CI/CD Setup - Visual Guide

## 🎯 Complete Overview

```
┌────────────────────────────────────────────────────────────────────┐
│                    GITHUB REPOSITORY (FestMela/EventEase)          │
│                                                                    │
│  ┌──────────────────────────────────────────────────────────────┐ │
│  │ Your Code (main branch)                                      │ │
│  │ ├── EventEase.Api.csproj                                    │ │
│  │ ├── EventEase.Infrastructure.csproj                         │ │
│  │ ├── Dockerfile        ← Multi-stage .NET 8 build            │ │
│  │ ├── .dockerignore      ← Optimization                       │ │
│  │ └── .github/workflows/deploy-gcp.yml ← CI/CD Pipeline       │ │
│  └──────────────────────────────────────────────────────────────┘ │
└───────────────────────────┬──────────────────────────────────────────┘
                            │
                            │ (git push origin main)
                            ▼
                   ┌─────────────────────┐
                   │  GitHub Actions     │
                   │  (CI/CD Pipeline)   │
                   └─────────┬───────────┘
                             │
                ┌────────────┼────────────┐
                ▼            ▼            ▼
           Step 1        Step 2        Step 3
         Checkout    Build Docker    Push to GCR
          Code        Image
                             │
                             ▼
                   ┌──────────────────────┐
                   │ Google Container     │
                   │ Registry (GCR)       │
                   │ gcr.io/project/...   │
                   └──────────┬───────────┘
                              │
                              ▼
                   ┌──────────────────────┐
                   │  Google Cloud Run    │
                   │  (eventeaseapi)      │
                   │  - Auto-scaling      │
                   │  - Serverless        │
                   │  - Pay per request   │
                   └──────────┬───────────┘
                              │
                              ▼
                   ┌──────────────────────┐
                   │  Google Cloud SQL    │
                   │  (EventEaseDb)       │
                   │  - MySQL/PostgreSQL  │
                   │  - Automated backups │
                   │  - Private network   │
                   └──────────────────────┘
```

---

## 📱 Step-by-Step Flow

### Phase 1: Setup (One-Time)

```
┌─ PHASE 1: INITIAL SETUP ──────────────────────────┐
│                                                   │
│  1. Run Setup Script                             │
│     └─→ Choose OS (Windows/Linux/Mac)            │
│     └─→ Enter GitHub & GCP info                  │
│                                                  │
│  2. Script Automatically:                        │
│     └─→ Creates GCP Project                      │
│     └─→ Enables required APIs                    │
│     └─→ Creates service accounts                 │
│     └─→ Sets up Workload Identity                │
│     └─→ Configures IAM roles                     │
│     └─→ Displays GitHub secrets                  │
│                                                  │
│  3. Add GitHub Secrets                           │
│     └─→ Copy 4 secrets from script output        │
│     └─→ Go to GitHub Repo Settings              │
│     └─→ Add to Actions Secrets                  │
│                                                  │
│  4. Optional: Create Database                   │
│     └─→ Run script database setup               │
│     └─→ Or use gcloud commands                  │
│                                                  │
└──────────────────────────────────────────────────┘
```

### Phase 2: Deployment (Automatic)

```
┌─ PHASE 2: EVERY DEPLOYMENT ───────────────────────┐
│                                                   │
│  1. Make Changes Locally                         │
│     └─→ Edit code as needed                      │
│     └─→ Commit changes                           │
│                                                  │
│  2. Push to Main                                 │
│     git push origin main                         │
│     └─→ Triggers GitHub Actions automatically   │
│                                                  │
│  3. GitHub Actions Runs:                         │
│     ├─→ Checkout code                          │
│     ├─→ Authenticate with GCP (via WIF)        │
│     ├─→ Build Docker image                     │
│     ├─→ Push to Google Container Registry       │
│     ├─→ Deploy to Cloud Run                    │
│     └─→ Output service URL                     │
│                                                  │
│  4. Automatic Deployment Complete               │
│     └─→ API live and accessible                 │
│     └─→ Check deployment in Cloud Run console  │
│     └─→ View logs automatically                │
│                                                  │
└──────────────────────────────────────────────────┘
```

---

## 🔐 Security Architecture

```
┌──────────────────────────────────────────────────────┐
│         SECURITY: Workload Identity Federation        │
├──────────────────────────────────────────────────────┤
│                                                      │
│  GitHub (FestMela/EventEase)                        │
│         │                                            │
│         │ (OpenID Connect Token)                     │
│         ▼                                            │
│  ┌─────────────────────────────────┐               │
│  │ WIF Provider (google.com)       │               │
│  │ Verifies GitHub token           │               │
│  └──────────────┬──────────────────┘               │
│                 │                                   │
│                 │ (Exchanges for GCP token)        │
│                 ▼                                   │
│  ┌─────────────────────────────────┐               │
│  │ eventeaseapi-github SA          │               │
│  │ (Time-limited credentials)      │               │
│  └──────────────┬──────────────────┘               │
│                 │                                   │
│                 │ (Can access only assigned        │
│                 │  resources per IAM roles)        │
│                 ▼                                   │
│  ┌─────────────────────────────────┐               │
│  │ Deploy to Cloud Run             │               │
│  │ Push to Container Registry      │               │
│  │ Access Secret Manager           │               │
│  └─────────────────────────────────┘               │
│                                                      │
│  ✅ NO LONG-LIVED KEYS STORED ANYWHERE             │
│  ✅ NO SERVICE ACCOUNT JSON FILES                  │
│  ✅ AUTOMATIC TOKEN ROTATION                       │
│  ✅ AUDIT TRAIL AVAILABLE                          │
│                                                      │
└──────────────────────────────────────────────────────┘
```

---

## 📊 Resource Diagram

```
┌────────────────────────────────────────────────┐
│           GCP PROJECT (eventeaseapi-prod)      │
│                                                │
│  ┌──────────────────────────────────────────┐ │
│  │  Cloud Run                               │ │
│  │  ┌────────────────────────────────────┐ │ │
│  │  │ eventeaseapi Service                │ │ │
│  │  │ URL: https://eventeaseapi-...      │ │ │
│  │  │ Memory: 1Gi                         │ │ │
│  │  │ CPU: 1                              │ │ │
│  │  │ Instances: 0-1000 (auto-scale)     │ │ │
│  │  └────────────────────────────────────┘ │ │
│  └─────────────────────┬────────────────────┘ │
│                        │                       │
│                        ▼ (Cloud SQL Auth)      │
│  ┌──────────────────────────────────────────┐ │
│  │  Cloud SQL Instance                      │ │
│  │  ┌────────────────────────────────────┐ │ │
│  │  │ MySQL 8.0 (eventeasedb-prod)      │ │ │
│  │  │ Database: EventEaseDb             │ │ │
│  │  │ User: eventeaseadmin              │ │ │
│  │  │ Auto backups: Daily               │ │ │
│  │  └────────────────────────────────────┘ │ │
│  └──────────────────────────────────────────┘ │
│                                                │
│  ┌──────────────────────────────────────────┐ │
│  │  Secret Manager                          │ │
│  │  ├── db-connection-string                │ │
│  │  ├── jwt-key                             │ │
│  │  ├── redis-connection                    │ │
│  │  └── azure-storage-connection            │ │
│  └──────────────────────────────────────────┘ │
│                                                │
│  ┌──────────────────────────────────────────┐ │
│  │  Container Registry                      │ │
│  │  ├── gcr.io/.../eventeaseapi:latest    │ │
│  │  └── gcr.io/.../eventeaseapi:sha-...   │ │
│  └──────────────────────────────────────────┘ │
│                                                │
└────────────────────────────────────────────────┘
```

---

## 🔄 CI/CD Pipeline Stages

```
┌─ Stage 1: Trigger ─────────────────────────────────┐
│                                                    │
│  Event: git push origin main                      │
│  Trigger: GitHub Actions                          │
│  Runner: ubuntu-latest                            │
│                                                    │
└────────────────────┬───────────────────────────────┘
                     ▼
┌─ Stage 2: Authenticate ───────────────────────────┐
│                                                    │
│  1. Request OIDC token from GitHub                │
│  2. Send to Google WIF Provider                   │
│  3. Receive GCP access token                      │
│  4. Configure gcloud CLI                          │
│                                                    │
│  ✅ No credentials stored in GitHub               │
│  ✅ Token valid for ~1 hour                       │
│                                                    │
└────────────────────┬───────────────────────────────┘
                     ▼
┌─ Stage 3: Build Docker Image ─────────────────────┐
│                                                    │
│  1. Checkout code                                 │
│  2. Set up Docker Buildx                          │
│  3. Build multi-stage Dockerfile                  │
│     ├── SDK Stage: Build .NET app                │
│     ├── Publish Stage: Prepare for runtime       │
│     └── Runtime Stage: Final image               │
│  4. Layer caching enabled (faster builds)         │
│                                                    │
└────────────────────┬───────────────────────────────┘
                     ▼
┌─ Stage 4: Push to Registry ───────────────────────┐
│                                                    │
│  1. Configure Docker for GCR                      │
│  2. Tag image with:                               │
│     ├── git SHA: eventeaseapi:abc123...         │
│     └── latest: eventeaseapi:latest              │
│  3. Push both tags to gcr.io                      │
│  4. Image is now available globally               │
│                                                    │
└────────────────────┬───────────────────────────────┘
                     ▼
┌─ Stage 5: Deploy to Cloud Run ────────────────────┐
│                                                    │
│  1. gcloud run deploy eventeaseapi                │
│  2. Specify image from GCR                        │
│  3. Set resource limits (1Gi, 1 CPU)             │
│  4. Set environment variables                     │
│  5. Assign service account                        │
│  6. Traffic: 100% to new revision                │
│  7. Allow unauthenticated access (optional)       │
│                                                    │
└────────────────────┬───────────────────────────────┘
                     ▼
┌─ Stage 6: Verify & Report ────────────────────────┐
│                                                    │
│  1. Cloud Run creates new revision                │
│  2. Automatic health checks run                   │
│  3. Route traffic to new revision                 │
│  4. Display service URL                           │
│  5. Deployment complete                           │
│                                                    │
│  ✅ Service live and handling requests            │
│  ✅ Previous revision kept for rollback           │
│  ✅ Logs available in real-time                   │
│                                                    │
└────────────────────────────────────────────────────┘
```

---

## 📈 Scaling Architecture

```
                    Your API Domain
                          │
                   Cloud Load Balancer
                          │
        ┌──────────────────┼──────────────────┐
        │                  │                  │
        ▼                  ▼                  ▼
    Cloud Run         Cloud Run          Cloud Run
    Container 1      Container 2        Container N
    Instance         Instance           Instance
        │                  │                  │
        └──────────────────┼──────────────────┘
                           │
                           ▼
                    Cloud SQL Instance
                    (Single Database)

┌─ Scaling ────────────────────────────────┐
│                                          │
│ Incoming Requests                        │
│    ↓                                     │
│ Cloud Run Auto-scaling                  │
│ • 0 instances at idle                   │
│ • 1 instance at low load                │
│ • 10+ instances at medium load         │
│ • 100+ instances at high load          │
│ • Max 1000 instances                   │
│                                          │
│ Each instance:                          │
│ • Independent container                │
│ • 1 Gi memory                           │
│ • 1 vCPU                                │
│ • Managed by GCP                        │
│                                          │
│ Database Connection:                    │
│ • Cloud SQL Auth (private)              │
│ • Connection pooling managed            │
│ • Automatic failover                    │
│                                          │
└──────────────────────────────────────────┘
```

---

## 💰 Cost Breakdown (Approximate)

```
┌─ Monthly Costs ────────────────────────────┐
│                                            │
│ Cloud Run (1M requests/month)              │
│ • Requests: $0.40 (1M @ $0.0000004)       │
│ • Compute: $10.00 (100 Gi-hours)          │
│ └─→ Subtotal: ~$10.40                     │
│                                            │
│ Cloud SQL (db-f1-micro)                   │
│ • Instance: ~$7-10                        │
│ • Storage (10GB): ~$0.50                  │
│ • Backups: Free (included)                │
│ └─→ Subtotal: ~$7-10                      │
│                                            │
│ Container Registry (storage)               │
│ • Storage: ~$0.02                         │
│ └─→ Subtotal: ~$0.02                      │
│                                            │
│ ├─────────────────────────────────────────┤
│ │ TOTAL: ~$17-20/month                    │
│ ├─────────────────────────────────────────┤
│                                            │
│ Includes:                                 │
│ ✅ 1M+ API requests                       │
│ ✅ Auto-scaling (0-1000 instances)        │
│ ✅ Database with backups                  │
│ ✅ CI/CD pipelines                        │
│ ✅ 1TB egress (free tier)                 │
│                                            │
└────────────────────────────────────────────┘
```

---

## 🚀 Quick Reference Commands

```bash
# View deployment status
gcloud run services describe eventeaseapi

# Stream real-time logs
gcloud run logs read eventeaseapi --follow

# Update environment variables
gcloud run services update eventeaseapi \
  --set-env-vars=KEY=VALUE

# View traffic distribution
gcloud run services describe eventeaseapi --format='value(status.traffic)'

# Rollback to previous revision
gcloud run services update-traffic eventeaseapi \
  --to-revisions=PREVIOUS_REVISION=100

# Check deployment history
gcloud run revisions list

# Monitor resource usage
gcloud monitoring read --filter='metric.type="run.googleapis.com/request_count"'

# View database status
gcloud sql instances describe eventeasedb-prod

# Backup database
gcloud sql backups create --instance=eventeasedb-prod
```

---

## 🎉 Success Indicators

✅ **Deployment Successful When:**
- GitHub Actions workflow completes without errors
- Docker image appears in gcr.io
- Cloud Run shows new revision
- Service URL is accessible
- Logs show no errors
- API endpoints respond correctly
- Database queries work properly

✅ **Production Ready When:**
- All resources deployed
- Monitoring and alerts configured
- Backup strategy in place
- Rollback plan documented
- Team trained on deployment
- Security review complete

---

## 📞 Next Steps

1. **Run Setup Script** → All infrastructure created
2. **Add GitHub Secrets** → Enable CI/CD
3. **Push to Main** → Automatic deployment starts
4. **Monitor Dashboard** → Track deployment progress
5. **Test API** → Verify functionality
6. **Configure Monitoring** → Set up alerts
7. **Scale Resources** → Adjust as needed

---

**Ready to deploy? Start with the setup script!** 🚀

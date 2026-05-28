#!/usr/bin/env pwsh

# GCP Setup Script for EventEase API (PowerShell)
# This script automates the initial setup process for deploying to Google Cloud Run

param(
    [string]$GithubOwner = "",
    [string]$GithubRepo = "",
    [string]$GcpProjectId = "",
    [string]$GcpRegion = "us-central1"
)

$ErrorActionPreference = "Stop"

Write-Host "🚀 EventEase API - GCP Cloud Run Setup Script" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# Function to check if command exists
function Test-Command {
    param($Command)
    $null = Get-Command $Command -ErrorAction SilentlyContinue
    return $?
}

# Check prerequisites
Write-Host "📋 Checking prerequisites..." -ForegroundColor Yellow
if (-not (Test-Command gcloud)) {
    Write-Host "❌ gcloud CLI not installed. Please install it first." -ForegroundColor Red
    Write-Host "Download from: https://cloud.google.com/sdk/docs/install-sdk" -ForegroundColor Red
    exit 1
}
if (-not (Test-Command docker)) {
    Write-Host "❌ Docker not installed. Please install it first." -ForegroundColor Red
    exit 1
}
Write-Host "✅ Prerequisites met" -ForegroundColor Green
Write-Host ""

# Get user inputs if not provided
if (-not $GithubOwner) {
    $GithubOwner = Read-Host "GitHub Repository Owner (e.g., FestMela)"
}
if (-not $GithubRepo) {
    $GithubRepo = Read-Host "GitHub Repository Name (e.g., EventEase)"
}
if (-not $GcpProjectId) {
    $GcpProjectId = Read-Host "GCP Project ID (e.g., eventeaseapi-prod)"
}

Write-Host ""
Write-Host "📝 Configuration Summary:" -ForegroundColor Yellow
Write-Host "  GitHub Repo: $GithubOwner/$GithubRepo"
Write-Host "  GCP Project: $GcpProjectId"
Write-Host "  Region: $GcpRegion"
Write-Host ""
$confirm = Read-Host "Continue with this configuration? (y/n)"
if ($confirm -ne "y" -and $confirm -ne "Y") {
    Write-Host "Cancelled."
    exit 1
}

# Set project
Write-Host ""
Write-Host "🔧 Setting up GCP Project..." -ForegroundColor Yellow
& gcloud config set project $GcpProjectId
$projectNumber = & gcloud projects describe $GcpProjectId --format='value(projectNumber)'
Write-Host "✅ Project set to: $GcpProjectId (Number: $projectNumber)" -ForegroundColor Green

# Enable APIs
Write-Host ""
Write-Host "🔌 Enabling required GCP APIs..." -ForegroundColor Yellow
$apis = @(
    "run.googleapis.com",
    "cloudbuild.googleapis.com",
    "containerregistry.googleapis.com",
    "secretmanager.googleapis.com",
    "sqladmin.googleapis.com",
    "artifactregistry.googleapis.com",
    "iam.googleapis.com"
)

foreach ($api in $apis) {
    Write-Host "  Enabling $api..."
    & gcloud services enable $api --quiet
}
Write-Host "✅ APIs enabled" -ForegroundColor Green

# Create Service Accounts
Write-Host ""
Write-Host "👤 Creating Service Accounts..." -ForegroundColor Yellow

try {
    & gcloud iam service-accounts create eventeaseapi-sa `
        --display-name="EventEase API Service Account" `
        --quiet
} catch {
    Write-Host "  Service account eventeaseapi-sa already exists"
}

try {
    & gcloud iam service-accounts create eventeaseapi-github `
        --display-name="EventEase API GitHub Actions" `
        --quiet
} catch {
    Write-Host "  Service account eventeaseapi-github already exists"
}

Write-Host "✅ Service accounts created" -ForegroundColor Green

# Set up Workload Identity Federation
Write-Host ""
Write-Host "🔐 Setting up Workload Identity Federation..." -ForegroundColor Yellow

$workloadIdentityPool = "github-pool"
$workloadIdentityProvider = "github-provider"

# Create Workload Identity Pool
try {
    & gcloud iam workload-identity-pools create $workloadIdentityPool `
        --project=$GcpProjectId `
        --location=global `
        --display-name="GitHub Pool" `
        --quiet
} catch {
    Write-Host "  Workload Identity Pool already exists"
}

# Create Workload Identity Provider
try {
    & gcloud iam workload-identity-providers create-oidc $workloadIdentityProvider `
        --project=$GcpProjectId `
        --location=global `
        --display-name="GitHub Provider" `
        --attribute-mapping="google.subject=assertion.sub,attribute.actor=assertion.actor,attribute.repository=assertion.repository,attribute.repository_owner=assertion.repository_owner" `
        --issuer-uri="https://token.actions.githubusercontent.com" `
        --attribute-condition="assertion.repository_owner == '$GithubOwner'" `
        --quiet
} catch {
    Write-Host "  Workload Identity Provider already exists"
}

Write-Host "✅ Workload Identity Federation configured" -ForegroundColor Green

# Configure IAM roles
Write-Host ""
Write-Host "👮 Configuring IAM roles..." -ForegroundColor Yellow

$githubSa = "eventeaseapi-github@$GcpProjectId.iam.gserviceaccount.com"
$runSa = "eventeaseapi-sa@$GcpProjectId.iam.gserviceaccount.com"

$roles = @(
    "roles/run.admin",
    "roles/storage.admin",
    "roles/artifactregistry.writer"
)

foreach ($role in $roles) {
    Write-Host "  Granting $role to GitHub Actions service account..."
    & gcloud projects add-iam-policy-binding $GcpProjectId `
        --member="serviceAccount:$githubSa" `
        --role=$role `
        --quiet
}

# Create Workload Identity binding
& gcloud iam service-accounts add-iam-policy-binding $githubSa `
    --project=$GcpProjectId `
    --role="roles/iam.workloadIdentityUser" `
    --member="principalSet://iam.googleapis.com/projects/$projectNumber/locations/global/workloadIdentityPools/$workloadIdentityPool/attribute.repository/$GithubOwner/$GithubRepo" `
    --quiet

Write-Host "✅ IAM roles configured" -ForegroundColor Green

# Display GitHub Secrets information
Write-Host ""
Write-Host "🔑 GitHub Secrets to Add:" -ForegroundColor Cyan
Write-Host "================================================"

$wifProvider = "projects/$projectNumber/locations/global/workloadIdentityPools/$workloadIdentityPool/providers/$workloadIdentityProvider"

Write-Host ""
Write-Host "Add these secrets to your GitHub repository:" -ForegroundColor Yellow
Write-Host "  Settings → Secrets and variables → Actions"
Write-Host ""
Write-Host "GCP_PROJECT_ID:" -ForegroundColor Cyan
Write-Host "  $GcpProjectId"
Write-Host ""
Write-Host "WIF_PROVIDER:" -ForegroundColor Cyan
Write-Host "  $wifProvider"
Write-Host ""
Write-Host "WIF_SERVICE_ACCOUNT:" -ForegroundColor Cyan
Write-Host "  $githubSa"
Write-Host ""
Write-Host "CLOUD_RUN_SERVICE_ACCOUNT:" -ForegroundColor Cyan
Write-Host "  $runSa"
Write-Host ""

# Database setup prompt
Write-Host ""
$setupDb = Read-Host "Do you want to set up Cloud SQL database now? (y/n)"
if ($setupDb -eq "y" -or $setupDb -eq "Y") {
    $dbInstance = Read-Host "Database instance name (default: eventeasedb-prod)"
    if (-not $dbInstance) {
        $dbInstance = "eventeasedb-prod"
    }

    Write-Host ""
    Write-Host "📊 Creating Cloud SQL instance..." -ForegroundColor Yellow
    try {
        & gcloud sql instances create $dbInstance `
            --database-version=MYSQL_8_0 `
            --tier=db-f1-micro `
            --region=$GcpRegion `
            --backup `
            --quiet
    } catch {
        Write-Host "  Database instance already exists"
    }

    Write-Host ""
    Write-Host "📂 Creating database..." -ForegroundColor Yellow
    try {
        & gcloud sql databases create EventEaseDb `
            --instance=$dbInstance `
            --quiet
    } catch {
        Write-Host "  Database already exists"
    }

    Write-Host ""
    $dbUser = Read-Host "Create database user (name, default: eventeaseadmin)"
    if (-not $dbUser) {
        $dbUser = "eventeaseadmin"
    }
    $dbPassword = Read-Host "Database password" -AsSecureString
    $dbPasswordPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToCoTaskMemUnicode($dbPassword))

    try {
        & gcloud sql users create $dbUser `
            --instance=$dbInstance `
            --password=$dbPasswordPlain `
            --quiet
    } catch {
        Write-Host "  User already exists"
    }

    $cloudsqlConnectionName = & gcloud sql instances describe $dbInstance --format='value(connectionName)'

    Write-Host ""
    Write-Host "✅ Cloud SQL configured" -ForegroundColor Green
    Write-Host ""
    Write-Host "Cloud SQL Connection Name (for Cloud Run):" -ForegroundColor Cyan
    Write-Host "  $cloudsqlConnectionName"
}

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "✅ GCP Setup Complete!" -ForegroundColor Green
Write-Host ""
Write-Host "📚 Next Steps:" -ForegroundColor Yellow
Write-Host "  1. Add the secrets listed above to your GitHub repository"
Write-Host "  2. Update appsettings.Production.json with your configuration"
Write-Host "  3. Push changes to main branch to trigger CI/CD"
Write-Host "  4. Check GitHub Actions for deployment status"
Write-Host ""
Write-Host "📖 For detailed information, see GCP_DEPLOYMENT_GUIDE.md" -ForegroundColor Cyan

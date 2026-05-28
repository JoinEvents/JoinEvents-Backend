# GCP Cloud Storage Setup Guide

## Overview
This guide explains how to configure Google Cloud Platform (GCP) Cloud Storage as a replacement for Azure Blob Storage in the EventEase application.

## Prerequisites
- Google Cloud Platform account
- GCP Project
- Service Account with Cloud Storage permissions
- GCP SDK (optional, for testing)

## Step 1: Create a GCP Project

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project or select an existing one
3. Note your **Project ID** (you'll need this)

## Step 2: Enable Cloud Storage API

1. In the GCP Console, go to "APIs & Services" > "Library"
2. Search for "Cloud Storage API"
3. Click on it and press "Enable"

## Step 3: Create a Service Account

1. In GCP Console, go to "APIs & Services" > "Credentials"
2. Click "Create Credentials" > "Service Account"
3. Fill in the service account details:
   - Service account name: `eventease-storage`
   - Service account ID: (auto-generated)
   - Click "Create and Continue"
4. Grant the role: **Storage Object Admin** (or **Storage Admin** for full permissions)
5. Click "Continue" and "Done"

## Step 4: Create and Download Service Account Key

1. In the service account page, go to the "Keys" tab
2. Click "Add Key" > "Create new key"
3. Choose **JSON** format
4. Click "Create" - the JSON file will download automatically
5. Save this file securely (e.g., in your project's secrets folder)

**Important**: Do NOT commit this file to Git. Add it to `.gitignore`

Example structure of the key file location:
```
EventEase/
├── secrets/
│   └── gcp-service-account-key.json  (add to .gitignore)
└── ...
```

## Step 5: Create a GCP Storage Bucket

1. In GCP Console, go to "Cloud Storage" > "Buckets"
2. Click "Create Bucket"
3. Fill in bucket details:
   - **Bucket name**: `eventease-dev-bucket` (must be globally unique)
   - **Location type**: Multi-region (recommended for availability)
   - **Location**: Choose your region (e.g., `us`)
   - **Storage class**: Standard
   - **Access control**: Uniform
   - **Uncheck** "Enforce public access prevention" if needed (or keep checked for security)
4. Click "Create"

Note the **Bucket Name** exactly as created (you'll need this).

## Step 6: Configure appsettings.json

Update `EventEase/appsettings.json` with your GCP details:

```json
{
  "Gcp": {
    "ProjectId": "your-gcp-project-id",
    "BucketName": "eventease-dev-bucket",
    "CredentialsPath": "secrets/gcp-service-account-key.json"
  }
}
```

### Configuration Explanation:
- **ProjectId**: Your GCP Project ID (found in GCP Console dashboard)
- **BucketName**: The exact name of your GCS bucket
- **CredentialsPath**: Path to your downloaded service account key JSON file (relative to the app's working directory)

## Step 7: Add to .gitignore

Ensure your service account key is never committed:

```
# GCP Credentials
secrets/gcp-service-account-key.json
gcp-service-account-key.json
**/gcp-*.json
```

## Step 8: Environment-Specific Configuration

For different environments, create separate appsettings files:

```
EventEase/
├── appsettings.json                    (default/development)
├── appsettings.Development.json        (development overrides)
├── appsettings.Production.json         (production settings)
└── appsettings.Staging.json           (staging settings)
```

Example `appsettings.Production.json`:
```json
{
  "Gcp": {
    "ProjectId": "eventease-prod-project-id",
    "BucketName": "eventease-prod-bucket",
    "CredentialsPath": "/etc/secrets/gcp-service-account-key.json"
  }
}
```

## Step 9: Testing the Configuration

### Using Swagger/API
1. Run the application
2. Navigate to `https://localhost:7010/swagger`
3. Upload a file through any endpoint that uses `IBlobService` (e.g., vendor profile picture upload)
4. Check GCP Console > Cloud Storage > Your Bucket to verify the file appeared

### Using GCP Console
1. Go to GCP Console > Cloud Storage > Your Bucket
2. You should see uploaded files in the structure: `{userId}/{guid}{extension}`

### Using gsutil (Optional)
```bash
# List files in bucket
gsutil ls gs://your-bucket-name/

# Download a file
gsutil cp gs://your-bucket-name/filename.jpg ./local-file.jpg

# Delete a file
gsutil rm gs://your-bucket-name/filename.jpg
```

## Step 10: IAM Permissions

Ensure your service account has these permissions:
- `storage.objects.create`
- `storage.objects.delete`
- `storage.objects.get`
- `storage.objects.list`

These are included in the **Storage Object Admin** role assigned earlier.

## Troubleshooting

### Issue: "Gcp:ProjectId is not configured"
- Check your `appsettings.json` has the correct configuration
- Verify the `ProjectId` matches your GCP project ID

### Issue: "Failed to authenticate with GCP"
- Ensure the service account key JSON file exists at the path specified
- Check the path is correct relative to the app's working directory
- Verify the service account has Cloud Storage permissions

### Issue: "Bucket not found"
- Verify the bucket name is exactly correct (case-sensitive)
- Confirm the bucket exists in GCP Console
- Check the service account has access to the bucket

### Issue: "Permission denied" errors
- Go to GCP Console > Cloud Storage > Bucket > Permissions
- Add your service account email with **Storage Object Admin** role
- Wait a minute for permissions to propagate

## Monitoring

### Cloud Storage Metrics
Monitor your usage in GCP Console:
1. Go to Cloud Storage > Buckets > Your Bucket
2. View:
   - Storage usage
   - Number of objects
   - Operations metrics
   - Network transfer

### Cost Estimation
GCP Cloud Storage pricing:
- **Storage**: $0.020 per GB/month (Standard)
- **Class A Operations** (uploads): $0.005 per 1,000 operations
- **Class B Operations** (downloads): $0.0004 per 1,000 operations
- **Network egress**: Variable based on destination

Use [GCP Pricing Calculator](https://cloud.google.com/pricing/calculator) for estimates.

## Switching Back to Azure (if needed)

To revert to Azure Blob Storage:

1. Update `Program.cs`:
```csharp
builder.Services.AddSingleton<IBlobService, BlobService>();
```

2. Update `appsettings.json` to use Azure configuration

3. Rebuild and run

## Additional Resources

- [Google Cloud Storage Documentation](https://cloud.google.com/storage/docs)
- [GCP .NET Client Library](https://cloud.google.com/dotnet/docs/reference/Google.Cloud.Storage.V1/latest)
- [Service Accounts Guide](https://cloud.google.com/iam/docs/service-accounts)
- [GCP Pricing](https://cloud.google.com/pricing)

---

**Next Steps**: Provide the following values from your GCP setup to complete the configuration:
1. GCP Project ID
2. GCP Bucket Name
3. Service Account Key (securely share or keep locally)

# Azure Storage for uploads

Every file the app accepts — profile photos, package galleries, vendor portfolio images,
verification documents, support attachments — is stored in an Azure Storage account.

Before this, uploads went to two places, neither of them workable in production:

- **Local disk** (`storage/` on the API container) for verification documents and support
  attachments. Container filesystems are ephemeral and not shared between replicas, so those
  files disappeared on restart and were invisible to every other instance.
- **A made-up URL** (`https://storage.joinevents.com/uploads/...`) for package images, written
  to the row whether or not the upload succeeded. That domain does not serve anything.

---

## What we need from the Azure side

Everything below is one storage account plus two containers. Nothing else is required.

### 1. A storage account

| Setting | Value | Why |
|---|---|---|
| Kind | StorageV2 (general purpose v2) | Blob tier support, lifecycle rules |
| Performance | Standard | Images and PDFs; premium buys nothing here |
| Replication | LRS (dev) / ZRS or GRS (prod) | Pick to taste |
| Region | Same region as the API | Egress between regions is billable and slower |
| **Allow Blob anonymous access** | **Enabled** | Required for the public media container below |
| Minimum TLS | 1.2 | |

> The account-level *"Allow Blob anonymous access"* toggle must be on, or the media container
> cannot be set to public and every image URL 404s. It only *permits* per-container public
> access; the documents container stays private regardless.

### 2. Two containers

| Container | Access level | Holds |
|---|---|---|
| `joinevents-media` | **Blob** (anonymous read) | Avatars, package images, vendor portfolio images |
| `joinevents-documents` | **Private** | Verification documents, support attachments |

The split is deliberate. Image URLs are written onto rows (`User.Avatar`,
`PackageImage.Url`) and read back in a dozen places, so they must not expire — which means the
media container serves anonymous reads. Documents are never public: the API stores only their
path and signs a short-lived SAS URL each time one is displayed.

The app creates both containers on first use if they are missing, provided the identity has
permission. Creating them up front is fine too.

### 3. Access for the API — pick one

**Option A — managed identity (preferred; nothing to rotate).**

1. Turn on a system-assigned managed identity on the App Service / Container App running the API.
2. Grant that identity the **Storage Blob Data Contributor** role, scoped to the storage account.
3. Give the app `AzureStorage__AccountName` only. No key or connection string anywhere.

With this, SAS links for documents are signed with a *user delegation key* the app fetches from
Azure, so there is still no account key in play.

**Option B — connection string.**

Give the app `AzureStorage__ConnectionString` from *Access keys*. Simpler, but the key is a
credential that has to be stored and rotated. Use for local or throwaway environments.

### 4. Optional — CDN / custom domain

Put Azure Front Door or Azure CDN in front of `joinevents-media` and set
`AzureStorage__PublicBaseUrl` to its hostname. Image URLs are then built from that instead of
`https://<account>.blob.core.windows.net`. Purely an optimisation; skip it to start.

### 5. Optional — lifecycle rule

Deletes are handled in the app (removing a package image or replacing an avatar deletes the old
blob). A lifecycle rule moving blobs untouched for 90+ days to Cool is a reasonable extra.

### 6. CORS

Not required. Browsers load these URLs with plain `<img>` and `<a>` tags, which are not
subject to CORS. Add a rule only if the app later fetches blobs with `fetch()`/XHR.

---

## What to set on the API

Supply these as environment variables / app settings at deploy time. Nothing goes in
`appsettings.json`.

```
Storage__Provider=Azure

# Option A — managed identity (preferred)
AzureStorage__AccountName=joineventsmedia

# Option B — connection string, instead of AccountName
# AzureStorage__ConnectionString=DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net

# Defaults shown; override only if you named things differently
AzureStorage__ContainerName=joinevents-media
AzureStorage__DocumentsContainerName=joinevents-documents
AzureStorage__MediaPublicAccess=true
AzureStorage__SasTtlMinutes=60
AzureStorage__MaxUploadBytes=10485760

# Only if a CDN is in front of the media container
# AzureStorage__PublicBaseUrl=https://cdn.joinevents.com
```

Startup **fails** in Production if neither `AzureStorage__AccountName` nor
`AzureStorage__ConnectionString` is set — an API that accepts pictures with nowhere to put them
is worse than one that refuses to boot.

`Storage__Provider=Gcp` restores the previous Google Cloud Storage path, so this can be rolled
back with one setting. Note that even on that path there is no local-disk storage: documents go
to GCS objects, not to the container filesystem.

There is no local-disk implementation at all any more. Every upload path, under either provider,
writes to object storage.

### Local development

Run [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite) and set:

```
AzureStorage__ConnectionString=UseDevelopmentStorage=true
```

---

## How the app uses it

| Upload | Endpoint | Container | What is stored on the row |
|---|---|---|---|
| Avatar | `POST /api/v1/profile/avatar` | media | Permanent URL |
| Package images | `POST /api/v1/vendor/packages/{id}/images` | media | Permanent URL |
| Portfolio / inclusion images | `POST /api/v1/files/images` | media | Permanent URL |
| Verification documents | `POST /api/v1/vendor/verification/upload` | documents | Storage path |
| Support attachments | `POST /api/v1/support/upload` | documents | Storage path |

Blob names are `{userId}/{guid}{ext}` for media and `{prefix}/{guid}-{name}{ext}` for documents.
The GUID is what makes them unique and unguessable; the original filename is kept only as a
readable suffix.

Documents are turned into a signed URL at the moment they are displayed. **Never write a signed
URL to the database** — it expires. Store the path the upload returned.

### Existing data

Rows written before this change hold either an absolute URL or a server-relative `/files/...`
path. Both are passed through untouched, so nothing needs migrating. Files that were on a
container's local disk are already gone and cannot be recovered; those vendors will have to
re-upload. Package images pointing at `storage.joinevents.com` were never real and can be
cleared.

### A note on the media container

Blob-level anonymous read means anyone holding an exact blob URL can fetch it, without a token.
That is the trade for permanent image URLs. The ownership checks on `/api/v1/files/*` still stop
one account enumerating another's uploads through the API, and blob names carry a GUID, but
anything genuinely confidential belongs in the documents container — not in media.

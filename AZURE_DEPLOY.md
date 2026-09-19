# Deploying the JoinEvents API to Azure (free tier)

Target: **Azure Container Apps** for the API, **Azure SQL Database free offer** for the
database. Both have a standing monthly free allowance — this is not a trial that expires.

| Piece | Free allowance |
| --- | --- |
| Container Apps | 180,000 vCPU-seconds, 360,000 GiB-seconds, 2M requests per subscription per month |
| Azure SQL | 100,000 vCore-seconds, 32 GB data, 32 GB backup per database per month, up to 10 databases |
| Container image | GitHub Container Registry (free). **Not** Azure Container Registry, which bills from the Basic tier up. |

Nothing here needs a code change: the existing `Dockerfile` already listens on 8080,
which is what Container Apps expects.

---

## Before you start

You need the Azure CLI and an Azure subscription:

```bash
az login
az account set --subscription "<your subscription>"
az extension add --name containerapp --upgrade
```

---

## Step 1 — Pick a region

Use one region for everything, so the API and database are not talking across the
planet. `centralindia` is the sensible default for Indian users.

Check the Azure SQL free offer is available there before committing — it is not offered
in every region. If it is not, pick the nearest region where it is, and use that same
region for Container Apps.

---

## Step 2 — Create the database (portal)

Do this one in the **portal**, not the CLI: the free offer is a checkbox in the create
flow, and the CLI flag names for it have moved between `az` versions.

1. **Create a resource → SQL Database**.
2. Create a new logical server in the region from step 1. Note the admin login and password.
3. On the **Compute + storage** step, choose **General Purpose → Serverless**.
4. Tick **Apply the free offer**.
5. When asked what to do if the monthly limit is reached, choose
   **Auto-pause the database until next month** — *not* "continue for additional charges".
   This is what makes a runaway impossible to bill you for.
6. After creation, open the server's **Networking** tab and enable
   **Allow Azure services and resources to access this server**. Container Apps has no
   fixed outbound IP, so a per-IP firewall rule will not work.

Then build the connection string. **Set `Connect Timeout=60`** — the default 15 seconds
is shorter than the time a paused serverless database takes to wake, so the first request
after idle would fail before it ever had a chance:

```
Server=tcp:<server>.database.windows.net,1433;Initial Catalog=<db>;Persist Security Info=False;User ID=<admin>;Password=<password>;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connect Timeout=60;
```

The API already retries transient SQL failures (`EnableRetryOnFailure`, 5 attempts), which
covers the `40613` "database unavailable" error Azure returns while resuming.

---

## Step 3 — Provision the API

```bash
export LOCATION='centralindia'
export SQL_CONNECTION_STRING='<the string from step 2>'
export JWT_KEY="$(openssl rand -base64 48)"
export BOOTSTRAP_ADMIN_EMAIL='you@example.com'
export BOOTSTRAP_ADMIN_PASSWORD='<a strong password you choose>'
export ALLOWED_ORIGINS='https://<your-frontend-host>'
export IMAGE='mcr.microsoft.com/k8se/quickstart:latest'   # placeholder for the first run

./azure/provision.sh
```

The placeholder image just gets the app created; step 5 replaces it with your build.

Keep `JWT_KEY` somewhere safe — rotating it signs every existing user out.

---

## Step 4 — Apply the database schema

Migrations do **not** run at startup (`Database:MigrateOnStartup` defaults to false),
because EF Core's `Migrate()` is not safe to run from several instances at once. Apply
them from your machine, pointed at the Azure database:

```bash
export ConnectionStrings__DefaultConnection='<the string from step 2>'
dotnet ef database update --project EventEase.Infrastructure --startup-project EventEase
```

If this is the first deploy, generate the outstanding migration first — the hardening
work changed the EF model and no migration was committed for it:

```bash
dotnet ef migrations add AddOperationalIndexes --project EventEase.Infrastructure --startup-project EventEase
```

A note if you are pointing this at a database that already holds rows: a unique index is
now declared on `Users.Email`, so check for duplicates first, since the migration fails if
any exist —
`SELECT Email, COUNT(*) FROM Users GROUP BY Email HAVING COUNT(*) > 1;`

---

## Step 5 — Wire up deploys from GitHub

Create a federated credential so Actions can log in without a stored password:

```bash
SUBSCRIPTION_ID=$(az account show --query id -o tsv)

az ad app create --display-name joinevents-deploy
APP_ID=$(az ad app list --display-name joinevents-deploy --query '[0].appId' -o tsv)
az ad sp create --id "$APP_ID"

az role assignment create \
  --assignee "$APP_ID" \
  --role Contributor \
  --scope "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/joinevents-rg"

az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:JoinEvents/JoinEvents-Backend:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'

echo "AZURE_CLIENT_ID       = $APP_ID"
echo "AZURE_TENANT_ID       = $(az account show --query tenantId -o tsv)"
echo "AZURE_SUBSCRIPTION_ID = $SUBSCRIPTION_ID"
```

In the repository settings add those three as **secrets**, and add two **variables**:

| Variable | Value |
| --- | --- |
| `AZURE_APP_NAME` | `joinevents-api` |
| `AZURE_RESOURCE_GROUP` | `joinevents-rg` |

Then run **Actions → Deploy to Azure Container Apps → Run workflow**. It builds, tests,
pushes the image to GitHub Container Registry and points the container app at it.

Once a manual run has succeeded, uncomment the `push` trigger at the top of
`.github/workflows/deploy-azure.yml` to deploy on every push to `main`.

---

## Step 6 — Point the frontend at it

The provisioning script prints the API URL. Put it in the frontend's
`src/environments/environment.ts` as `apiUrl` (with the `/api/v1` suffix), then re-run
`provision.sh` with `ALLOWED_ORIGINS` set to the frontend's own origin — otherwise CORS
blocks every call and the app looks broken for a reason the browser console explains
badly.

---

## Three things that will cost you the free tier

**Never point an uptime monitor at `/health`.** It opens a database connection. A monitor
hitting it every minute keeps the serverless database permanently awake, which burns the
whole 100,000 vCore-second monthly allowance in roughly two days. Use `/health/live`,
which touches nothing. For the same reason `provision.sh` deliberately configures **no
HTTP health probe** on the container app — the default TCP probe is enough.

**Expect a slow first request.** With `--min-replicas 0` the container scales to zero and
the database auto-pauses. The first request after an idle period pays both: roughly a
minute in total. Every request after that is normal. If you are about to demo it, open the
app a minute or two beforehand to warm it. Raising `--min-replicas` to 1 removes the
container half of the delay but costs roughly 720 vCPU-hours a month against a 50-hour free
grant, so it is not free.

**Do not choose "continue for additional charges"** on the database's limit behaviour. That
is the setting that turns a mistake into a bill.

---

## Budget the database uptime

The free grant works out to about **55 hours of active database time per month** at the
0.5 vCore minimum — roughly 1.8 hours a day. That is plenty for development and demos, and
nowhere near enough to leave the database awake continuously. Treat it as a budget you
spend deliberately, and set a **zero-rupee budget alert** on the subscription
(Cost Management → Budgets) so you hear about it immediately if anything starts billing.

If the cold start turns out to be the thing that annoys you most, Neon's Postgres free tier
wakes in well under a second and has Azure regions — at the cost of a provider swap. The
trade-off is written up in the launch plan.

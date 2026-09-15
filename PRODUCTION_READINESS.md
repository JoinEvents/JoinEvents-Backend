# Production readiness changes

This document covers the security and operability work applied to the backend, and the
steps an operator must take before deploying it.

## Required actions before deploying

These are **not optional** — the application will refuse to start without the first two.

### 1. Configuration that must be supplied

`appsettings.json` no longer contains `${VAR}` placeholders. .NET configuration never expanded
those, so they were being read as literal strings — most seriously, the literal
`${EVENT_EASE_JWT_KEY}` could end up being used as the JWT signing key, a value published in
this repository. Startup now fails fast instead.

| Setting | Environment variable | Notes |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | Required. |
| `Jwt:Key` | `Jwt__Key` | Required. Minimum 32 bytes of random secret. |
| `AllowedOrigins` | `AllowedOrigins__0`, `AllowedOrigins__1`, … | Required in Production. |
| `Bootstrap:AdminEmail` | `Bootstrap__AdminEmail` | Used once, only if no admin exists. |
| `Bootstrap:AdminPassword` | `Bootstrap__AdminPassword` | Used once, only if no admin exists. |
| `Authentication:Google:ClientId` | `Authentication__Google__ClientId` | Required for Google sign-in. |
| `Authentication:Facebook:AppId` / `AppSecret` | `Authentication__Facebook__*` | Required for Facebook sign-in. |
| `Database:MigrateOnStartup` | `Database__MigrateOnStartup` | Default `false`; see below. |
| `Payments:AllowSimulator` | `Payments__AllowSimulator` | Default `false`; see below. |

The old `EVENT_EASE_JWT_KEY` and `EVENT_EASE_DB_CONNECTION` variables are no longer read.
Use the standard names above.

### 2. Rotate compromised credentials

`DbInitializer` previously re-set `admin@gmail.com` to the password `test` **on every single
startup**, and created several other accounts with the same password. Anyone who knew this
could sign in as an administrator, and any password an operator set was reverted by the next
deploy.

Before or immediately after deploying:

```sql
-- Review every account that the old seed could have created.
SELECT Id, Email, Role, AccountStatus FROM Users
WHERE Email IN ('admin@gmail.com','support@gmail.com','customer@gmail.com','vendor@gmail.com',
                'admin@test.com','support@test.com','user@test.com','vendor@test.com');
```

Delete the ones you do not recognise and reset the password on any you keep. Assume any data
these accounts could reach has been exposed.

### 3. Generate and apply a migration

The EF model changed (indexes on `Bookings`, `RefreshTokens`, `Payments`, `Users`,
`BookingLogs`, `Notifications`, `ChatMessages`). No migration is included because it could not
be generated or verified in the environment these changes were written in. Run:

```bash
dotnet ef migrations add AddOperationalIndexes --project EventEase.Infrastructure --startup-project EventEase
dotnet ef database update --project EventEase.Infrastructure --startup-project EventEase
```

**Check for duplicate emails first** — a unique index is now declared on `Users.Email`, and the
migration will fail if duplicates exist:

```sql
SELECT Email, COUNT(*) FROM Users GROUP BY Email HAVING COUNT(*) > 1;
```

Duplicates are themselves a bug: login resolves users with `FirstOrDefault` on email, so which
account you land in is arbitrary. Merge or remove them before applying the index.

### 4. Migrations no longer run automatically

`Database:MigrateOnStartup` defaults to `false`. EF Core's `Migrate()` is not safe to run from
several instances at once, and the previous startup path also executed roughly fifteen raw
`ALTER TABLE` statements on every boot inside a `try/catch` that swallowed failures.

Run migrations as a separate release step. Set `Database__MigrateOnStartup=true` only for
single-instance environments.

One piece of removed startup SQL was a **one-off data repair**, not a schema change: it rewrote
`ChatThreads.VendorId` values that held a `Vendors.Id` instead of a `Users.Id`. If your data
still needs it, run it once, manually:

```sql
UPDATE ChatThreads SET VendorId = v.UserId
FROM ChatThreads t JOIN Vendors v ON t.VendorId = v.Id
WHERE t.VendorId NOT IN (SELECT Id FROM Users);
```

### 5. Payments

`SimulatorGateway` is a development stub — it has never been a real payment integration. The
application now **refuses to start in Production** unless `Payments:AllowSimulator` is `true`.

Set it to `true` only as a deliberate, temporary acknowledgement that payments do not work.
Otherwise implement `IPaymentGateway` against a real provider. `VerifyPaymentAsync` must ask
the provider for the payment's status, or validate a signed webhook — it must never trust a
status supplied by the client.

## API changes the frontend must follow

| Endpoint | Change |
| --- | --- |
| `POST /api/v1/booking` | Body is now `CreateBookingRequest`. Monetary fields are **rejected** — the server prices the booking. Send `vendorId`, `eventDate`, `packageId`, `serviceIds`, `guestCount`, `mealPreference`, `eventName`, `venue`, `city`. |
| `POST /api/v1/payment/confirm` | No longer accepts `status`. Send only `providerRef`. |
| `PATCH /api/v1/bookings/{id}/status` | Rejects unknown statuses and illegal transitions. `Paid`/`Settled` cannot be set here. |
| `POST /api/v1/bookings/{id}/cancel` | `cancelledBy` and refund amounts are ignored for non-staff callers; the server derives them. |
| `GET /api/v1/bookings` | Returns `{ items, page, pageSize, total, totalPages }` instead of a bare array. Accepts `page` and `pageSize`. The `userId` parameter is now staff-only. |
| `GET /api/v1/vendor/bookings` | Same paged envelope. |
| Login / register / social login | Response now includes `refreshToken` and `refreshTokenExpiresAt`. |
| `POST /api/v1/auth/refresh` | **New.** `{ refreshToken }` → new token pair. |
| `POST /api/v1/auth/logout` | **New.** Revokes one refresh token. |
| `POST /api/v1/auth/logout-all` | **New.** Revokes every session for the caller. |
| `POST /api/v1/auth/social-login` | Google now requires an **ID token**, not an access token. |
| `POST /api/v1/profile/avatar` | Returns an object-storage blob path instead of a local URL. |
| File upload/download/delete | Moved to `/api/v1/files/*` and restricted to the caller's own files. |
| `/hubs/chat` | Pass the JWT as `?access_token=` (the standard SignalR pattern). `JoinThread` now fails for non-participants. |
| `/health` | Readiness (checks the database). `/health/live` is liveness. |

Auth endpoints are rate limited to 10 requests/minute per caller; everything else to
300/minute. Both return `429` with a JSON body.

## What changed and why

### Critical
- **Seeded administrator removed.** Demo fixtures are gated behind `Database:SeedDemoData` and
  never run in Production. The bootstrap admin is created only when no administrator exists and
  its password is never reset on an existing account.
- **Booking prices are computed server-side** by `BookingPricingService` from the vendor's own
  catalogue. `Create` previously bound the `Booking` entity straight from the request body, so
  the caller chose their own `TotalAmount`, `UserId` and fee fields.
- **Payment confirmation** is idempotent, checks that the caller is a party to the booking, and
  reads the outcome from the gateway instead of the request body.
- **Booking status** goes through a transition table; `Paid` and `Settled` are reserved to the
  payment flow.
- **Object-level authorization.** Holding the `Vendor` role no longer grants access to every
  booking on the platform. `RescheduleBooking` and `GetBookingLogs` had no ownership check at
  all; `AddDamage` let any vendor bill any booking; `UserController` download/delete accepted
  any blob path.
- **JWT signing key** must be present and at least 32 bytes, or startup fails.
- **Rate limiting** added.

### High
- Audit logging now actually records: the middleware moved after `UseAuthentication` (it ran
  before it, so `context.User` was always anonymous and no row was ever written) and logs after
  the response status is known.
- `UseExceptionHandler` moved to the front of the pipeline.
- Google sign-in validates an ID token with the audience pinned to our client ID; Facebook uses
  `debug_token` to confirm the token was issued to this app. The previous access-token check
  accepted tokens minted by any Google or Facebook application.
- Refresh tokens are returned, hashed at rest, rotated on use, and revoked as a family on
  reuse. Suspended and banned accounts cannot log in or refresh.
- SignalR: JWT is read from the query string, `JoinThread` and `SendMessage` verify thread
  membership.
- Booking creation and payment confirmation run in transactions.
- Avatars go to object storage instead of a container's ephemeral disk, and uploads are checked
  for real image magic bytes.
- Legacy unsalted SHA-256 password hashes are now genuinely upgraded to BCrypt on successful
  login (the old code claimed to do this in a comment but did not).

### Medium
- Pagination on booking list endpoints; listing bookings no longer writes to the database.
- Indexes added for the lookups on the hot paths.
- Internal exception messages removed from HTTP responses.
- Per-request claim logging removed; `Console.WriteLine` replaced with Serilog.
- CI restored (`.github/workflows/ci.yml`: build, test, secret scan). The stale
  `azure-pipelines.yml` (targeting `master`, .NET Framework and IIS) was removed.
- Tests added for pricing, status transitions and refresh-token rotation. The previous
  integration test asserted that the hardcoded `test` password worked, encoding the
  vulnerability as a requirement; it was removed.

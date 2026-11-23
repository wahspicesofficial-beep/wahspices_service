# WAH! Spices backend: secrets, encryption and database hardening

## 1. ROTATE THESE NOW (they were stored in plain text in appsettings*.json)

The values below have been removed from the config files. Anyone who has seen a copy of the project folder (a zip, a
backup, a shared drive or a hosting file manager) can still use them until you change them. **Rotate every one of them:**

| Credential | Where to rotate it | Then set it as |
|---|---|---|
| **Hostinger SMTP password** for `yash@wahspices.in` | Hostinger hPanel → Emails → yash@wahspices.in → Change password | `Email__Password` |
| **Gmail app password** for `wahspicesofficial@gmail.com` (current sender for OTP / order emails, smtp.gmail.com:587) | Google Account → Security → 2-Step Verification → App passwords — revoke the old one and create a new one if it is ever exposed | `Email__Password` |
| **Old hosted DB password** (databaseasp.net, login `db55965`) | databaseasp.net control panel → database `db55965` → change the user password (or delete the user and create a least-privilege one, see section 5) | `ConnectionStrings__DefaultConnection` |
| **JWT signing key** (`WAH-SPICES-SUPER-SECRET-KEY-2026…`) | Already replaced locally by a new random 64-byte key. Production must use its **own** new key. Changing it logs out every customer and admin, which is intended. | `Jwt__SecretKey` |
| **Local Docker `sa` password** (was in appsettings.Development.json) | `ALTER LOGIN sa WITH PASSWORD = '…'` on the `wahmasala-sql` container (dev only, lower risk) | only used for migrations |

Razorpay and MSG91 never had real values committed (they were placeholders). Put the real ones only in secrets.

## 2. Where secrets live

`appsettings.json` contains **placeholders only** (`SET-IN-USER-SECRETS-OR-ENV …`). Real values come from:

* **Development:** `dotnet user-secrets` (stored in your user profile under `~/.microsoft/usersecrets/cc950312-1e17-4f7f-8ce3-f98f5ed87571/secrets.json`, outside the project folder).
* **Production:** environment variables on the host. Use a double underscore `__` in place of `:`.

| Config key | Production env var | Required | Notes |
|---|---|---|---|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | yes | Least-privilege login, `Encrypt=True; TrustServerCertificate=False` |
| `Jwt:SecretKey` | `Jwt__SecretKey` | yes | ≥ 64 random bytes: `openssl rand -base64 64` |
| `Security:DataEncryptionKey` | `Security__DataEncryptionKey` | yes | 32 random bytes, base64: `openssl rand -base64 32` |
| `Security:DataEncryptionKeyId` | `Security__DataEncryptionKeyId` | no (default `k1`) | Change it when you rotate the key |
| `Security:PreviousDataEncryptionKeys:<id>` | `Security__PreviousDataEncryptionKeys__k1` | only while rotating | Old key(s) kept for decrypting |
| `Razorpay:KeyId` / `Razorpay:KeySecret` | `Razorpay__KeyId` / `Razorpay__KeySecret` | yes in Production | |
| `Email:Username` / `Email:Password` | `Email__Username` / `Email__Password` | yes unless `Email:DeliveryMode` = `Disabled` | `Outbox` mode is refused in Production |
| `Sms:ApiKey` | `Sms__ApiKey` | when SMS OTP is enabled | |
| `Google:ClientId` | `Google__ClientId` | when Google sign-in is enabled | Not secret, but kept out of the repo |
| `WhatsApp:AccessToken` | `WhatsApp__AccessToken` | only for WhatsApp campaigns | Permanent System User token (Meta Business Manager), `whatsapp_business_messaging` permission |
| `WhatsApp:PhoneNumberId` | `WhatsApp__PhoneNumberId` | only for WhatsApp campaigns | WhatsApp Manager → API setup → Phone number ID (not secret) |
| `WhatsApp:TemplateName` | `WhatsApp__TemplateName` | only for WhatsApp campaigns | Name of an **approved** Marketing template; also set `WhatsApp:Enabled=true` |
| `Marketing:UnsubscribeKey` | `Marketing__UnsubscribeKey` | no | Signs unsubscribe links; falls back to a key derived from `Jwt:SecretKey` (rotating that invalidates old links) |

**Startup check:** in Production the API **refuses to start** when a required secret is missing, still a placeholder or too
short (JWT key under 64 bytes, data key not exactly 32 bytes). It also refuses when the connection string uses `sa`,
`TrustServerCertificate=True` or no encryption, or when email delivery is set to `Outbox`. In Development it fails only on
DB/JWT/data key and logs warnings for the rest.

### Development setup (already done on this machine)

```bash
cd WahSpices.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1434; Database=WahSpices; User Id=wahspices_app; Password=<app pw>; Encrypt=True; TrustServerCertificate=True; MultipleActiveResultSets=True;"
dotnet user-secrets set "Jwt:SecretKey" "$(openssl rand -base64 64 | tr -d '\n')"
dotnet user-secrets set "Security:DataEncryptionKey" "$(openssl rand -base64 32)"
dotnet user-secrets set "Security:DataEncryptionKeyId" "k1"
dotnet user-secrets set "Email:Password" "<new SMTP password>"     # only needed if DeliveryMode is not Outbox
dotnet user-secrets list      # shows the values, so never paste its output anywhere
```

### Production host

* **IIS / Windows (Hostinger, databaseasp.net-style hosting):** set the variables in the site's `web.config`
  `<aspNetCore><environmentVariables>` block **on the server only**, or under IIS Manager → Configuration Editor →
  `system.webServer/aspNetCore` → environmentVariables. Never commit that web.config.
* **Linux / systemd:** create `/etc/wahspices/api.env` with `chmod 600`, owned by the service user, and reference it with
  `EnvironmentFile=/etc/wahspices/api.env`.
* **Docker:** `--env-file` (file kept outside the repo) or Docker/Kubernetes secrets.
* Always set `ASPNETCORE_ENVIRONMENT=Production`.

### Customer communications (Admin → Marketing)

* **Email campaigns** use the same Gmail account as order/OTP mail but go through a throttled background
  dispatcher (`Services/Marketing/MarketingDispatcherService.cs`). Limits are edited in Admin → Marketing → Campaigns →
  *Limits* (defaults: 20/min, 100/hour, 400/24 h, Gmail limit 500/24 h with 100 kept free for transactional mail).
  On a Gmail quota error (`5.4.5 Daily user sending quota exceeded`) email campaigns pause for 24 h and continue automatically.
  Every campaign email has a signed one-click unsubscribe link (`/unsubscribe?token=…`, `List-Unsubscribe` header).
* **WhatsApp** (Meta Cloud API) stays disabled until configured. Development:
  ```bash
  cd WahSpices.API
  dotnet user-secrets set "WhatsApp:Enabled" "true"
  dotnet user-secrets set "WhatsApp:AccessToken" "<permanent system-user token>"
  dotnet user-secrets set "WhatsApp:PhoneNumberId" "<phone number id>"
  dotnet user-secrets set "WhatsApp:TemplateName" "<approved template name>"
  dotnet user-secrets set "WhatsApp:TemplateLanguage" "en"          # the template's language code
  # Body parameters {{1}}, {{2}} … in order — any of: name, title, body, link, coupon (default: name, title)
  dotnet user-secrets set "WhatsApp:TemplateParams:0" "name"
  dotnet user-secrets set "WhatsApp:TemplateParams:1" "title"
  # dotnet user-secrets set "WhatsApp:TemplateImageHeader" "true"  # template has an IMAGE header
  ```
  Production: the same keys as environment variables (`WhatsApp__AccessToken`, `WhatsApp__TemplateParams__0`, …).
  Only message customers who agreed to WhatsApp updates (Meta policy); the messaging tier (250 / 1K / 10K per 24 h)
  goes into the WhatsApp *Provider daily limit*.
* Run the dispatcher on **one** instance only (`Marketing__Dispatcher__Enabled=false` on the others).
* The real-time hub `/hubs/notifications` accepts the customer access token in `?access_token=` **only on that path**
  (browsers can't send headers on WebSockets). If a reverse proxy logs query strings, exclude that path from access logs.

## 3. Field-level encryption at rest

The following columns are encrypted with **AES-256-GCM**, with a fresh random nonce per value and the column name bound in as
associated data. They are stored as `enc:v1:<keyId>:<base64url>`:

| Table | Columns |
|---|---|
| Employees | AadharNumber, PanNumber, BankAccountNo, BankIfsc, PfNumber, EsicNumber |
| EmployeeDocuments | DocumentNumber |
| Vendors | BankAccountNo, BankIfsc |
| CrmCustomers | Pan |
| InvoiceSettings | Pan, BankAccount, BankIfsc, BankBranch (your own business details; shown in full on invoices) |
| AdminUsers | TotpSecret, TotpBackupCodes (backup codes are also hashed) |

* The application code is unchanged: EF Core value converters encrypt on save and decrypt on load
  (`WahSpices.Infrastructure/Security/FieldEncryption.cs`, configured in `AppDbContext`).
* **Masking:** admin lists and details return masked values (`XXXX-XXXX-1234`, `XXXXXX234F`). An unchanged masked value sent
  back by an edit form never overwrites the real one. To see the full value, use **Reveal**:
  `POST /api/admin/sensitive/{employees|employee-documents|vendors|crm-customers}/{id}/reveal` `{ "reason": "…" }`.
  It needs the owning module permission (Staff, Purchase or Customers). Every reveal writes an `AuditLogs` row
  (`Action = reveal-sensitive`, who, which record, which fields, reason, IP), and responses are `Cache-Control: no-store`.
* The payroll bank-transfer file and payslips decrypt server-side where full values are legitimately needed.
* **Left in plain text on purpose**, because they must be searchable, joinable or unique-indexed: emails, phone numbers, GSTINs,
  names and addresses. Passwords were never stored in any form other than BCrypt hashes.
* **Backfill job** (`EncryptedFieldsBackfill`) runs on every start. It encrypts any plaintext value, re-encrypts values written
  under an older key id, skips values already on the current key, and logs counts. It is idempotent, and each UPDATE is guarded
  by `WHERE Id=@id AND col=@old`.
* **BACK UP THE DATA KEY** in a password manager. Without it the encrypted columns cannot be recovered. Development and
  Production must use **different** keys. A database backup is useless to a thief who doesn't also have the key.

### Rotating the data-encryption key

1. Generate a new key: `openssl rand -base64 32`.
2. Move the current key to `Security__PreviousDataEncryptionKeys__k1=<old key>`.
3. Set `Security__DataEncryptionKey=<new key>` and `Security__DataEncryptionKeyId=k2`.
4. Restart. The backfill re-encrypts every value onto `k2` and logs `… re-keyed`.
5. Once the log shows `0 undecryptable` and a restart reports nothing left to re-key, remove the previous key.

### Rotating the JWT key

Set a new `Jwt__SecretKey` and restart. All existing tokens become invalid, so everyone has to log in again. Do this
immediately if you suspect a leak.

## 4. Error and log hygiene

* Unhandled errors return a generic message plus a correlation id (also in the `X-Correlation-Id` response header). The
  exception and stack trace go only to the server log under that id. Development additionally shows the exception message.
* A Serilog enricher (`Security/LogRedaction.cs`) masks properties named like password, otp, token, secret, totp, pan, aadhar,
  account, ifsc, cvv or signature. It also masks JWTs, bearer/basic headers, `password=…` pairs, 12–19 digit runs
  (card/Aadhaar/account numbers) and ciphertexts anywhere in a log string. Literal text inside a message template can't be
  scrubbed, so never write secrets into template text.

## 5. Database hardening

**Least-privilege app login.** The app needs only to read and write rows:

```sql
CREATE LOGIN wahspices_app WITH PASSWORD = '<long random>', CHECK_POLICY = ON, DEFAULT_DATABASE = WahSpices;
USE WahSpices;
CREATE USER wahspices_app FOR LOGIN wahspices_app;
ALTER ROLE db_datareader ADD MEMBER wahspices_app;
ALTER ROLE db_datawriter ADD MEMBER wahspices_app;
GRANT EXECUTE TO wahspices_app;
```

It has no DDL, no `db_owner` and no server roles. **Locally this is done**: the Development user secret uses `wahspices_app`, and
the app was verified to start (the startup `DbInitializer` only runs guarded `IF NOT EXISTS` DDL, which is skipped once tables
exist), serve the catalog, place orders and pass `/health`.

Keep `sa` (or an owner login) **only for migrations**, and pass it explicitly so it is never in the app config:

```bash
dotnet ef database update --project WahSpices.Infrastructure --startup-project WahSpices.API \
  --connection "Server=…; Database=WahSpices; User Id=sa; Password=…; Encrypt=True; TrustServerCertificate=True"
```

On the hosted DB (databaseasp.net or similar), use the panel to create a separate DB user with read/write only. If the panel
can't do that, run the SQL above as the owner. Point `ConnectionStrings__DefaultConnection` at that user.

**Transport encryption.** In Production use `Encrypt=True; TrustServerCertificate=False`. The server needs a certificate signed
by a trusted CA; the startup check enforces this. `TrustServerCertificate=True` is acceptable only for the local Docker
container.

**At rest.** If the host offers it, enable **TDE** (Transparent Data Encryption, available on Azure SQL and SQL Server 2019+
Standard/Enterprise) and **encrypted backups** (`BACKUP … WITH ENCRYPTION`). Store the backup certificate separately from the
backups. Field-level encryption (section 3) protects the most sensitive columns even if TDE isn't available.

**Network.** Allow the DB port only from the API server's IP. Never expose 1433/1434 publicly. Don't reuse DB passwords anywhere.

## 6. Uploads

* Product, blog and recipe images (`/uploads/...`, public) are limited to an allow-list of image/video extensions
  (no SVG/HTML) and a size limit. The file's **magic bytes must match** its extension, files get random server-side names,
  and subfolders are a fixed list.
* `/uploads/**` serves only image/video extensions (anything else is 404), with `X-Content-Type-Options: nosniff` and
  `Content-Security-Policy: default-src 'none'; sandbox`. ASP.NET Core never executes static files.
* Staff documents are stored **outside wwwroot** (`App_Data/staff-docs`), served only through an admin-authorized endpoint, and
  magic-byte checked. Return photos must also stay outside wwwroot (handled in the returns module).

## 7. What this can't do (be realistic)

A public website's APIs can always be called by a script that copies what the browser does. The protections here make sure
no endpoint hands out data the caller isn't entitled to:

* ownership checks return 404;
* customer endpoints reject admin tokens and vice versa;
* sensitive fields are encrypted and masked;
* reveals are audited.

Rate limiting and bot detection make abuse slow and visible, but they can't make it impossible.

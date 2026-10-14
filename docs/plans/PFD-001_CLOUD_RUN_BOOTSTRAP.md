# PFD-001 — Cloud Run GCP bootstrap & owner-only permissions (2026-10-14)

**Status:** REVIEWED, NO GCP CHANGES EXECUTED. Code in Draft PR #22 is read-only and **not** ready to serve real Windows clients. The OWNER approved V2 to **read historical V1 data but write only separately isolated test data**, and explicitly forbade legacy shutdown until a later order.

## Strict target identities

- GitHub write: `tamnv2/pickfacedamage1291` only. Read-only reference exception for `tamnv2/supra-inventory` only project-memory process; not involved in the migration.
- GCP project: `pickface-damage-1291` ONLY. Region: `asia-southeast1`.
- Drive root: `16jDCy5_Z1X5cKJyNPR1rn_ZqbExQSAeC`. Legacy spreadsheet `1Ubm9EhALocUovzVr3UIspdCMtHIPm2NPUw6UjlZcqw4` directly under root. Image folder `1K_lUl_uE4dskR28cVK4iZJrzFUINfvXf`, logs folder `1Z19VgAAmCN1u7z_xSSztx9IVuq3kFSlK` directly under root.
- Drive metadata from scoped connector: root and files have `driveId=null` and single Google user as OWNER: this is **My Drive, not Shared Drive**. Do not move any legacy files.
- Existing Firebase RTDB and Authentication are in the authorized project only. Do not inspect any other GCP project or any other Billing Account's associated projects.

## Verified blocker / what OWNER must authorize

**1. Billing (OWNER only).** In [Cloud Billing console](https://console.cloud.google.com/billing?project=pickface-damage-1291), check this project's billing linkage and status. If not active, OWNER must approve linking an active billing account. Record only `BILLING_ACTIVE=YES/NO` and whether this project's account shares Cloud Run Free Tier (`UNKNOWN` is acceptable). Do **not** request a billing account ID or inspect any other project.

**2. Google Cloud APIs / IAM (OWNER one-time; grants are explicitly permission-sensitive).** On exactly `pickface-damage-1291`, review/enable `run.googleapis.com`, `artifactregistry.googleapis.com`, `iam.googleapis.com`, `iamcredentials.googleapis.com`, `sts.googleapis.com`, `secretmanager.googleapis.com`, `monitoring.googleapis.com`, `sheets.googleapis.com`, `drive.googleapis.com` and required Firebase Auth APIs. Create *project-owned* service identities, preferably:
- runtime: `pfd-cloudrun-runtime@pickface-damage-1291.iam.gserviceaccount.com` (for Cloud Run runtime to reach only this project's Firebase; later narrowly scoped Secret Manager secretAccessor for specific secret)
- deploy: `pfd-gh-deployer@pickface-damage-1291.iam.gserviceaccount.com` (for GitHub Actions; `roles/run.developer` on own project, `roles/artifactregistry.writer` on specific own-project container repo, `roles/iam.serviceAccountUser` on runtime SA, minimal extra API roles only after proven necessary)
- do NOT issue/download service-account keys, grant broad project Owner/Editor, or use an account from a different project.
- Artifact Registry repo for image in `asia-southeast1` of `pickface-damage-1291` only, e.g. `pfd-run`.
- Owner must review granting unauthenticated **Cloud Run transport invocation** to an explicitly named test service if EXE uses Firebase Auth rather than Google IAM. This does **not** mean business requests are unauthenticated: every business action requires server-side Firebase token verification, active profile and RBAC; login/password reset must have throttling. Never enable this on the unfinished read-only staging service by implication.

**3. GitHub deploy identity — no shared passwords.** Preferred no-key setup: Workload Identity Federation (WIF) GitHub OIDC issuer `https://token.actions.githubusercontent.com/`, with attribute mapping `google.subject=assertion.sub,attribute.repository=assertion.repository,attribute.ref=assertion.ref,attribute.repository_id=assertion.repository_id`; enforce exact repository and immutable repo ID, plus deployment branch/environment, not only the GitHub organization name. Associate deploy SA via `roles/iam.workloadIdentityUser` binding, restricted to `tamnv2/pickfacedamage1291` GitHub workflow. On GitHub, set **non-secret** repo variables (exact values only after creation):
  - `GCP_WIF_PROVIDER`: `projects/<PROJECT_NUMBER>/locations/global/workloadIdentityPools/<POOL>/providers/<PROVIDER>`
  - `GCP_DEPLOY_SA`: deploy service-account email
  - `GCP_RUNTIME_SA`: runtime service-account email
  - `GCP_PROJECT_ID`: `pickface-damage-1291`
  - `GCP_REGION`: `asia-southeast1`
  Deploy workflow gets `id-token: write` and `contents: read`; restrict to reviewed manually dispatched workflow and single test environment, avoiding public PR fork privilege. The assistant has GitHub repo rights but **no connected Google Cloud Run/IAM tool**; a configured WIF workflow is the route to automation after OWNER setup.

**4. My Drive OAuth (OWNER required).** Service accounts cannot create/own new files on user My Drive, so sharing an Editor role alone does not solve V2 image creation. Prefer a project-local OAuth client authorized by the **existing My Drive owner account**, storing OAuth client secret and refresh credential *only in Google Secret Manager in project `pickface-damage-1291`*, with Cloud Run runtime access to precisely those secrets. The OWNER must complete Google consent and permission grant interactively. Never paste tokens/secret JSON into chat, commit or EXE. Need evaluate Google OAuth verification/restricted scopes and production consent status; external Testing refresh tokens often expire after seven days. Avoid reuse of another project's OAuth credentials. Avoid unnecessary broad Drive operations: API calls must verify exact file ID ancestry within authorized root and approved child paths. A delegated Workspace Shared Drive option requires separately authorized storage movement, **not** assumed.

**5. Runtime/test data isolation (already OWNER approved).** Legacy Sheet/old images read-only from V2. Create V2 TEST spreadsheet, image and log folder only as descendants of fixed root, after OAuth grant. V2 writes to its own data files, independent `change_seq`, deduplication, CAS/locks, audit trail; legacy `REPORT_CHANGE_SEQ_V140`, `PRODUCT_CHANGE_SEQ_V140` and `LockService` never become V2 writer authority. Preserve existing Firebase user profiles read-only while testing; NO modification of legacy `active_operator`; independent v2 operator lease with test-specific IAM/rules considered separately. Old clients still get App Script when quota available. No old-Gateway retirement and no stable auto-update during tests.

**6. Monitoring and costs.** Configure optional project-scoped budget alerts from 50/70/85% of measured estimated Cloud Run Free Tier, with **UNKNOWN** for Billing Account-wide free tier if other projects may share it. Cloud Run built-in metrics are automatically available in Cloud Monitoring. Secret Manager, Artifact Registry storage, API quota, network egress and logging have independent usage/cost dimensions; do not promise zero-cost. `min-instances=0`, request-based, `max-instances=2`, moderate concurrency, bounded retries. No additional 24/7 polling process.

## Owner return checklist — no credential values

- `billing_enabled`: yes/no/unknown (this project only)
- `WIF_PROVIDER_RESOURCE`: resource name (public identifier, not secret)
- `DEPLOY_SERVICE_ACCOUNT_EMAIL`: public identity
- `RUNTIME_SERVICE_ACCOUNT_EMAIL`: public identity
- `ARTIFACT_REGISTRY_REPO`: in-project repo name
- `DRIVE_OAUTH_CONSENT`: pending/completed/blocked; no token or secret content
- `CLOUD_RUN_PUBLIC_TEST_ENDPOINT_PERMISSION`: not approved / Owner explicitly approves public *transport* invocation guarded by Firebase verification (do not pre-enable before reviewed backend)
- `PERMISSION_GRANTS_AUTHORIZED`: only exact resources/roles named above, after OWNER confirms, no broad blanket IAM change.

## Implementation once permissions are ready

1. Source code Cloud Run complete 1:1 action parity, Firebase token verification and user authorization, username/email alias parity from own-project RTDB + Firebase Auth, token refresh and recovery, Drive OAuth integration; separate v2 storage and lease. Never reuse resource from Báo hàng.
2. Source tests: duplicate report, concurrent SKU/write retries, partial image upload recovery, conflict detection, hard-delete containment, token expiration, rate-limited login, 2-PC old/new lease independence and project/root fail-closed.
3. Separate Windows V2 candidate EXE with Cloud Run endpoint pinned for test; do not change legacy `runtime-config.json` or main stable GitHub latest. Produce isolated test build artifact for Owner-selected PC.
4. Build & verify exact branch HEAD CI; OWNER code-ready signoff before *first deployment*. Deploy Cloud Run test service only under authorized project once infrastructure and consent proven, then field test.
5. Continue to phase C usage UI after B is verified. Legacy retirement is separate later OWNER action.

## Current evidence / uncertainty

- Scoped Drive connector verified My Drive folder and direct-child IDs on 2026-10-14.
- Draft PR #22 Cloud Run source is read-only incomplete. It has not been deployed.
- Current tools include GitHub and scoped Google Drive, not a Google Cloud Run IAM/Billing administration connector; cannot directly verify Cloud Billing, enabled APIs, IAM or deploy a real Cloud Run service from this chat today.
- GCP privileges are sensitive; this checklist is a **proposal for OWNER setup**, not approval already exercised.

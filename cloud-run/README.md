# Cloud Run staging backend — NOT YET DEPLOYED

Project isolation hardcoded to `pickface-damage-1291`. Service cannot start unless `GOOGLE_CLOUD_PROJECT` or `GCLOUD_PROJECT` matches. Intended region `asia-southeast1`.

This is a **non-production READ-ONLY migration slice**. Includes signed Firebase JWT verification (Google JWKS), active user profile check, project-owned Sheets read adapters (`pull_changes`, `pull_products`, `list_audit`), Drive image read with strict direct-parent check, fixed repo release metadata and `gateway_info`/health. It refuses all mutations. Existing Windows EXE must **not** be pointed to it: `production_ready:false` and missing required capabilities intentionally fail-closed.

It uses runtime Google Cloud metadata server identity; there are no embedded secret credentials or SDK key files. Deployment depends on an OWNER-approved service identity with legitimate access to Drive root subtree, Sheets and Firebase in **this project only**. Never upload OAuth client secrets/refresh tokens, service account private keys, or personnel data to the public repo.

Security prerequisites before production: real IAM/Drive scope test, username alias migration, account recovery and user administration, distributed conditional writes/IDempotency for report/images/catalog/audit, Gateway feature parity, load tests, rollback, new .NET Windows release, live field tests, provider Usage monitoring and explicit access-grant approval. Prior `LockService` semantics must be preserved. Neither source nor tests prove permission or GCP deploy.

Local tests (no secrets / GCP credentials): `cd cloud-run && node --test *.test.mjs`.

**Do not deploy yet**: staging read-only code is incomplete for the OWNER's full migration.

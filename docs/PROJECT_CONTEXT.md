# PROJECT CONTEXT — PICKFACE DAMAGE 1291

Repository: `tamnv2/pickfacedamage1291` (public). Drive root: `16jDCy5_Z1X5cKJyNPR1rn_ZqbExQSAeC`. GCP project: `pickface-damage-1291`. `PROJECT_SCOPE_LOCK.md` governs permitted access. The separate `tamnv2/supra-inventory` repository is **read-only reference for durable memory design**, never a business/credentials/provider source.

## Existing verified source and data model (2026-10-14)

- .NET 8 Windows portable desktop client; `src/PickfaceDamage1291/PickfaceDamage1291.csproj` indicates v1.4.33.
- Windows authenticates through username/password and Firebase, Google Apps Script gateway delegates user profile/RTDB/Sheets/Drive and audit, with Office relay for blocked Firebase routes.
- `src/PickfaceDamage1291/RuntimeConfigService.cs` currently pins `script.google.com`; switching to Cloud Run requires Windows source changes and a new release.
- Existing Sheets tabs: damage reports, `SKU_Catalog`, `AuditHistory`; last observed 295 reports, 2,563 SKUs, 1,735 audit records (not a guaranteed current count).
- Existing image and log folders are genuine direct descendants of the authorized Drive root.
- Verified client usage data: 876 usage snapshots from 3 devices over 25 days (16 Sep–13 Oct 2026). ~35,978 Gateway requests, 29,206 RTDB relay actions, rough UrlFetchApp ~100,000 for period. Counts are client-collected/estimated, not quota-billing ground truth.
- Google Apps Script uses `PropertiesService` for username aliases and report/product change counters and `LockService` for report synchronization. Cloud Run needs durable equivalents and idempotent transactions.
- Cloud Run region requested: Singapore `asia-southeast1`. OWNER accepts that Office network might not work; normal-network usage must still be tested.
- No Cloud Run deployment, production cutover, GCP Billing proof or field acceptance has been verified in this workstream.

## Hard constraints

- Read repository and journal, current implementation and CI *before* substantive work; never substitute conversational memory.
- No cross-project reads, writes, reused cloud products or credentials. Strict root-folder descendants on Google Drive.
- Keep existing Drive files/photos/history; deletion and permission changes separately OWNER-gated.
- Source is public: no service-account keys, passwords, refresh tokens, ID tokens or sensitive customer/worker details in commits/logs.
- Do not infer Free Tier remainder from project request count if Billing Account is shared.

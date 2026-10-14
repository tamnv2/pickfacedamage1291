# OWNER decisions — PICKFACE DAMAGE 1291

Authority: explicit OWNER commands and evidence stored in this repository. This document is not a raw chat transcript. New approved changes must also appear in `ops/change-continuity/PFD-001.json`.

## 2026-10-14 — PFD-001 (approved)

1. **A — repository-first project continuity.** OWNER approved consulting `tamnv2/supra-inventory` **read-only for the persistence mechanism only**; implement durable sanitized working discussion, decisions, pending items, implementation, release and test states in `tamnv2/pickfacedamage1291`. Never use AI memory as the authoritative project record. A must be verified complete before B.
2. **B — migrate all Apps Script Gateway/UrlFetchApp flows.** Target project `pickface-damage-1291`, Cloud Run `asia-southeast1`, request-based, scale-to-zero; preserve Firebase Auth/RTDB, Sheets/Drive records, image links and conflicts, username identity and audit, RBAC, operator-lease correctness. No new Cloudflare resources; Office network can be unsupported. No cross-project resource reuse. Do not delete legacy Apps Script, Drive files or grant permissions without a separate explicit OWNER request.
3. **C — Cloud Run usage instead of Apps Script usage.** Replace old in-app table; show Cloud Run request, CPU and memory, cost/free-tier projection when verified provider data available, distinguish project-only metrics from billing-account-shared allowance. Warn before free-tier exhaustion, with no fake 0%/guaranteed-free claim.
4. **Consumption evidence before migration.** 25 days of client usage snapshots yielded ~35,978 Apps Script gateway requests, of which ~29,206 were RTDB proxy requests. Approximate ~4,000 Apps Script UrlFetchApp calls/day from code-path estimates, not Google actual quota.
5. **Owner authorization scope.** The previous approval to build and deploy B/C is acknowledged; a provider permission change, destructive migration or removal still requires distinct explicit approval. Mark CODE_READY, CI_PASS, DEPLOYED, FIELD_PASS separately.

## Historical release baseline

`docs/handovers/HANDOVER_CURRENT.md` documents Windows v1.4.33, GitHub-only update binary and previous Apps Script runtime. This is evidence of a prior release, **not** proof all deployed PCs run that version.

## Unresolved questions

- Which project-scoped Cloud Run deployment credentials / GitHub Actions identity are available?
- Cloud Run Google Drive/Sheets authority for current fixed root (My Drive vs Shared Drive); any OAuth/grant must be OWNER-approved and server-side only.
- How to preserve Apps Script `PropertiesService` username aliases and change sequence safely without moving files out of scope?
- How to measure CPU/RAM and shared Billing Account allowance without inspecting any other GCP project?

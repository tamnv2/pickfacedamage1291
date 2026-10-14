# Change continuity protocol — Pickface Damage 1291

Adapted from **governance mechanics only** of read-only `tamnv2/supra-inventory`. No business rules or resources copied.

## Bootstrap and authority

1. Read `ops/authority-manifest.json` from `main`, consuming listed files in order.
2. Read active working branch/PR (when set in `ops/project-state.json`) at its exact HEAD, including journal and actual diff/CI.
3. Distinguish accepted `main` and historical GitHub release from unpublished code and installed/field status.
4. On conflict between documented proposal and later OWNER decision, record the latter with supersedes reference; do not erase earlier discussion.
5. Only current OWNER command may authorize new scope, release or permission changes. Project scope must still be satisfied.

## Every meaningful exchange

Append one or more `entries[]` to the currently active journal with stable unique IDs, VN date, topic, type (OWNER_COMMAND, DISCUSSION, PROPOSAL, DECISION, TECHNICAL_FINDING, TEST, STATUS, BLOCKER), status, approval, facts, alternatives, open questions, next action, source evidence and optional supersedes. Summarize exact technical decisions; **never record raw chat, secrets, personal staff data, customer/product rows, access tokens or private logs**. Omit duplicates for read-only no-op responses.

- OWNER decisions update `docs/OWNER_DECISIONS.md` as well as journal.
- Source changes update `ops/project-state.json` with evidence and distinction between source/test/CI/deploy/field pass.
- Workstream transitions update journal resume and state only after prerequisite DONE with commit/readback.
- If GitHub writes unavailable, report `CHECKPOINT_NOT_SAVED` and provide actionable pending summary.
- Neither this file nor CI automatically mirrors conversations: assistant must execute each checkpoint and verify SHA.

## Evidence and deploy gates

`CODE_READY` requires source committed, tests passed as identified, and no unresolved known P0. `CI_PASS` requires exact SHA and workflow success. `DEPLOYED` requires provider/GitHub release receipt. `RUNTIME_VERIFIED` requires live check under permitted project. `FIELD_PASS` requires actual user/device outcome. `OWNER_PASS` only when OWNER states it. A code commit or green workflow alone never proves live migration success.

For B migration, retain rollback, confirm account authority and Drive subtree, preserve `PropertiesService` alias/counters, RBAC, lease concurrency and image idempotency. Never destructively retire the old service without OWNER approval.

## C usage measurement

Provider metrics and self-reported client metrics are separate. Make Cloud Run CPU allocated billable seconds, RAM GiB-seconds, request count and charges visible only with authorized project scopes. For shared billing free tier, report `UNKNOWN` unless verified billed data is available without accessing another project. Do not infer 100% remaining free tier.

Run structural validation via `python tools/project_memory_guard.py` and enforce in CI.

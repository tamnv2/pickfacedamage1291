#!/usr/bin/env python3
"""Structural, in-repo-only continuity guard for Pickface Damage 1291."""
from __future__ import annotations
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
def read(path: str) -> dict:
    return json.loads((ROOT / path).read_text(encoding="utf-8"))
def check() -> None:
    m = read("ops/authority-manifest.json")
    s = read("ops/project-state.json")
    j = read("ops/change-continuity/PFD-001.json")
    scope = read("ops/project-scope.json")
    assert m["project_key"] == s["project_key"] == scope["project_key"] == "pickface-damage-1291"
    assert s["repository"] == j["source_repo"] == scope["github_write_repository"] == "tamnv2/pickfacedamage1291"
    assert s["current_change"] == j["change_id"] == "PFD-001"
    assert scope["google_cloud"]["project_id"] == "pickface-damage-1291"
    assert scope["drive"]["root_id"] == "16jDCy5_Z1X5cKJyNPR1rn_ZqbExQSAeC"
    assert scope["github_reference_read_only"]["repository"] == "tamnv2/supra-inventory"
    for entry in m["bootstrap_order"]:
        if entry not in {"recent_commits_and_ci", "relevant_live_source"}:
            assert (ROOT / entry).is_file(), f"bootstrap file missing: {entry}"
    entries = j["entries"]
    assert entries and j["last_checkpoint"] == entries[-1]["id"]
    ids = [x["id"] for x in entries]
    assert len(ids) == len(set(ids)), "duplicate checkpoint id"
    required = {"id", "date_vn", "topic", "kind", "status", "owner_approval", "facts", "next_action", "evidence"}
    for x in entries:
        assert required.issubset(x), f"missing journal fields: {x.get('id')}"
        assert x["id"].startswith(j["change_id"] + "-E"), x["id"]
    required_resume={"approved","implemented","in_progress","pending_approval","blocked","next_actions","release_boundary"}
    assert required_resume.issubset(j["resume"]), "missing resume fields"
    assert s["change_control"]["last_checkpoint"] == j["last_checkpoint"]
    text = "\n".join((ROOT / p).read_text(encoding="utf-8") for p in ["docs/PROJECT_CONTEXT.md", "docs/OWNER_DECISIONS.md"])
    assert "tamnv2/supra-inventory" in text, "read-only provenance missing"
    print("PICKFACE_PROJECT_MEMORY_GUARD_PASS")
if __name__ == "__main__":
    check()

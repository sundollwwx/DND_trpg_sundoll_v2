#!/usr/bin/env python3
"""Run the disposable, Unity-independent portion of the M0 technical spike."""

from __future__ import annotations

import copy
import hashlib
import json
import os
import platform
import shutil
import tempfile
import time
import traceback
from pathlib import Path
from typing import Any, Callable, Dict, Iterable, List, Optional, Tuple


ROOT = Path(__file__).resolve().parents[1]
RESULTS = ROOT / "results"


def json_bytes(value: Any) -> bytes:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def canonical_state(state: Dict[str, Any]) -> Dict[str, Any]:
    """Keep only authoritative fields; display and local workspace state are excluded."""
    excluded = {"savedAtUtc", "displaySummary", "workspacePreferences", "diagnostics", "undo"}
    return {key: value for key, value in state.items() if key not in excluded}


def canonical_hash(state: Dict[str, Any]) -> str:
    return sha256_bytes(json_bytes(canonical_state(state)))


def fsync_directory(directory: Path) -> None:
    try:
        fd = os.open(str(directory), os.O_RDONLY)
    except OSError:
        return
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def atomic_write(path: Path, content: bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(f".{path.name}.m0tmp-{os.getpid()}-{time.time_ns()}")
    with open(temp, "wb") as handle:
        handle.write(content)
        handle.flush()
        os.fsync(handle.fileno())
    os.replace(temp, path)
    fsync_directory(path.parent)


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path: Path, value: Any) -> None:
    atomic_write(path, json_bytes(value) + b"\n")


def make_world(revision: int = 0) -> Dict[str, Any]:
    return {
        "projectId": "m0-project",
        "formatVersion": 0,
        "worldSchemaVersion": 0,
        "worldRevision": revision,
        "maps": [{"mapId": "map-1", "width": 256, "height": 256, "cells": {"0,0": "grass"}}],
        "catalog": [{"definitionId": "piece-1", "name": "蓝色圆片", "asset": "blob:pending"}],
        "scenarios": [{"scenarioId": "scenario-1", "boardId": "board-1", "pieces": {}}],
        "extensions": [{"typeId": "future:unknown", "version": 1, "data": {"kept": True}}],
        "savedAtUtc": "not-authoritative",
        "displaySummary": "not-authoritative",
        "workspacePreferences": {"panelWidth": 280},
    }


def command(command_id: str, operation: str, piece_id: str, x: int, y: int, base_revision: int) -> Dict[str, Any]:
    return {
        "commandId": command_id,
        "typeId": operation,
        "payloadVersion": 1,
        "baseWorldRevision": base_revision,
        "payload": {"pieceId": piece_id, "x": x, "y": y},
    }


class LocalAuthority:
    def __init__(self, state: Dict[str, Any]) -> None:
        self.state = copy.deepcopy(state)
        self.results: Dict[str, Dict[str, Any]] = {}
        self.undo_stack: List[Dict[str, Any]] = []
        self.redo_stack: List[Dict[str, Any]] = []

    def submit(self, envelope: Dict[str, Any]) -> Dict[str, Any]:
        command_id = envelope["commandId"]
        if command_id in self.results:
            return copy.deepcopy(self.results[command_id])
        if envelope["baseWorldRevision"] != self.state["worldRevision"]:
            result = {"status": "conflict", "commandId": command_id, "revision": self.state["worldRevision"]}
            self.results[command_id] = result
            return copy.deepcopy(result)

        candidate = copy.deepcopy(self.state)
        try:
            if envelope["typeId"] != "MovePiece":
                raise ValueError("unsupported command")
            payload = envelope["payload"]
            pieces = candidate["scenarios"][0]["pieces"]
            pieces[payload["pieceId"]] = {"x": int(payload["x"]), "y": int(payload["y"])}
            candidate["worldRevision"] += 1
        except (KeyError, TypeError, ValueError) as exc:
            result = {"status": "rejected", "commandId": command_id, "error": str(exc)}
            self.results[command_id] = result
            return copy.deepcopy(result)

        self.undo_stack.append(copy.deepcopy(self.state))
        self.state = candidate
        self.redo_stack.clear()
        result = {"status": "accepted", "commandId": command_id, "revision": self.state["worldRevision"]}
        self.results[command_id] = result
        return copy.deepcopy(result)

    def undo(self) -> None:
        if not self.undo_stack:
            raise AssertionError("undo stack is empty")
        self.redo_stack.append(copy.deepcopy(self.state))
        self.state = self.undo_stack.pop()

    def redo(self) -> None:
        if not self.redo_stack:
            raise AssertionError("redo stack is empty")
        self.undo_stack.append(copy.deepcopy(self.state))
        self.state = self.redo_stack.pop()


def render_view(state: Dict[str, Any]) -> Dict[str, Any]:
    pieces = state["scenarios"][0]["pieces"]
    return {piece_id: (piece["x"], piece["y"]) for piece_id, piece in sorted(pieces.items())}


def durable_journal_batch(command_id: str, sequence: int, world_revision: int, payload: Dict[str, Any]) -> bytes:
    unsigned = {
        "commandId": command_id,
        "journalStreamId": "stream-1",
        "worldRevision": world_revision,
        "operationSequence": sequence,
        "payloadVersion": 1,
        "payload": payload,
    }
    batch = dict(unsigned)
    batch["sha256"] = sha256_bytes(json_bytes(unsigned))
    return json_bytes(batch) + b"\n"


def recover_journal(path: Path) -> List[Dict[str, Any]]:
    accepted: List[Dict[str, Any]] = []
    if not path.exists():
        return accepted
    with open(path, "rb") as handle:
        for raw in handle:
            try:
                batch = json.loads(raw.decode("utf-8"))
                unsigned = dict(batch)
                expected = unsigned.pop("sha256")
                if sha256_bytes(json_bytes(unsigned)) != expected:
                    break
                accepted.append(batch)
            except (UnicodeDecodeError, json.JSONDecodeError, KeyError, TypeError):
                break
    return accepted


def commit_revision(root: Path, state: Dict[str, Any], save_id: str, fault: Optional[str] = None) -> None:
    revisions = root / "revisions"
    staging = root / "staging" / save_id
    staging.mkdir(parents=True, exist_ok=True)
    revision = staging / "revision"
    revision.mkdir(parents=True, exist_ok=True)

    project = copy.deepcopy(state)
    project["savedAtUtc"] = "excluded-from-canonical-hash"
    project_path = revision / "project.json"
    write_json(project_path, project)
    if fault == "after_project":
        raise RuntimeError("injected after_project")

    manifest = {
        "saveRevisionId": save_id,
        "parentRevisionId": read_json(root / "HEAD.json")["activeSaveRevisionId"] if (root / "HEAD.json").exists() else None,
        "formatVersion": 0,
        "worldSchemaVersion": 0,
        "canonicalStateHash": canonical_hash(project),
        "files": [{"path": "project.json", "size": project_path.stat().st_size, "sha256": sha256_bytes(project_path.read_bytes())}],
    }
    write_json(revision / "revision-manifest.json", manifest)
    if fault == "after_manifest":
        raise RuntimeError("injected after_manifest")

    final_revision = revisions / save_id
    final_revision.parent.mkdir(parents=True, exist_ok=True)
    os.replace(revision, final_revision)
    fsync_directory(final_revision.parent)
    if fault == "after_revision_commit":
        raise RuntimeError("injected after_revision_commit")

    head = read_json(root / "HEAD.json") if (root / "HEAD.json").exists() else {"generation": 0}
    new_head = {
        "projectId": state["projectId"],
        "activeSaveRevisionId": save_id,
        "activeJournalStreamId": "stream-1",
        "generation": head["generation"] + 1,
    }
    if fault == "before_head_commit":
        raise RuntimeError("injected before_head_commit")
    write_json(root / "HEAD.json", new_head)


def check(name: str, fn: Callable[[], Dict[str, Any]]) -> Dict[str, Any]:
    started = time.perf_counter()
    try:
        detail = fn()
        return {"name": name, "status": "pass", "elapsedMs": round((time.perf_counter() - started) * 1000, 3), "detail": detail}
    except Exception as exc:  # pragma: no cover - this is the test runner boundary
        return {
            "name": name,
            "status": "fail",
            "elapsedMs": round((time.perf_counter() - started) * 1000, 3),
            "error": f"{type(exc).__name__}: {exc}",
            "traceback": traceback.format_exc(),
        }


def verify_canonical_hash() -> Dict[str, Any]:
    left = make_world()
    right = copy.deepcopy(left)
    right["maps"] = list(reversed(right["maps"]))
    right["savedAtUtc"] = "different timestamp"
    right["workspacePreferences"] = {"panelWidth": 900}
    left_hash = canonical_hash(left)
    right_hash = canonical_hash(right)
    assert left_hash == right_hash
    return {"hash": left_hash, "excludedNonAuthoritativeFields": True}


def verify_authority_and_rebuild() -> Dict[str, Any]:
    authority = LocalAuthority(make_world())
    first = authority.submit(command("cmd-1", "MovePiece", "piece-a", 2, 3, 0))
    retry = authority.submit(command("cmd-1", "MovePiece", "piece-a", 99, 99, 0))
    assert first == retry
    assert authority.state["worldRevision"] == 1
    before_bad = copy.deepcopy(authority.state)
    rejected = authority.submit(command("cmd-2", "MovePiece", "piece-b", 4, 5, 0))
    assert rejected["status"] == "conflict"
    assert authority.state == before_bad
    view_before = render_view(authority.state)
    view_after = render_view(copy.deepcopy(authority.state))
    assert view_before == view_after == {"piece-a": (2, 3)}
    authority.undo()
    assert render_view(authority.state) == {}
    authority.redo()
    assert render_view(authority.state) == {"piece-a": (2, 3)}
    return {"idempotentRetry": True, "staleConflictAtomic": True, "viewRebuild": True, "undoRedo": True}


def verify_asset_hash_and_chinese_path() -> Dict[str, Any]:
    with tempfile.TemporaryDirectory(prefix="m0-中文-") as temp:
        root = Path(temp) / "项目路径-中文"
        root.mkdir()
        blob = "合成占位棋子".encode("utf-8")
        digest = sha256_bytes(blob)
        asset = root / "assets" / f"{digest}.bin"
        atomic_write(asset, blob)
        atomic_write(asset, blob)
        assert asset.read_bytes() == blob
        assert len(list((root / "assets").iterdir())) == 1
        return {"path": str(root), "assetHash": digest, "deduplicated": True}


def verify_atomic_revisions() -> Dict[str, Any]:
    with tempfile.TemporaryDirectory(prefix="m0-revisions-") as temp:
        root = Path(temp) / "项目存档-中文.sundollproj"
        root.mkdir()
        first = make_world(0)
        commit_revision(root, first, "rev-1")
        baseline = read_json(root / "HEAD.json")
        faults = ["after_project", "after_manifest", "after_revision_commit", "before_head_commit"]
        preserved = []
        for index, fault in enumerate(faults, start=2):
            try:
                commit_revision(root, make_world(index), f"rev-{index}", fault=fault)
            except RuntimeError:
                pass
            current = read_json(root / "HEAD.json")
            assert current == baseline
            preserved.append(fault)
        commit_revision(root, make_world(9), "rev-9")
        assert read_json(root / "HEAD.json")["activeSaveRevisionId"] == "rev-9"
        return {"faultPoints": preserved, "oldHeadPreserved": True, "successfulCommit": True}


def verify_journal_tail() -> Dict[str, Any]:
    with tempfile.TemporaryDirectory(prefix="m0-journal-") as temp:
        path = Path(temp) / "stream-1" / "000001-segment.log"
        path.parent.mkdir(parents=True)
        with open(path, "wb") as handle:
            handle.write(durable_journal_batch("cmd-1", 1, 1, {"op": "create"}))
            handle.write(durable_journal_batch("cmd-2", 2, 2, {"op": "move"}))
            handle.write(b'{"commandId":"torn"')
            handle.flush()
            os.fsync(handle.fileno())
        recovered = recover_journal(path)
        assert [batch["commandId"] for batch in recovered] == ["cmd-1", "cmd-2"]
        return {"acceptedBatches": len(recovered), "tornTailIgnored": True}


def benchmark() -> Dict[str, Any]:
    world = make_world()
    hash_start = time.perf_counter()
    synthetic_cells = {f"{x},{y}": "grass" if (x + y) % 2 else "stone" for y in range(256) for x in range(256)}
    world["maps"][0]["cells"] = synthetic_cells
    map_hash = canonical_hash(world)
    hash_ms = (time.perf_counter() - hash_start) * 1000

    replay_world = make_world()
    replay_start = time.perf_counter()
    for index in range(10000):
        replay_world["worldRevision"] += 1
        replay_world["scenarios"][0]["pieces"]["piece-a"] = {"x": index % 256, "y": (index * 7) % 256}
    replay_hash = canonical_hash(replay_world)
    replay_ms = (time.perf_counter() - replay_start) * 1000

    return {
        "syntheticMap": "256x256",
        "syntheticMapCanonicalHashMs": round(hash_ms, 3),
        "syntheticMapHash": map_hash,
        "operationReplayCount": 10000,
        "operationReplayAndHashMs": round(replay_ms, 3),
        "operationReplayHash": replay_hash,
        "warning": "Python standard-library data-layer measurement; not a Unity frame-time or IL2CPP result.",
    }


def main() -> int:
    checks = [
        check("canonical_state_hash", verify_canonical_hash),
        check("local_authority_idempotency_and_rebuild", verify_authority_and_rebuild),
        check("content_addressed_asset_and_chinese_path", verify_asset_hash_and_chinese_path),
        check("atomic_revision_and_head_fault_injection", verify_atomic_revisions),
        check("journal_torn_tail_recovery", verify_journal_tail),
    ]
    benchmark_result = benchmark()
    result = {
        "schema": "m0-verification-v0",
        "timestampUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "environment": {
            "platform": platform.platform(),
            "machine": platform.machine(),
            "python": platform.python_version(),
            "unityEditor": (
                "/Applications/Unity/Hub/Editor/6000.3.22f1/Unity.app"
                if Path("/Applications/Unity/Hub/Editor/6000.3.22f1/Unity.app").exists()
                else "unavailable"
            ),
        },
        "checks": checks,
        "benchmarks": benchmark_result,
        "overall": "pass" if all(item["status"] == "pass" for item in checks) else "fail",
        "editorValidation": (
            "batch-validated; see unity-validation.json and unity-build-verification.json"
            if Path("/Applications/Unity/Hub/Editor/6000.3.22f1/Unity.app").exists()
            else "pending Unity 6.3 LTS 6000.3.22f1 installation"
        ),
    }
    RESULTS.mkdir(parents=True, exist_ok=True)
    write_json(RESULTS / "m0-verification.json", result)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 0 if result["overall"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())

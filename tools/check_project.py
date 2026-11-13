"""Validate project documents/configuration/provenance, not the P2P implementation."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import struct
import sys
from urllib.parse import unquote
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
SKIP = {".git", ".vs", "bin", "obj", "tmp", "__pycache__"}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify-sources", action="store_true", help="Also compare original local slide/Vst files")
    parser.add_argument("--report", type=Path)
    args = parser.parse_args()
    errors: list[str] = []
    counts = {"markdown_files": 0, "local_links": 0, "external_local_links_skipped": 0, "json_files": 0, "snapshot_files": 0, "source_pdfs": 0, "protocol_vectors": 0}

    def require(condition: bool, message: str) -> None:
        if not condition:
            errors.append(message)

    def load(relative: str):
        return json.loads((ROOT / relative).read_text(encoding="utf-8-sig"))

    files = [p for p in ROOT.rglob("*") if p.is_file() and not set(p.relative_to(ROOT).parts) & SKIP]
    required = ["README.md", "AGENTS.md", "ET4230.P2P.sln", ".github/agent/CURRENT_STATE.md", ".github/agent/RECOVERY_NOTES.md", "docs/README.md", "docs/Tong-quan.md", "docs/Ke-hoach.md", "docs/thiet-ke/Giao-thuc-v1.md", "docs/hoc-tap/Kien-thuc-tu-slide.md"]
    for relative in required:
        require((ROOT / relative).is_file(), f"Missing required file: {relative}")

    link_pattern = re.compile(r"(?<!!)\[[^\]\n]*\]\((<[^>\n]+>|[^\s)]+)(?:\s+\"[^\"]*\")?\)")
    for file in files:
        if file.suffix == ".json":
            try:
                json.loads(file.read_text(encoding="utf-8-sig"))
                counts["json_files"] += 1
            except (ValueError, UnicodeError) as exc:
                errors.append(f"Invalid JSON {file.relative_to(ROOT)}: {exc}")
        if file.suffix == ".csproj":
            try:
                ET.parse(file)
            except ET.ParseError as exc:
                errors.append(f"Invalid project XML {file.relative_to(ROOT)}: {exc}")
        if file.suffix != ".md":
            continue
        counts["markdown_files"] += 1
        content = file.read_text(encoding="utf-8-sig")
        require("\ufffd" not in content, f"Replacement character in {file.relative_to(ROOT)}")
        require(not re.search(r"turn\d+(?:search|view)\d+", content), f"Internal web reference token in {file.relative_to(ROOT)}")
        for match in link_pattern.finditer(content):
            target = unquote(match.group(1).strip("<>"))
            if re.match(r"^[a-z][a-z0-9+.-]*://", target, re.I) or target.startswith(("mailto:", "#")):
                continue
            target = target.split("#", 1)[0].split("?", 1)[0]
            target = re.sub(r":\d+$", "", target)
            if re.match(r"^/?[A-Za-z]:[/\\]", target):
                if sys.platform != "win32" or not args.verify_sources:
                    counts["external_local_links_skipped"] += 1
                    continue
                resolved = Path(target.lstrip("/"))
            else:
                resolved = file.parent / target
            counts["local_links"] += 1
            require(resolved.exists(), f"Broken link in {file.relative_to(ROOT)}: {target}")

    try:
        team = load("configs/team.json")
        require({m["name"] for m in team["members"]} == {"Linh", "Minh", "Cường", "Quân", "Tú"} and len(team["members"]) == 5, "Team must contain the five confirmed members exactly once")
        require(team["language"] == "C#", "Confirmed language is C#")
        peer = load("configs/peer.default.json")
        transfer, limits = peer["transfer"], peer["limits"]
        for group in (transfer, limits):
            for key, value in group.items():
                require(type(value) is int and value > 0, f"Expected positive integer: {key}")
        block, piece = transfer["blockSizeBytes"], transfer["pieceSizeBytes"]
        require(piece >= block and piece % block == 0, "Piece size must be a positive multiple of block size")
        require(transfer["maxPayloadBytes"] >= block + 12, "Payload cap cannot hold a BLOCK")
        require(transfer["maxControlBytes"] <= transfer["maxPayloadBytes"], "Control cap exceeds payload cap")
        reserved = limits["maxConnections"] * (limits["maxInflightPerConnection"] + 2) + limits["writeQueueCapacity"] + limits["uploadQueueCapacity"]
        require(reserved <= limits["maxBufferCount"], "Planned queue/inflight reservations exceed global buffer count")
        require(limits["maxBufferCount"] * block + limits["hashScratchBytes"] <= limits["payloadMemoryBudgetBytes"], "Block pool plus scratch exceed payload budget")
        require(peer["tracker"]["ttlSeconds"] > peer["tracker"]["heartbeatSeconds"] > 0, "TTL must exceed positive heartbeat period")
        require(peer["peer"]["advertiseHost"] not in ("0.0.0.0", "::"), "Advertised address cannot be a bind wildcard")
        for endpoint in (peer["tracker"], peer["peer"]):
            require(type(endpoint["port"]) is int and 1 <= endpoint["port"] <= 65535, "Invalid port")

        experiments = load("configs/experiments.json")
        require(experiments["totalPeersIncludesSeed"] is True, "Experiment N must explicitly include seed")
        require(len(experiments["scenarios"]) >= 3, "Expected at least three planned scenarios")
        require(len(set(experiments["totalPeers"])) >= 2 and min(experiments["totalPeers"]) >= 3, "Invalid peer-count survey")
        require(len(experiments["randomSeeds"]) >= experiments["runsPerConfiguration"] >= 1, "Not enough seeds for planned runs")
        require(len(set(experiments["randomSeeds"])) == len(experiments["randomSeeds"]), "Duplicate random seeds")
        for scenario in experiments["scenarios"]:
            leaving = scenario["departingReceivers"]
            require(0 <= leaving < min(experiments["totalPeers"]) - 1, "Departure must leave a receiver plus the seed")
            require(scenario["seedAlwaysOnline"] is True, "Planned recovery scenario requires original seed online")
            require(all(rate > 0 for rate in scenario["receiverApplicationUploadMbpsPattern"]), "Upload rates must be positive")
            require(bool(scenario["receiverApplicationUploadMbpsPattern"]), "Upload pattern cannot be empty")
            if leaving:
                require(0 < scenario["departAtSeconds"] < experiments["runDeadlineSeconds"], "Departure time outside run")

        vectors = load("tests/protocol-vectors.json")
        for vector in vectors["vectors"]:
            header, payload = bytes.fromhex(vector["headerHex"]), bytes.fromhex(vector["payloadHex"])
            require(len(header) == vectors["headerSizeBytes"] == 16, "Header vector must have 16 bytes")
            magic, version, kind, flags, request, length = struct.unpack("!4sBBHII", header)
            require((magic, version, flags) == (b"E423", 1, 0), "Invalid vector protocol/version/flags")
            require((kind, request, length) == (vector["type"], vector["requestId"], vector["payloadLength"]), "Header metadata does not match vector")
            require(length == len(payload), "Vector payload length mismatch")
            counts["protocol_vectors"] += 1

        snapshot = load("references/VstHelper/SNAPSHOT.json")
        for record in snapshot["files"]:
            file = ROOT / "references" / "VstHelper" / record["path"]
            require(file.is_file(), f"Missing snapshot file: {record['path']}")
            if file.is_file():
                digest = hashlib.sha256(file.read_bytes()).hexdigest()
                require(digest == record["sha256"], f"Snapshot bytes changed: {record['path']}")
                counts["snapshot_files"] += 1
                if args.verify_sources:
                    original = Path(snapshot["original_source"]) / record["path"]
                    require(original.is_file() and hashlib.sha256(original.read_bytes()).hexdigest() == digest, f"Original source differs/missing: {record['path']}")
        slides = load("docs/tham-khao/slides-index.json")
        require(slides["total_pages"] == sum(f["pages"] for f in slides["files"]), "Slide page total mismatch")
        if args.verify_sources:
            for record in slides["files"]:
                file = ROOT.parent / "Slide" / record["file"]
                require(file.is_file() and hashlib.sha256(file.read_bytes()).hexdigest() == record["sha256"], f"Slide differs/missing: {record['file']}")
                counts["source_pdfs"] += 1
        require(len(list((ROOT / "docs" / "hoc-tap").glob("[0-9][0-9]-*.md"))) == 12, "Expected twelve lesson documents")
    except (OSError, KeyError, TypeError, ValueError, ZeroDivisionError, struct.error) as exc:
        errors.append(f"Configuration/provenance validation failed: {exc}")

    report = {"scope": "project structure/configuration/provenance only; no P2P application tests", "checkedAtUtc": datetime.now(timezone.utc).isoformat(), "passed": not errors, "counts": counts, "errors": errors}
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=True, indent=2))
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())

"""Run the real ET4230 CLI over TCP and retain raw logs and CSV results."""

import argparse
import csv
import hashlib
import json
import platform
import random
import re
import socket
import statistics
import subprocess
import time
import uuid
from collections import defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CLI = ROOT / "src/ET4230.Cli/bin/Release/net10.0/ET4230.Cli.dll"
METRICS = re.compile(r"DOWNLOAD_METRICS peerId=\S+ seconds=([\d.]+) retries=(\d+)")


def sha256(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


class PeerProcess:
    def __init__(self, role, folder, name, config):
        config_path = folder / f"{name}.json"
        config_path.write_text(json.dumps(config, indent=2), encoding="utf-8")
        self.log = folder / f"{name}.out"
        self.err = folder / f"{name}.err"
        self.output = folder / f"{name}.bin"
        self.seconds = None
        self.retries = None
        self.observed_at = None
        self.left = False
        with self.log.open("wb") as out, self.err.open("wb") as err:
            self.process = subprocess.Popen(
                ["dotnet", str(CLI), role, "--config", str(config_path)], stdout=out, stderr=err,
            )

    def text(self):
        return self.log.read_text(encoding="utf-8", errors="replace")

    def wait_for(self, marker, deadline):
        while marker not in self.text():
            if self.process.poll() is not None or time.monotonic() >= deadline:
                raise RuntimeError(f"{marker} missing: {self.log} / {self.err}")
            time.sleep(0.025)

    def observe(self):
        if self.observed_at is None and (metrics := METRICS.search(self.text())):
            self.seconds = float(metrics[1])
            self.retries = int(metrics[2])
            self.observed_at = time.monotonic()

    def stop(self):
        if self.process.poll() is None:
            self.process.terminate()
        try:
            self.process.wait(timeout=4)
        except subprocess.TimeoutExpired:
            self.process.kill()
            self.process.wait(timeout=4)


def loopback_ports(count):
    # Hold all port reservations until distinct numbers have been selected.
    listeners = []
    try:
        for _ in range(count):
            listener = socket.socket()
            listeners.append(listener)
            listener.bind(("127.0.0.1", 0))
        return [listener.getsockname()[1] for listener in listeners]
    finally:
        for listener in listeners:
            listener.close()


def run_transfer(folder, cfg, count, scenario, mode, policy, revision, proof=False):
    folder.mkdir()
    size = cfg["fileSizeBytes"]
    source = folder / "input.bin"
    rng = random.Random(4230)
    with source.open("wb") as stream:
        for offset in range(0, size, 1024 * 1024):
            stream.write(rng.randbytes(min(1024 * 1024, size - offset)))
    expected = sha256(source)
    tracker_port, *peer_ports = loopback_ports(count + 1)
    tracker = {"host": "127.0.0.1", "bindHost": "127.0.0.1", "port": tracker_port,
               "heartbeatSeconds": 1, "ttlSeconds": 4}
    timeout = cfg["runDeadlineSeconds"]
    processes = []

    def start(role, name, port, upload_mbps):
        config = {
            "schemaVersion": 1,
            "tracker": tracker,
            "peer": {"port": port, "maxUploadSessions": 8},
            "transfer": {
                "pieceSizeBytes": cfg["pieceSizeBytes"], "blockSizeBytes": cfg["blockSizeBytes"],
                "requestTimeoutSeconds": 10, "shareAfterCompletionSeconds": min(3600, timeout),
            },
            "strategy": {"applicationUploadLimitMbps": upload_mbps, "pieceSelection": policy},
            "file": {"sourcePath": "input.bin", "manifestPath": "manifest.json", "outputPath": f"{name}.bin"},
        }
        process = PeerProcess(role, folder, name, config)
        processes.append(process)
        return process

    def receiver(index):
        rates = scenario["receiverApplicationUploadMbpsPattern"]
        return start("peer" if mode == "p2p" else "download", f"receiver-{index}",
                     peer_ports[index], rates[(index - 1) % len(rates)])

    receivers = []
    departure_applied = False
    run_error = None
    started = None
    seed = None
    try:
        start("tracker", "tracker", peer_ports[0], 0).wait_for("TRACKER_READY", time.monotonic() + 15)
        seed = start("seed", "seed", peer_ports[0], cfg["initialSeedApplicationUploadLimitMbps"])
        seed.wait_for("SEED_READY", time.monotonic() + 30)
        started = time.monotonic()
        deadline = started + timeout
        receivers.append(receiver(1))
        if proof:
            receivers[0].wait_for("PEER_READY", deadline)
        for index in range(2, count):
            if not proof:
                time.sleep(cfg.get("receiverStartGapSeconds", 0))
            receivers.append(receiver(index))
        depart_at = scenario["departAtSeconds"]
        while time.monotonic() < deadline:
            for peer in receivers:
                peer.observe()
            if depart_at is not None and time.monotonic() >= started + depart_at:
                candidates = receivers[:scenario.get("departingReceivers", 1)]
                for leaving in candidates:
                    if leaving.observed_at is None and leaving.process.poll() is None:
                        leaving.left = True
                        leaving.stop()
                departure_applied = all(leaving.left for leaving in candidates)
                depart_at = None
            if all(p.observed_at is not None or p.process.poll() is not None for p in receivers):
                break
            time.sleep(0.025)
    except (OSError, RuntimeError) as error:
        run_error = str(error)
    finally:
        exit_codes = [peer.process.poll() for peer in receivers]
        for process in reversed(processes):
            process.stop()

    rows = []
    for index in range(1, count):
        peer = receivers[index - 1] if index <= len(receivers) else None
        if peer is None:
            status, hash_ok, seconds = "not_started", False, None
        else:
            hash_ok = peer.output.is_file() and sha256(peer.output) == expected
            if peer.left:
                status = "left"
            elif hash_ok and peer.observed_at is not None:
                status = "completed"
            elif exit_codes[index - 1] is None and run_error is None and peer.observed_at is None:
                status = "timeout"
            else:
                status = "failed"
            seconds = peer.seconds if status == "completed" else None
        rows.append({
            "run_id": folder.name, "scenario": scenario["id"], "mode": mode, "policy": policy,
            "buffer_strategy": "managed", "total_peers": count, "receiver_id": index, "status": status,
            "file_size_bytes": size, "verified_payload_bytes": size if hash_ok else 0,
            "completion_seconds": seconds,
            "goodput_mbit_s": (8 * size / (seconds * 1e6)) if seconds else None,
            "hash_ok": hash_ok, "retries": peer.retries if peer else None, "input_sha256": expected, "revision": revision,
        })

    observed = [peer.observed_at - started for peer in receivers
                if peer.observed_at is not None and not peer.left]
    upload_bytes = {}
    for process in [p for p in [seed, *receivers] if p is not None]:
        upload_bytes[process.log.stem] = sum(
            int(n) for n in re.findall(r"(?m)^UPLOAD_BLOCK .* bytes=(\d+)$", process.text())
        )
    proof_ok = False if proof else None
    if proof and seed is not None and len(receivers) == 2:
        first, second = receivers
        first_identity = re.search(r"RECEIVER_STARTED peerId=(\S+)", first.text())
        seed_identity = re.search(r"SEED_READY .* peerId=(\S+)", seed.text())
        if first_identity and seed_identity:
            served_while_downloading = first.text().split("DOWNLOAD_OK", 1)[0]
            first_pieces = set(re.findall(r"(?m)^UPLOAD_BLOCK .* piece=(\d+) ", served_while_downloading))
            second_pieces = set(re.findall(
                rf"(?m)^PIECE_VERIFIED .* source={first_identity[1]} piece=(\d+) ", second.text()))
            proof_ok = bool(first_pieces & second_pieces) and f"source={seed_identity[1]} " in second.text()

    metadata = {
        "scenario": scenario["id"], "mode": mode, "policy": policy, "topology": "TCP loopback, one host",
        "revision": revision, "input_sha256": expected, "departure_applied": departure_applied,
        "run_error": run_error,
        "elapsed_to_last_observed_completion_seconds": max(observed, default=None),
        "upload_payload_bytes": upload_bytes, "shared_while_downloading": proof_ok,
    }
    (folder / "run.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    return rows, metadata


def write_csv(path, rows):
    with path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)


def summarize(rows, runs):
    groups = defaultdict(list)
    for row in rows:
        groups[(row["scenario"], row["mode"], row["policy"], row["total_peers"])].append(row)
    summary = []
    for (scenario, mode, policy, count), group in groups.items():
        finished = [row["completion_seconds"] for row in group if row["status"] == "completed"]
        ids = {row["run_id"] for row in group}
        seed_bytes = [runs[run_id]["upload_payload_bytes"]["seed"] for run_id in ids
                      if "seed" in runs[run_id]["upload_payload_bytes"]]
        summary.append({
            "scenario": scenario, "mode": mode, "policy": policy, "total_peers": count,
            "runs": len(ids), "receivers": len(group),
            "completed": len(finished), "left": sum(row["status"] == "left" for row in group),
            "timed_out": sum(row["status"] == "timeout" for row in group),
            "failed_or_not_started": sum(row["status"] in ("failed", "not_started") for row in group),
            "mean_completion_seconds_successful_receivers": statistics.mean(finished) if finished else None,
            "stdev_completion_seconds_successful_receivers": statistics.stdev(finished) if len(finished) > 1 else None,
            "mean_seed_upload_payload_bytes_per_logged_run": statistics.mean(seed_bytes) if seed_bytes else None,
        })
    return summary


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, default=ROOT / "configs/experiments.json")
    parser.add_argument("--output", type=Path, default=ROOT / "results/raw")
    parser.add_argument("--policy", choices=["sequential", "rarest-first"], default="sequential")
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--smoke", action="store_true", help="Small runs in both modes across the three scenarios.")
    group.add_argument("--prove-sharing", action="store_true", help="Require one receiver to upload while still downloading.")
    args = parser.parse_args()
    if not CLI.is_file():
        parser.error("Build ET4230.P2P.sln in Release first.")

    cfg = json.loads(args.config.read_text(encoding="utf-8"))
    scenarios = cfg["scenarios"]
    if args.smoke or args.prove_sharing:
        cfg.update(totalPeers=[3] if args.prove_sharing else [3, 4], runsPerConfiguration=1,
                   fileSizeBytes=2 * 1024 * 1024 + 3, pieceSizeBytes=128 * 1024, blockSizeBytes=16 * 1024,
                   initialSeedApplicationUploadLimitMbps=8, runDeadlineSeconds=30,
                   receiverStartGapSeconds=0.35)
        scenarios = [dict(s, departAtSeconds=1.5) if s["id"] == "S3" else s for s in scenarios]
    if args.prove_sharing:
        scenarios = [{"id": "sharing", "receiverApplicationUploadMbpsPattern": [80], "departAtSeconds": None}]
    if (not cfg["totalPeers"] or any(not 2 <= n <= 9 for n in cfg["totalPeers"])
            or cfg["runsPerConfiguration"] < 1 or cfg["fileSizeBytes"] < 1
            or not scenarios or len({s["id"] for s in scenarios}) != len(scenarios)
            or any(not re.fullmatch(r"[A-Za-z0-9_-]+", s["id"]) or not s["receiverApplicationUploadMbpsPattern"]
                   for s in scenarios)):
        parser.error("Use unique scenario IDs, 2..9 peers, positive repetitions and file size, and upload rates.")

    output = args.output.resolve() / f"{time.strftime('%Y%m%d-%H%M%S')}-{uuid.uuid4().hex[:8]}"
    output.mkdir(parents=True)
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    dirty = bool(subprocess.check_output(
        ["git", "status", "--porcelain", "--", "src", "tools", "configs"], cwd=ROOT, text=True).strip())
    if dirty:
        diff = subprocess.check_output(["git", "diff", "--binary", "HEAD", "--", "src", "tools", "configs"], cwd=ROOT)
        (output / "source.patch").write_bytes(diff)
        untracked = subprocess.check_output(
            ["git", "ls-files", "--others", "--exclude-standard", "--", "src", "tools", "configs"], cwd=ROOT, text=True)
        for name in untracked.splitlines():
            target = output / "untracked" / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes((ROOT / name).read_bytes())
    settings = {
        "platform": platform.platform(), "dotnet": subprocess.check_output(["dotnet", "--version"], cwd=ROOT, text=True).strip(),
        "revision": revision, "dirty": dirty, "config": cfg, "scenario_overrides": scenarios,
        "mode": "proof" if args.prove_sharing else "smoke" if args.smoke else "matrix",
    }
    (output / "experiment.json").write_text(json.dumps(settings, indent=2), encoding="utf-8")
    print(f"Results: {output}", flush=True)

    totals = defaultdict(int)
    all_rows = []
    runs = {}
    failed = False
    for count in cfg["totalPeers"]:
        for scenario in scenarios:
            for repetition in range(cfg["runsPerConfiguration"]):
                modes = ("p2p",) if args.prove_sharing else (
                    ("cs", "p2p") if repetition % 2 == 0 else ("p2p", "cs"))
                for mode in modes:
                    folder = output / f"{scenario['id']}-{count}-{mode}-{repetition + 1}"
                    policy = args.policy if mode == "p2p" else "sequential"
                    rows, metadata = run_transfer(folder, cfg, count, scenario, mode, policy, revision,
                                                  proof=args.prove_sharing)
                    all_rows.extend(rows)
                    runs[folder.name] = metadata
                    write_csv(output / "receivers.csv", all_rows)
                    write_csv(output / "summary.csv", summarize(all_rows, runs))
                    for row in rows:
                        totals[row["status"]] += 1
                    if any(r["status"] not in ("left", "completed") for r in rows) or metadata["run_error"]:
                        failed = True
                    if scenario["departAtSeconds"] is not None and not metadata["departure_applied"]:
                        failed = True
                    if args.prove_sharing and not metadata["shared_while_downloading"]:
                        failed = True
                    print(f"{folder.name}: {[r['status'] for r in rows]}, upload bytes {metadata['upload_payload_bytes']}",
                          flush=True)
                    # Keep one reproducible input hash and the logs; bulk data can be regenerated from the config.
                    for name in ["input.bin", *(f"receiver-{r['receiver_id']}.bin" for r in rows)]:
                        (folder / name).unlink(missing_ok=True)
    print(dict(totals))
    return int(failed)


if __name__ == "__main__":
    raise SystemExit(main())

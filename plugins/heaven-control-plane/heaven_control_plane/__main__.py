from __future__ import annotations

import argparse
import json

from .runtime import ControlPlane


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Heaven Control Plane contract/debug CLI")
    parser.add_argument("command", choices=["discover", "prepare"])
    parser.add_argument("--request", help="JSON request object for prepare")
    args = parser.parse_args(argv)
    plane = ControlPlane()
    if args.command == "discover":
        print(json.dumps(plane.discovery(), indent=2, sort_keys=True))
        return 0
    if not args.request:
        parser.error("--request is required for prepare")
    request = json.loads(args.request)
    captured = {}

    def capture(invocation):
        captured.update(
            {
                "job_id": invocation.job_id,
                "action": invocation.action,
                "params": invocation.params,
                "ttl_seconds": invocation.ttl_seconds,
                "priority": invocation.priority,
            }
        )
        return {"status": "completed", "prepared_only": True}

    result = plane.handle(request, capture)
    if result["ok"]:
        print(json.dumps(captured, indent=2, sort_keys=True))
        return 0
    print(json.dumps(result, indent=2, sort_keys=True))
    return 2


if __name__ == "__main__":
    raise SystemExit(main())


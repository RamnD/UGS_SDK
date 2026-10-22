#!/usr/bin/env python3
"""Reject missing, empty, failed or incomplete Unity test reports."""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

SMOKE_TESTS = {
    "SmokeScene_BootstrapsAndSendsEvent",
    "PeriodicFlush_SendsWithoutExplicitFlush",
    "TransientFailure_AutomaticallyRetriesSameEnvelope",
    "PendingEvent_SurvivesDisposeAndRecreation",
}


def verify(directory):
    cases = []
    failures = []
    for path in sorted(Path(directory).rglob("*.xml")):
        root = ET.parse(path).getroot()
        if root.tag not in ("test-run", "test-results"):
            continue
        cases.extend(root.iter("test-case"))
        if root.get("result") in ("Failed", "Failure", "Cancelled") or int(root.get("failed", "0")) > 0:
            failures.append(str(path))
    if not cases:
        raise ValueError("No Unity test cases were reported. Import, compilation or licensing may have failed.")
    smoke = {case.get("name") for case in cases if case.get("fullname", "").startswith("AnalyticsSmokeTests.") and case.get("result") == "Passed"}
    missing = SMOKE_TESTS - smoke
    if missing:
        raise ValueError("Missing or unsuccessful PlayMode scenarios: " + ", ".join(sorted(missing)))
    editor = [case for case in cases if case.get("fullname", "").startswith("RamndAnalyticsTests.") and case.get("result") == "Passed"]
    if not editor:
        raise ValueError("Existing SDK EditMode tests were not executed successfully. Check manifest testables.")
    failed_cases = [case.get("fullname", case.get("name", "unknown")) for case in cases if case.get("result") not in ("Passed", "Skipped", "Ignored", "Inconclusive")]
    if failures or failed_cases:
        raise ValueError("Unity test failures: " + ", ".join(failures + failed_cases))
    print("### Unity smoke results\n")
    print(f"Passed all {len(SMOKE_TESTS)} required PlayMode scenarios and {len(editor)} SDK EditMode tests.")
    print(f"Total reported test cases: {len(cases)}. See the unity-smoke artifact for XML results and logs.")


if __name__ == "__main__":
    try:
        verify(sys.argv[1])
    except (ValueError, OSError, ET.ParseError) as error:
        print(f"Unity result validation failed: {error}", file=sys.stderr)
        sys.exit(1)

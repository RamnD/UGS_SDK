#!/usr/bin/env python3
"""Validate the committed smoke project without installing Unity."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / "TestProject"
manifest = json.loads((PROJECT / "Packages/manifest.json").read_text())
packages = {
    "com.ramnd.gameservices-sdk": "gameservices-sdk",
    "com.ramnd.core-logging": "core-logging",
    "com.ramnd.service-fault": "service-fault",
    "com.ramnd.gameservices-servicefault": "gameservices-servicefault",
}
for name, folder in packages.items():
    value = manifest["dependencies"][name]
    assert value == "file:../../packages/" + folder, (name, value)
    package = (PROJECT / "Packages" / value.removeprefix("file:")).resolve()
    assert package == ROOT / "packages" / folder
    assert json.loads((package / "package.json").read_text())["name"] == name
assert "com.ramnd.gameservices-sdk" in manifest["testables"]
assert re.fullmatch(r"m_EditorVersion: \d+\.\d+\.\d+f\d+\n?", (PROJECT / "ProjectSettings/ProjectVersion.txt").read_text())

guids = {}
assets = PROJECT / "Assets"
for path in assets.rglob("*"):
    if path.suffix == ".meta":
        continue
    meta = path.with_name(path.name + ".meta")
    assert meta.is_file(), f"Missing {meta}"
    match = re.search(r"^guid: ([0-9a-f]{32})$", meta.read_text(), re.M)
    assert match, meta
    guid = match[1]
    assert guid not in guids, f"Duplicate GUID: {guid}"
    guids[guid] = path
for meta in assets.rglob("*.meta"):
    assert meta.with_suffix("").exists(), f"Orphan meta: {meta}"
for path in assets.rglob("*.asmdef"):
    json.loads(path.read_text())
scene = assets / "Scenes/SdkSmoke.unity"
bootstrap_meta = assets / "Smoke/SdkSmokeBootstrap.cs.meta"
bootstrap_guid = re.search(r"^guid: ([0-9a-f]{32})$", bootstrap_meta.read_text(), re.M)[1]
assert "guid: " + bootstrap_guid in scene.read_text()
scene_guid = re.search(r"^guid: ([0-9a-f]{32})$", scene.with_suffix(".unity.meta").read_text(), re.M)[1]
build_settings = (PROJECT / "ProjectSettings/EditorBuildSettings.asset").read_text()
assert "Assets/Scenes/SdkSmoke.unity" in build_settings and scene_guid in build_settings
assert "productName: UGS SDK Smoke Tests" in (PROJECT / "ProjectSettings/ProjectSettings.asset").read_text()
assert not list((ROOT / "packages").glob("*/TestProject"))
print("Smoke project layout, local package bindings, scene references and asset metadata are valid.")

# SDK Unity smoke harness

This is a development-only Unity project outside all UPM package directories. Games installing `?path=packages/gameservices-sdk` do not import this project's scene, settings or tests. No package version or release tag is needed to test a commit.

## Cloud operation

The `Unity smoke` GitHub Actions workflow checks out the exact PR head, validates the project layout, imports all four RamnD packages through local folder dependencies, and runs Unity Test Framework EditMode and PlayMode suites in a GameCI container. Unity is pinned to **6000.3.8f1** in `ProjectSettings/ProjectVersion.txt`; this job does not certify the package's minimum supported Unity 2022.3 or mobile players.

After one-time licensing setup, PRs into `agent/jules`, `staging` and `main`, pushes to those branches, and manual workflow dispatch run without opening Unity locally. The initial feature branch also runs on push, so the first cloud attempt can happen before merge. An existing run does not pick up later YAML edits: start a new run at the new commit.

Results are attached to the Actions run as `unity-smoke-<head-sha>`: NUnit XML and available Unity logs. Missing/empty results, missing required smoke scenarios, failed tests, compilation failures and missing activation fail the job. `Smoke project layout` passing alone is not evidence that Unity ran.

## One-time Unity activation

An organization maintainer must provide a valid Unity activation method in repository Actions secrets. There are no game or backend credentials in this harness.

| Activation method | Repository secrets |
| --- | --- |
| Supported serial-based paid license | `UNITY_EMAIL`, `UNITY_PASSWORD`, `UNITY_SERIAL` |
| Supported Unity license file | `UNITY_EMAIL`, `UNITY_PASSWORD`, `UNITY_LICENSE` (license file contents) |
| Existing reachable floating-license server | `UNITY_LICENSING_SERVER` |

Use the method supported by your Unity entitlement and chosen Editor version. See [GameCI activation](https://game.ci/docs/github/activation/) and [test runner configuration](https://game.ci/docs/github/test-runner/). A Personal license file may require a one-time Unity Hub activation; this repository does not generate or bypass Unity licenses. Serial/server activation can keep project setup and test execution entirely in the cloud. License expiration, account policy changes and Unity upgrades may require maintainer intervention.

The workflow does not use the Jules handoff PAT. Unity code executes in a regular `pull_request`/`push` workflow, never a privileged `pull_request_target` checkout. Fork PRs receive layout validation only; a skipped Unity job does not validate their runtime. All same-repository branches that receive license secrets must be trusted. Review before copying external changes to such a branch.

## Covered behavior

| Suite | Checks |
| --- | --- |
| SDK EditMode | Existing `RamndAnalyticsTests` exposed through `manifest.json` `testables` |
| PlayMode scene bootstrap | Load the committed scene, create analytics via its public constructor, deliver an event, unload and dispose |
| PlayMode timer | Deliver below a full batch without explicit flush |
| PlayMode retry | HTTP 503 followed by automatic retry preserves the event ID |
| PlayMode persistence | An offline event survives disposal and a new system instance, then drains once |

The PlayMode cases use the real SDK assemblies, Unity synchronization context, timer and disk queue. Only HTTP ingest is replaced by an asynchronous local transport. Automatic-delivery cases require Unity to report a reachable network interface but never send an HTTP request. Tests have bounded waits and retain Unity Test Framework's unexpected-error handling.

The two existing corrupted-snapshot EditMode tests explicitly expect their known error log using `LogAssert.Expect`. Unexpected errors remain failures; logging is not disabled globally.

`SmokeEnvironment.ResetQueue` deletes only the analytics queue files in this test application's persistent-data directory and refuses to run under another product name. The project uses a dedicated product name and standalone identifier; do not copy the harness into a game.

This is not full facade initialization or live UGS integration. Authentication, purchases, ads, platform plugins, IL2CPP and device behavior require additional targeted suites. Existing package import/assembly errors are intentionally visible; the harness does not patch or stub third-party assemblies to make imports pass.

## Optional local use

Open `TestProject` with its pinned Unity version and run `Assets/Scenes/SdkSmoke.unity`, or use Test Runner. CI is the supported way to verify the project without a local Unity installation. Generated `Library`, logs and the initially resolved package lock are ignored; direct SDK dependencies remain as declared by the checked-out packages. After the first successful import, review and commit a generated package lock if reproducible transitive resolution is required.

## Adding a regression

Add a focused PlayMode case under `Assets/Tests/PlayMode` that reproduces the observed runtime failure, asserts the expected result and disposes created services. Keep it independent of production accounts and services. Run the cloud workflow against the fix branch before promotion. New assets require committed `.meta` files.

Recommended release classification: `release:none` for this infrastructure change.

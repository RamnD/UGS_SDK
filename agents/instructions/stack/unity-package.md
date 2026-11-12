# Unity package (UPM)

A `package.json` with a `"unity"` field is a sufficient marker, at the root or in `packages/*/package.json`. Unity also accepts a manifest that has `name` and `version` and no `unity` field. A path listed as a UPM package in the `PROJECT.md` map uses this document even when that field is absent. Either match is a Package Manager manifest, not an npm manifest. Do not run `npm install` or `pnpm install` for it. This check comes before the rule for an ordinary `package.json`. A `package.json` with neither the field nor a map entry stays an ordinary package.

Keep package boundaries. Public interfaces, serialized fields, package IDs, assembly names, UPM paths, persistence keys, and GUIDs are compatibility contracts. Editor code belongs to an Editor-only assembly; use the existing asmdef/asmref layout. Read [unity.md](unity.md) for assembly isolation, serialization, field style, method comments, and permitted checks.

Runtime code does not reference `UnityEditor`. An optional integration stays behind its existing assembly definition, `versionDefines`, `defineConstraints`, and platform guards. A consumer without that optional package still compiles. A package does not take a hard dependency on a game project, scene, Resources asset, or project singleton.

Do not edit generated, vendored, or third-party content unless the task explicitly targets it. Keep the license file that already ships with the package. Do not replace, remove, or relicense it. Do not commit credentials, service-account files, signing material, store secrets, or a consumer's production identifiers.

An asynchronous flow states cancellation, timeout, duplicate callbacks, retries, and late completion. An indeterminate result is not reported as success and is not turned into a destructive failure.

Read the affected package README and changelog before changing a public contract. Update documentation when installation, configuration, public behavior, or migration requirements change. A breaking change names the migration path. Do not silently remove or rename a public API. Package dependency declarations match the assembly references. Version facts come from the current manifests, tags, and changelog, not from a version copied into a prompt.

A release task changes the manifest version and the changelog entry. A feature change does not. Do not align neighboring package versions along the way. Versions already differ across packages in one repository.

Branches, labels, and merges come from the version-control document and `PROJECT.md`, not from this file.

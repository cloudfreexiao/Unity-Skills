---
name: unity-yooasset
description: "YooAsset hot-update / asset bundle automation. Build bundles via ScriptableBuildPipeline or RawFileBuildPipeline, run Editor simulate builds, configure AssetBundleCollector (package / group / collector tree + rule routing), and analyze BuildReport JSON for bundle sizes, dependencies, and orphaned assets. Triggers: yooasset, YooAsset, AssetBundleBuilder, AssetBundleCollector, BuildReport, ResourcePackage, ScriptableBuildPipeline, RawFileBuildPipeline, EditorSimulateBuildPipeline, CollectorPackage, CollectorGroup, AddressRule, PackRule, FilterRule, IgnoreRule, IndependAsset, 资源打包, 资源收集, 资源收集器, 资源规则, 构建报告, 构建管线, 模拟构建, 热更新打包, 资源分组, 资产分析, 孤立资源, asset bundle build, asset bundle collector, build report, hot update build, bundle dependency, orphan asset, asset pipeline. Requires com.tuyoogame.yooasset (2.3.15+)."
---

# Unity YooAsset Skills

Editor-side automation for the YooAsset hot-update framework — build pipeline orchestration, Collector configuration CRUD, and BuildReport analysis. Every skill wraps a concrete YooAsset Editor API (anchored to 2.3.18 source). When the package is absent, every skill except `yooasset_check_installed` returns a `NoYooAsset()` error with install instructions.

> **Requires**: `com.tuyoogame.yooasset` **≥ 2.3.15**, Unity 2022.3+ (validated against 2.3.18).
> **Strongly recommended**: before writing ANY YooAsset runtime code, load [yooasset-design](../yooasset-design/SKILL.md). PlayMode / parameter-class / handle-lifecycle pitfalls are strict, and only the advisory module surfaces them.

## Guardrails

**Mode**: Both (Semi-Auto + Full-Auto).

**DO NOT** (common hallucinations):
- `yooasset_initialize` / `yooasset_load_asset` / `yooasset_create_downloader` — do NOT exist. Runtime APIs (`YooAssets.Initialize`, `ResourcePackage.LoadAssetAsync`, `RequestPackageVersionAsync`, `CreateResourceDownloader`) belong in game code, not Editor REST skills. Use the [yooasset-design](../yooasset-design/SKILL.md) advisory to write them correctly.
- `yooasset_remove_collector_package` / `yooasset_modify_group` — NOT provided. Collector deletion / structural rewrites are better done in the YooAsset AssetBundle Collector window (`yooasset_open_collector_window`).
- `yooasset_install` — NOT a skill. Package install is a Package Manager user action.
- Do NOT pass `timeout` to `yooasset_build_bundles` — the property was removed in YooAsset 2.3.16. Runtime watchdog now lives on `CacheFileSystemParameters.DOWNLOAD_WATCH_DOG_TIME` (runtime concern, not a build parameter).
- Do NOT call `yooasset_build_bundles` while `EditorUserBuildSettings.isBuildingPlayer == true` — `BuildParameters.CheckBuildParameters` throws.

**Routing**:
- Collector configuration (packages, groups, collectors, rules) → this module.
- Actual bundle build + simulate + paths → this module.
- Build report analysis (bundle size, dependency, orphan) → this module.
- Runtime code (Initialize / Load / Download / Scene) → write it yourself using [yooasset-design](../yooasset-design/SKILL.md) — no REST skills here for those.
- Version-policy decisions (2.x vs pre-2.3.15 manifest, `FindAssetType` on `IFilterRule`) → [yooasset-design/PITFALLS.md](../yooasset-design/PITFALLS.md).

## Skills

### Environment (1)
| Skill | Purpose | Key Parameters |
|-------|---------|----------------|
| `yooasset_check_installed` | Reflection probe — works even when the package is missing. Reports runtime assembly, package version, editor availability, and which of the 4 pipelines are present. | (none) |

### Build pipeline (5)
| Skill | Purpose | Key Parameters |
|-------|---------|----------------|
| `yooasset_build_bundles` | Build bundles via `ScriptableBuildPipeline` or `RawFileBuildPipeline`. Writes to `<projectPath>/Bundles/<PackageName>/<Version>`. | `packageName`, `packageVersion="auto"`, `pipeline="ScriptableBuildPipeline"`, `buildTarget?`, `compression="LZ4"`, `fileNameStyle="HashName"`, `clearBuildCache=false`, `useAssetDependencyDB=true`, `verifyBuildingResult=true`, `replaceAssetPathWithAddress=false`, `enableLog=true` |
| `yooasset_simulate_build` | Run `AssetBundleSimulateBuilder.SimulateBuild` for an `EditorSimulateMode` package — virtual bundles only, no files written. | `packageName` |
| `yooasset_get_default_paths` | Return `BuildOutputRoot` + `StreamingAssetsRoot` that YooAsset uses. | (none) |
| `yooasset_open_builder_window` | Open the YooAsset `AssetBundle Builder` Editor window. | (none) |
| `yooasset_open_collector_window` | Open the YooAsset `AssetBundle Collector` Editor window. | (none) |

### Collector configuration (6)
| Skill | Purpose | Key Parameters |
|-------|---------|----------------|
| `yooasset_list_collector_packages` | List Packages → Groups → Collectors tree. `verbose=true` returns the full descent. | `verbose=false` |
| `yooasset_list_collector_rules` | Return registered Active / Address / Pack / Filter / Ignore rule classes. | `ruleKind="all"` (or `activeRule|addressRule|packRule|filterRule|ignoreRule`) |
| `yooasset_create_collector_package` | Create a new Collector Package. Fails on duplicate unless `allowDuplicate=true`. | `packageName`, `allowDuplicate=false` |
| `yooasset_create_collector_group` | Create a Group inside an existing Package. | `packageName`, `groupName`, `groupDesc=""`, `activeRule="EnableGroup"`, `assetTags=""` |
| `yooasset_add_collector` | Add an `AssetBundleCollector` to a Group. Validates `collectPath` via AssetDatabase and each rule via its registered class name. | `packageName`, `groupName`, `collectPath`, `collectorType="MainAssetCollector"`, `addressRule="AddressByFileName"`, `packRule="PackDirectory"`, `filterRule="CollectAll"`, `assetTags=""`, `userData=""` |
| `yooasset_save_collector_config` | Persist the `AssetBundleCollectorSetting.asset`. Optionally run `FixFile()` to repair dangling rule references. | `fixErrors=true` |

### Build report analysis (4)
| Skill | Purpose | Key Parameters |
|-------|---------|----------------|
| `yooasset_load_build_report` | Deserialize a BuildReport JSON and return `ReportSummary` metadata + top-level totals. | `reportPath` |
| `yooasset_list_report_bundles` | Paginated / filtered / sorted bundle listing from the report. | `reportPath`, `filterEncrypted?`, `filterTag?`, `sortBy="size"` (`size|name|refCount|dependCount`), `limit=100`, `offset=0` |
| `yooasset_get_bundle_detail` | Full `ReportBundleInfo` for a single bundle — `DependBundles`, `ReferenceBundles`, asset `BundleContents`. | `reportPath`, `bundleName` |
| `yooasset_list_independ_assets` | Assets not referenced by any main asset — cleanup candidates. | `reportPath`, `limit=100`, `offset=0` |

## Quick Start

```python
import unity_skills as u

# 0. Probe installation (works even if package is missing)
u.call_skill("yooasset_check_installed")
# -> { installed, packageVersion, availablePipelines: [...], editorAvailable, ... }

# 1. Configure collectors (end-to-end)
u.call_skill("yooasset_list_collector_rules", ruleKind="all")  # discover available rule class names
u.call_skill("yooasset_create_collector_package", packageName="DefaultPackage")
u.call_skill("yooasset_create_collector_group",
    packageName="DefaultPackage", groupName="UI", activeRule="EnableGroup",
    assetTags="ui")
u.call_skill("yooasset_add_collector",
    packageName="DefaultPackage", groupName="UI",
    collectPath="Assets/GameRes/UI",
    collectorType="MainAssetCollector",
    addressRule="AddressByFileName",
    packRule="PackDirectory",
    filterRule="CollectAll")
u.call_skill("yooasset_save_collector_config", fixErrors=True)

# 2. Simulate build (EditorSimulateMode) — fast, no file I/O
u.call_skill("yooasset_simulate_build", packageName="DefaultPackage")
# -> { packageRootDirectory: ".../Bundles/DefaultPackage/Simulate" }

# 3. Real build
result = u.call_skill("yooasset_build_bundles",
    packageName="DefaultPackage",
    packageVersion="1.0.0",
    pipeline="ScriptableBuildPipeline",
    compression="LZ4",
    fileNameStyle="HashName",
    verifyBuildingResult=True)

# 4. Analyze the report
import os
report_path = os.path.join(result["outputDirectory"], "BuildReport.json")
u.call_skill("yooasset_load_build_report", reportPath=report_path)
u.call_skill("yooasset_list_report_bundles", reportPath=report_path, sortBy="size", limit=20)
u.call_skill("yooasset_list_independ_assets", reportPath=report_path)
```

## Critical Rules (must read)

1. **`yooasset_check_installed` is the ONLY skill that works without `YOO_ASSET` compile define.** Every other skill returns `NoYooAsset()` — correct the environment (install or upgrade the package, then let Unity recompile) before retrying.
2. **Pipeline ↔ Parameters pairing is enforced.** `yooasset_build_bundles pipeline=ScriptableBuildPipeline` uses `ScriptableBuildParameters`; `pipeline=RawFileBuildPipeline` uses `RawFileBuildParameters`. `EditorSimulateBuildPipeline` is rejected here — use `yooasset_simulate_build`. `BuiltinBuildPipeline` is legacy and explicitly rejected.
3. **`packageVersion="auto"` fills `DateTime.UtcNow.ToString("yyyyMMddHHmm")`.** Pass a semver string when you want deterministic versioning.
4. **`yooasset_add_collector` validates `collectPath`** via `AssetDatabase.LoadAssetAtPath`. Paths must be under `Assets/...`; absolute or relative `../` paths will fail.
5. **Rule names in Create Skills are CLASS names, not display names.** Use `yooasset_list_collector_rules` to discover valid values (e.g. `EnableGroup`, `AddressByFileName`, `PackDirectory`, `CollectAll`, `NormalIgnoreRule`).
6. **Collector mutations are persisted by `yooasset_save_collector_config`.** Create skills mark `IsDirty=true` but do NOT auto-save — batch several edits, then save once.
7. **`yooasset_build_bundles` is NOT undo-tracked** (external file I/O, not scene/asset state). `clearBuildCache=true` re-runs the full pipeline; defaults avoid it for incremental speed.
8. **BuildReport JSON lives next to bundles** — by default at `<BuildOutputRoot>/<PackageName>/<Version>/BuildReport.json`. `yooasset_list_report_bundles` reads it standalone, so you can also point it at an archived copy.
9. **`yooasset_list_independ_assets` identifies assets not referenced by any main asset** — candidates for the Collector to drop. Verify manually before removing; a main asset created later could still need them.

## Version Scope

- **Target**: YooAsset `2.3.18` (published 2025-12-04). Source anchors use this version.
- **Minimum**: `2.3.15` (enforced by the asmdef `versionDefines` entry). Earlier versions use an incompatible manifest format and pre-`FindAssetType` `IFilterRule` (would not compile against this Skill module).
- **2.3.17** fixed a critical CRC validation bug; users on 2.3.15/2.3.16 should upgrade before relying on `verifyBuildingResult=true`.
- Runtime-side rules (PlayMode mapping, handle lifecycle, update flow, legacy-API migration) → [yooasset-design/SKILL.md](../yooasset-design/SKILL.md).

## Exact Signatures

For authoritative parameter names, defaults, and return fields, query `GET /skills/schema?category=YooAsset` or `unity_skills.get_skill_schema()`. This document is a routing / best-practice guide, not the signature source.

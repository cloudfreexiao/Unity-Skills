using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

#if YOO_ASSET
using YooAsset;
using YooAsset.Editor;
#endif

namespace UnitySkills
{
    /// <summary>
    /// YooAsset Editor-side skills — build pipeline orchestration, Collector configuration,
    /// and build-report analysis. Requires com.tuyoogame.yooasset (2.3.15+).
    ///
    /// `yooasset_check_installed` works WITHOUT the package via reflection; every other skill
    /// returns a NoYooAsset() hint when the package is missing. All API calls anchor to YooAsset
    /// 2.3.18 Editor source — see yooasset-design advisory module for the design contract.
    /// </summary>
    public static class YooAssetSkills
    {
#if !YOO_ASSET
        private static object NoYooAsset() => new
        {
            error = "YooAsset package (com.tuyoogame.yooasset) is not installed or below 2.3.15. " +
                    "Install via Window > Package Manager > Add package from git URL > " +
                    "https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset#2.3.18"
        };
#endif

        // ==================================================================================
        // A. Environment (1 skill) — works WITHOUT the YOO_ASSET define (pure reflection)
        // ==================================================================================

        [UnitySkill("yooasset_check_installed",
            "Report YooAsset installation status, runtime version, available Editor pipelines, and Collector subsystem availability. Runs with or without the package installed.",
            Category = SkillCategory.YooAsset, Operation = SkillOperation.Query,
            Tags = new[] { "yooasset", "package", "install", "check", "environment" },
            Outputs = new[] { "installed", "packageVersion", "runtimeAssembly", "editorAvailable", "availablePipelines", "hasCollectorSetting", "compileDefineSet" },
            ReadOnly = true)]
        public static object CheckInstalled()
        {
            var runtimeType = Type.GetType("YooAsset.YooAssets, YooAsset");
            if (runtimeType == null)
            {
                return new
                {
                    installed = false,
                    reason = "Runtime type 'YooAsset.YooAssets, YooAsset' is not resolvable. com.tuyoogame.yooasset is not installed.",
                    hint = "Install via Package Manager git URL: https://github.com/tuyoogame/YooAsset.git?path=Assets/YooAsset#2.3.18"
                };
            }

            var runtimeAssemblyVersion = runtimeType.Assembly.GetName().Version?.ToString();
            var editorType = Type.GetType("YooAsset.Editor.AssetBundleCollectorSettingData, YooAsset.Editor");
            var pipelineNames = new[] { "EditorSimulateBuildPipeline", "BuiltinBuildPipeline", "ScriptableBuildPipeline", "RawFileBuildPipeline" };
            var availablePipelines = pipelineNames
                .Where(n => Type.GetType($"YooAsset.Editor.{n}, YooAsset.Editor") != null)
                .ToArray();

            string packageVersion = null;
            try
            {
                var pkgInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(runtimeType.Assembly);
                packageVersion = pkgInfo?.version;
            }
            catch { /* best-effort; ignore */ }

            return new
            {
                installed = true,
                packageVersion,
                runtimeAssembly = runtimeAssemblyVersion,
                editorAvailable = editorType != null,
                availablePipelines,
                hasCollectorSetting = editorType != null,
                compileDefineSet =
#if YOO_ASSET
                    true,
#else
                    false,
#endif
                note = "compileDefineSet=false means YOO_ASSET symbol is not defined; other yooasset_* skills will return NoYooAsset() until Unity recompiles with the package active."
            };
        }

        // ==================================================================================
        // B. Build pipeline (5 skills)
        // ==================================================================================

        [UnitySkill("yooasset_build_bundles",
            "Build YooAsset bundles via ScriptableBuildPipeline or RawFileBuildPipeline. Writes bundles to BuildOutputRoot/<PackageName>/<Version>.",
            Category = SkillCategory.YooAsset, Operation = SkillOperation.Execute,
            Tags = new[] { "yooasset", "build", "bundles", "pipeline" },
            Outputs = new[] { "success", "outputDirectory", "packageVersion", "errorInfo", "failedTask" },
            MutatesAssets = true, RiskLevel = "medium", SupportsDryRun = false,
            RequiresPackages = new[] { "com.tuyoogame.yooasset" })]
        public static object BuildBundles(
            string packageName,
            string packageVersion = "auto",
            string pipeline = "ScriptableBuildPipeline",
            string buildTarget = null,
            string compression = "LZ4",
            string fileNameStyle = "HashName",
            bool clearBuildCache = false,
            bool useAssetDependencyDB = true,
            bool verifyBuildingResult = true,
            bool replaceAssetPathWithAddress = false,
            bool enableLog = true)
        {
#if !YOO_ASSET
            return NoYooAsset();
#else
            if (string.IsNullOrEmpty(packageName))
                return new { error = "packageName is required." };

            if (!Enum.TryParse<EBuildPipeline>(pipeline, out var eBp))
                return new { error = $"Unknown pipeline: {pipeline}. Available: {string.Join(", ", Enum.GetNames(typeof(EBuildPipeline)))}" };

            if (eBp == EBuildPipeline.EditorSimulateBuildPipeline)
                return new { error = "Use yooasset_simulate_build for EditorSimulateBuildPipeline." };

            var target = string.IsNullOrEmpty(buildTarget)
                ? EditorUserBuildSettings.activeBuildTarget
                : (BuildTarget)Enum.Parse(typeof(BuildTarget), buildTarget, true);

            var version = (packageVersion == "auto" || string.IsNullOrEmpty(packageVersion))
                ? DateTime.UtcNow.ToString("yyyyMMddHHmm")
                : packageVersion;

            if (!Enum.TryParse<EFileNameStyle>(fileNameStyle, out var eFns))
                return new { error = $"Unknown fileNameStyle: {fileNameStyle}. Available: HashName, BundleName, BundleName_HashName" };

            BuildParameters buildParameters;
            IBuildPipeline iPipeline;
            int buildBundleType;

            if (eBp == EBuildPipeline.ScriptableBuildPipeline)
            {
                if (!Enum.TryParse<ECompressOption>(compression, out var eCompress))
                    return new { error = $"Unknown compression: {compression}. Available: Uncompressed, LZMA, LZ4" };

                var sbp = new ScriptableBuildParameters
                {
                    CompressOption = eCompress,
                    ReplaceAssetPathWithAddress = replaceAssetPathWithAddress
                };
                buildParameters = sbp;
                iPipeline = new ScriptableBuildPipeline();
                buildBundleType = (int)EBuildBundleType.AssetBundle;
            }
            else if (eBp == EBuildPipeline.RawFileBuildPipeline)
            {
                buildParameters = new RawFileBuildParameters();
                iPipeline = new RawFileBuildPipeline();
                buildBundleType = (int)EBuildBundleType.RawBundle;
            }
            else // BuiltinBuildPipeline
            {
                return new { error = "BuiltinBuildPipeline is legacy and no longer recommended; use ScriptableBuildPipeline." };
            }

            buildParameters.BuildOutputRoot = AssetBundleBuilderHelper.GetDefaultBuildOutputRoot();
            buildParameters.BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot();
            buildParameters.BuildPipeline = eBp.ToString();
            buildParameters.BuildBundleType = buildBundleType;
            buildParameters.BuildTarget = target;
            buildParameters.PackageName = packageName;
            buildParameters.PackageVersion = version;
            buildParameters.FileNameStyle = eFns;
            buildParameters.ClearBuildCacheFiles = clearBuildCache;
            buildParameters.UseAssetDependencyDB = useAssetDependencyDB;
            buildParameters.VerifyBuildingResult = verifyBuildingResult;
            buildParameters.BuildinFileCopyOption = EBuildinFileCopyOption.None;
            buildParameters.BuildinFileCopyParams = string.Empty;

            BuildResult result;
            try
            {
                result = iPipeline.Run(buildParameters, enableLog);
            }
            catch (Exception ex)
            {
                return new { success = false, error = ex.Message, exceptionType = ex.GetType().Name };
            }

            return new
            {
                success = result.Success,
                packageName,
                packageVersion = version,
                pipeline = eBp.ToString(),
                buildTarget = target.ToString(),
                outputDirectory = result.OutputPackageDirectory,
                errorInfo = result.ErrorInfo,
                failedTask = result.FailedTask
            };
#endif
        }

        [UnitySkill("yooasset_simulate_build",
            "Run the EditorSimulateBuildPipeline for a package — produces a virtual bundle map without writing real bundles. Use for EditorSimulateMode during development.",
            Category = SkillCategory.YooAsset, Operation = SkillOperation.Execute,
            Tags = new[] { "yooasset", "simulate", "editor", "pipeline" },
            Outputs = new[] { "success", "packageRootDirectory" },
            RiskLevel = "low",
            RequiresPackages = new[] { "com.tuyoogame.yooasset" })]
        public static object SimulateBuild(string packageName)
        {
#if !YOO_ASSET
            return NoYooAsset();
#else
            if (string.IsNullOrEmpty(packageName))
                return new { error = "packageName is required." };

            try
            {
                var param = new PackageInvokeBuildParam(packageName)
                {
                    BuildPipelineName = EBuildPipeline.EditorSimulateBuildPipeline.ToString()
                };
                var result = AssetBundleSimulateBuilder.SimulateBuild(param);
                return new
                {
                    success = true,
                    packageName,
                    packageRootDirectory = result.PackageRootDirectory
                };
            }
            catch (Exception ex)
            {
                return new { success = false, error = ex.Message, exceptionType = ex.GetType().Name };
            }
#endif
        }

        [UnitySkill("yooasset_get_default_paths",
            "Return the default build output directory (under <projectPath>/Bundles) and the StreamingAssets root YooAsset ships bundles into.",
            Category = SkillCategory.YooAsset, Operation = SkillOperation.Query,
            Tags = new[] { "yooasset", "path", "build", "streamingassets" },
            Outputs = new[] { "defaultBuildOutputRoot", "streamingAssetsRoot" },
            ReadOnly = true,
            RequiresPackages = new[] { "com.tuyoogame.yooasset" })]
        public static object GetDefaultPaths()
        {
#if !YOO_ASSET
            return NoYooAsset();
#else
            return new
            {
                defaultBuildOutputRoot = AssetBundleBuilderHelper.GetDefaultBuildOutputRoot(),
                streamingAssetsRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot()
            };
#endif
        }

        [UnitySkill("yooasset_open_builder_window",
            "Open the YooAsset 'AssetBundle Builder' Editor window (menu: YooAsset/AssetBundle Builder).",
            Category = SkillCategory.YooAsset, Operation = SkillOperation.Execute,
            Tags = new[] { "yooasset", "window", "builder", "editor" },
            Outputs = new[] { "opened" },
            RequiresPackages = new[] { "com.tuyoogame.yooasset" })]
        public static object OpenBuilderWindow()
        {
#if !YOO_ASSET
            return NoYooAsset();
#else
            bool opened = EditorApplication.ExecuteMenuItem("YooAsset/AssetBundle Builder");
            return new { opened, menuPath = "YooAsset/AssetBundle Builder" };
#endif
        }

        [UnitySkill("yooasset_open_collector_window",
            "Open the YooAsset 'AssetBundle Collector' Editor window (menu: YooAsset/AssetBundle Collector).",
            Category = SkillCategory.YooAsset, Operation = SkillOperation.Execute,
            Tags = new[] { "yooasset", "window", "collector", "editor" },
            Outputs = new[] { "opened" },
            RequiresPackages = new[] { "com.tuyoogame.yooasset" })]
        public static object OpenCollectorWindow()
        {
#if !YOO_ASSET
            return NoYooAsset();
#else
            bool opened = EditorApplication.ExecuteMenuItem("YooAsset/AssetBundle Collector");
            return new { opened, menuPath = "YooAsset/AssetBundle Collector" };
#endif
        }

        // ==================================================================================
        // C. Collector configuration (6 skills)
        // ==================================================================================

        [UnitySkill("yooasset_list_collector_packages",
            "List all collector packages with their groups and collectors. Set verbose=true for full group + collector trees.",
            Category = SkillCategory.YooAsset, Operation = SkillOperation.Query,
            Tags = new[] { "yooasset", "collector", "package", "list" },
            Outputs = new[] { "packageCount", "packages" },
            ReadOnly = true,
            RequiresPackages = new[] { "com.tuyoogame.yooasset" })]
        public static object ListCollectorPackages(bool verbose = false)
        {
#if !YOO_ASSET
            return NoYooAsset();
#else
            var setting = AssetBundleCollectorSettingData.Setting;
            var packages = setting.Packages.Select(p =>
            {
                var collectorCount = p.Groups.Sum(g => g.Collectors.Count);
                var allTags = p.GetAllTags();
                if (!verbose)
                {
                    return (object)new
                    {
                        name = p.PackageName,
                        desc = p.PackageDesc,
                        groupCount = p.Groups.Count,
                        collectorCount,
                        allTags,
                        enableAddressable = p.EnableAddressable,
                        supportExtensionless = p.SupportExtensionless,
                        locationToLower = p.LocationToLower,
                        includeAssetGUID = p.IncludeAssetGUID,
                        autoCollectShaders = p.AutoCollectShaders,
                        ignoreRule = p.IgnoreRuleName
                    };
                }
                return new
                {
                    name = p.PackageName,
                    desc = p.PackageDesc,
                    groupCount = p.Groups.Count,
                    collectorCount,
                    allTags,
                    enableAddressable = p.EnableAddressable,
                    supportExtensionless = p.SupportExtensionless,
                    locationToLower = p.LocationToLower,
                    includeAssetGUID = p.IncludeAssetGUID,
                    autoCollectShaders = p.AutoCollectShaders,
                    ignoreRule = p.IgnoreRuleName,
                    groups = p.Groups.Select(g => new
                    {
                        name = g.GroupName,
                        desc = g.GroupDesc,
                        activeRule = g.ActiveRuleName,
                        assetTags = g.AssetTags,
                        collectors = g.Collectors.Select(c => new
                        {
                            collectPath = c.CollectPath,
                            collectorType = c.CollectorType.ToString(),
                            addressRule = c.AddressRuleName,
                            packRule = c.PackRuleName,
                            filterRule = c.FilterRuleName,
                            assetTags = c.AssetTags,
                            userData = c.UserData
                        }).ToArray()
                    }).ToArray()
                };
            }).ToArray();

            return new
            {
                packageCount = packages.Length,
                showPackageView = setting.ShowPackageView,
                uniqueBundleName = setting.UniqueBundleName,
                packages
            };
#endif
        }

        [UnitySkill("yooasset_list_collector_rules",
            "List available Address / Pack / Filter / Active / Ignore rule classes registered in the AssetBundleCollectorSettingData. Set ruleKind=<all|addressRule|packRule|filterRule|activeRule|ignoreRule>.",
            Category = SkillCategory.YooAsset, Operation = SkillOperation.Query,
            Tags = new[] { "yooasset", "collector", "rule", "list" },
            Outputs = new[] { "activeRules", "addressRules", "packRules", "filterRules", "ignoreRules" },
            ReadOnly = true,
            RequiresPackages = new[] { "com.tuyoogame.yooasset" })]
        public static object ListCollectorRules(string ruleKind = "all")
        {
#if !YOO_ASSET
            return NoYooAsset();
#else
            var kind = (ruleKind ?? "all").ToLowerInvariant();
            var active = AssetBundleCollectorSettingData.GetActiveRuleNames().Select(r => new { className = r.ClassName, displayName = r.DisplayName }).ToArray();
            var address = AssetBundleCollectorSettingData.GetAddressRuleNames().Select(r => new { className = r.ClassName, displayName = r.DisplayName }).ToArray();
            var pack = AssetBundleCollectorSettingData.GetPackRuleNames().Select(r => new { className = r.ClassName, displayName = r.DisplayName }).ToArray();
            var filter = AssetBundleCollectorSettingData.GetFilterRuleNames().Select(r => new { className = r.ClassName, displayName = r.DisplayName }).ToArray();
            var ignore = AssetBundleCollectorSettingData.GetIgnoreRuleNames().Select(r => new { className = r.ClassName, displayName = r.DisplayName }).ToArray();

            switch (kind)
            {
                case "activerule":  return new { activeRules = active };
                case "addressrule": return new { addressRules = address };
                case "packrule":    return new { packRules = pack };
                case "filterrule":  return new { filterRules = filter };
                case "ignorerule":  return new { ignoreRules = ignore };
                default:
                    return new { activeRules = active, addressRules = address, packRules = pack, filterRules = filter, ignoreRules = ignore };
            }
#endif
        }

        [UnitySkill("yooasset_create_collector_package",
            "Create a new collector package. Fails if a package with the same name already exists (unless allowDuplicate=true).",
            TracksWorkflow = true,
            Category = SkillCategory.YooAsset, Operation = SkillOperation.Create,
            Tags = new[] { "yooasset", "collector", "package", "create" },
            Outputs = new[] { "success", "packageName" },
            MutatesAssets = true, RiskLevel = "low",
            RequiresPackages = new[] { "com.tuyoogame.yooasset" })]
        public static object CreateCollectorPackage(string packageName, bool allowDuplicate = false)
        {
#if !YOO_ASSET
            return NoYooAsset();
#else
            if (string.IsNullOrEmpty(packageName))
                return new { error = "packageName is required." };

            var setting = AssetBundleCollectorSettingData.Setting;
            if (!allowDuplicate && setting.Packages.Any(p => p.PackageName == packageName))
                return new { error = $"Package '{packageName}' already exists. Pass allowDuplicate=true to override the check." };

            var pkg = AssetBundleCollectorSettingData.CreatePackage(packageName);
            AssetBundleCollectorSettingData.SaveFile();
            return new
            {
                success = true,
                packageName = pkg.PackageName,
                groupCount = pkg.Groups.Count
            };
#endif
        }

        [UnitySkill("yooasset_create_collector_group",
            "Create a new group inside an existing collector package. Validates activeRule against the registered IActiveRule list.",
            TracksWorkflow = true,
            Category = SkillCategory.YooAsset, Operation = SkillOperation.Create,
            Tags = new[] { "yooasset", "collector", "group", "create" },
            Outputs = new[] { "success", "packageName", "groupName" },
            MutatesAssets = true, RiskLevel = "low",
            RequiresPackages = new[] { "com.tuyoogame.yooasset" })]
        public static object CreateCollectorGroup(
            string packageName,
            string groupName,
            string groupDesc = "",
            string activeRule = "EnableGroup",
            string assetTags = "")
        {
#if !YOO_ASSET
            return NoYooAsset();
#else
            if (string.IsNullOrEmpty(packageName)) return new { error = "packageName is required." };
            if (string.IsNullOrEmpty(groupName))   return new { error = "groupName is required." };

            var setting = AssetBundleCollectorSettingData.Setting;
            var pkg = setting.Packages.FirstOrDefault(p => p.PackageName == packageName);
            if (pkg == null) return new { error = $"Package '{packageName}' not found. Use yooasset_create_collector_package first." };

            if (!AssetBundleCollectorSettingData.HasActiveRuleName(activeRule))
                return new { error = $"Unknown activeRule: {activeRule}. Use yooasset_list_collector_rules ruleKind=activeRule to list valid names." };

            if (pkg.Groups.Any(g => g.GroupName == groupName))
                return new { error = $"Group '{groupName}' already exists in package '{packageName}'." };

            var group = AssetBundleCollectorSettingData.CreateGroup(pkg, groupName);
            group.GroupDesc = groupDesc;
            group.ActiveRuleName = activeRule;
            group.AssetTags = assetTags;
            AssetBundleCollectorSettingData.SaveFile();

            return new
            {
                success = true,
                packageName = pkg.PackageName,
                groupName = group.GroupName,
                activeRule = group.ActiveRuleName
            };
#endif
        }

        [UnitySkill("yooasset_add_collector",
            "Add an AssetBundleCollector to an existing group. Validates collectPath via AssetDatabase and each rule via its registered name.",
            TracksWorkflow = true,
            Category = SkillCategory.YooAsset, Operation = SkillOperation.Create,
            Tags = new[] { "yooasset", "collector", "create", "add" },
            Outputs = new[] { "success", "packageName", "groupName", "collectPath" },
            MutatesAssets = true, RiskLevel = "low",
            RequiresPackages = new[] { "com.tuyoogame.yooasset" })]
        public static object AddCollector(
            string packageName,
            string groupName,
            string collectPath,
            string collectorType = "MainAssetCollector",
            string addressRule = "AddressByFileName",
            string packRule = "PackDirectory",
            string filterRule = "CollectAll",
            string assetTags = "",
            string userData = "")
        {
#if !YOO_ASSET
            return NoYooAsset();
#else
            if (string.IsNullOrEmpty(packageName)) return new { error = "packageName is required." };
            if (string.IsNullOrEmpty(groupName))   return new { error = "groupName is required." };
            if (string.IsNullOrEmpty(collectPath)) return new { error = "collectPath is required (Asset path to a folder or single asset)." };

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(collectPath) == null)
                return new { error = $"collectPath '{collectPath}' does not resolve to a valid asset. Use an Asset path such as 'Assets/Resources/Prefabs'." };

            if (!Enum.TryParse<ECollectorType>(collectorType, out var eType))
                return new { error = $"Unknown collectorType: {collectorType}. Available: MainAssetCollector, StaticAssetCollector, DependAssetCollector." };

            if (!AssetBundleCollectorSettingData.HasAddressRuleName(addressRule))
                return new { error = $"Unknown addressRule: {addressRule}." };
            if (!AssetBundleCollectorSettingData.HasPackRuleName(packRule))
                return new { error = $"Unknown packRule: {packRule}." };
            if (!AssetBundleCollectorSettingData.HasFilterRuleName(filterRule))
                return new { error = $"Unknown filterRule: {filterRule}." };

            var setting = AssetBundleCollectorSettingData.Setting;
            var pkg = setting.Packages.FirstOrDefault(p => p.PackageName == packageName);
            if (pkg == null) return new { error = $"Package '{packageName}' not found." };
            var group = pkg.Groups.FirstOrDefault(g => g.GroupName == groupName);
            if (group == null) return new { error = $"Group '{groupName}' not found in package '{packageName}'." };

            var collector = new AssetBundleCollector
            {
                CollectPath = collectPath,
                CollectorGUID = AssetDatabase.AssetPathToGUID(collectPath),
                CollectorType = eType,
                AddressRuleName = addressRule,
                PackRuleName = packRule,
                FilterRuleName = filterRule,
                AssetTags = assetTags,
                UserData = userData
            };
            AssetBundleCollectorSettingData.CreateCollector(group, collector);
            AssetBundleCollectorSettingData.SaveFile();

            return new
            {
                success = true,
                packageName = pkg.PackageName,
                groupName = group.GroupName,
                collectPath = collector.CollectPath,
                collectorType = collector.CollectorType.ToString(),
                addressRule, packRule, filterRule
            };
#endif
        }

        [UnitySkill("yooasset_save_collector_config",
            "Persist the AssetBundleCollectorSetting ScriptableObject to disk; optionally run FixFile() first to repair dangling rule names.",
            Category = SkillCategory.YooAsset, Operation = SkillOperation.Modify,
            Tags = new[] { "yooasset", "collector", "save", "persist" },
            Outputs = new[] { "saved", "fixed", "isDirty" },
            MutatesAssets = true, RiskLevel = "low",
            RequiresPackages = new[] { "com.tuyoogame.yooasset" })]
        public static object SaveCollectorConfig(bool fixErrors = true)
        {
#if !YOO_ASSET
            return NoYooAsset();
#else
            bool fixedApplied = false;
            if (fixErrors)
            {
                AssetBundleCollectorSettingData.FixFile();
                fixedApplied = true;
            }
            AssetBundleCollectorSettingData.SaveFile();
            return new
            {
                saved = true,
                fixed_ = fixedApplied,
                isDirty = AssetBundleCollectorSettingData.IsDirty
            };
#endif
        }

        // ==================================================================================
        // D. Build report analysis (4 skills)
        // ==================================================================================

        [UnitySkill("yooasset_load_build_report",
            "Load a BuildReport JSON file and return its Summary (build metadata + totals). Use yooasset_list_report_bundles or yooasset_get_bundle_detail for deeper dives.",
            Category = SkillCategory.YooAsset, Operation = SkillOperation.Analyze,
            Tags = new[] { "yooasset", "report", "build", "analyze" },
            Outputs = new[] { "summary", "bundleCount", "assetCount", "independAssetCount" },
            ReadOnly = true,
            RequiresPackages = new[] { "com.tuyoogame.yooasset" })]
        public static object LoadBuildReport(string reportPath)
        {
#if !YOO_ASSET
            return NoYooAsset();
#else
            if (string.IsNullOrEmpty(reportPath)) return new { error = "reportPath is required." };
            if (!File.Exists(reportPath))         return new { error = $"Report file not found: {reportPath}" };

            BuildReport report;
            try
            {
                report = BuildReport.Deserialize(File.ReadAllText(reportPath));
            }
            catch (Exception ex)
            {
                return new { error = $"Failed to deserialize report: {ex.Message}" };
            }

            var s = report.Summary;
            return new
            {
                reportPath,
                bundleCount = report.BundleInfos?.Count ?? 0,
                assetCount = report.AssetInfos?.Count ?? 0,
                independAssetCount = report.IndependAssets?.Count ?? 0,
                summary = new
                {
                    yooVersion = s.YooVersion,
                    unityVersion = s.UnityVersion,
                    buildDate = s.BuildDate,
                    buildSeconds = s.BuildSeconds,
                    buildTarget = s.BuildTarget.ToString(),
                    buildPipeline = s.BuildPipeline,
                    packageName = s.BuildPackageName,
                    packageVersion = s.BuildPackageVersion,
                    packageNote = s.BuildPackageNote,
                    compressOption = s.CompressOption.ToString(),
                    fileNameStyle = s.FileNameStyle.ToString(),
                    enableAddressable = s.EnableAddressable,
                    supportExtensionless = s.SupportExtensionless,
                    replaceAssetPathWithAddress = s.ReplaceAssetPathWithAddress,
                    disableWriteTypeTree = s.DisableWriteTypeTree,
                    useAssetDependencyDB = s.UseAssetDependencyDB,
                    totals = new
                    {
                        assetFiles = s.AssetFileTotalCount,
                        mainAssets = s.MainAssetTotalCount,
                        bundles = s.AllBundleTotalCount,
                        bundlesSize = s.AllBundleTotalSize,
                        encryptedBundles = s.EncryptedBundleTotalCount,
                        encryptedBundlesSize = s.EncryptedBundleTotalSize
                    }
                }
            };
#endif
        }

        [UnitySkill("yooasset_list_report_bundles",
            "List bundles from a BuildReport JSON with paging + filtering. Sort by size / name / refCount / dependCount.",
            Category = SkillCategory.YooAsset, Operation = SkillOperation.Analyze,
            Tags = new[] { "yooasset", "report", "bundle", "list", "analyze" },
            Outputs = new[] { "total", "returned", "items" },
            ReadOnly = true,
            RequiresPackages = new[] { "com.tuyoogame.yooasset" })]
        public static object ListReportBundles(
            string reportPath,
            string filterEncrypted = null,
            string filterTag = null,
            string sortBy = "size",
            int limit = 100,
            int offset = 0)
        {
#if !YOO_ASSET
            return NoYooAsset();
#else
            if (string.IsNullOrEmpty(reportPath)) return new { error = "reportPath is required." };
            if (!File.Exists(reportPath))         return new { error = $"Report file not found: {reportPath}" };

            BuildReport report;
            try { report = BuildReport.Deserialize(File.ReadAllText(reportPath)); }
            catch (Exception ex) { return new { error = $"Failed to deserialize report: {ex.Message}" }; }

            IEnumerable<ReportBundleInfo> bundles = report.BundleInfos ?? new List<ReportBundleInfo>();

            if (!string.IsNullOrEmpty(filterEncrypted) && bool.TryParse(filterEncrypted, out var wantEnc))
                bundles = bundles.Where(b => b.Encrypted == wantEnc);

            if (!string.IsNullOrEmpty(filterTag))
                bundles = bundles.Where(b => b.Tags != null && b.Tags.Contains(filterTag));

            switch ((sortBy ?? "size").ToLowerInvariant())
            {
                case "name":        bundles = bundles.OrderBy(b => b.BundleName); break;
                case "refcount":    bundles = bundles.OrderByDescending(b => b.ReferenceBundles?.Count ?? 0); break;
                case "dependcount": bundles = bundles.OrderByDescending(b => b.DependBundles?.Count ?? 0); break;
                default:            bundles = bundles.OrderByDescending(b => b.FileSize); break;
            }

            var materialized = bundles.ToArray();
            var page = materialized.Skip(Math.Max(0, offset)).Take(Math.Max(1, limit)).ToArray();

            return new
            {
                reportPath,
                total = materialized.Length,
                returned = page.Length,
                offset,
                limit,
                sortBy,
                items = page.Select(b => new
                {
                    bundleName = b.BundleName,
                    fileName = b.FileName,
                    fileSize = b.FileSize,
                    fileHash = b.FileHash,
                    fileCRC = b.FileCRC,
                    encrypted = b.Encrypted,
                    tags = b.Tags,
                    dependBundleCount = b.DependBundles?.Count ?? 0,
                    referenceBundleCount = b.ReferenceBundles?.Count ?? 0,
                    bundleContentCount = b.BundleContents?.Count ?? 0
                }).ToArray()
            };
#endif
        }

        [UnitySkill("yooasset_get_bundle_detail",
            "Return full ReportBundleInfo for a single bundle — dependBundles, referenceBundles, and the per-asset BundleContents list.",
            Category = SkillCategory.YooAsset, Operation = SkillOperation.Analyze,
            Tags = new[] { "yooasset", "report", "bundle", "detail", "dependency" },
            Outputs = new[] { "bundleName", "fileSize", "dependBundles", "referenceBundles", "bundleContents" },
            ReadOnly = true,
            RequiresPackages = new[] { "com.tuyoogame.yooasset" })]
        public static object GetBundleDetail(string reportPath, string bundleName)
        {
#if !YOO_ASSET
            return NoYooAsset();
#else
            if (string.IsNullOrEmpty(reportPath)) return new { error = "reportPath is required." };
            if (string.IsNullOrEmpty(bundleName)) return new { error = "bundleName is required." };
            if (!File.Exists(reportPath))         return new { error = $"Report file not found: {reportPath}" };

            BuildReport report;
            try { report = BuildReport.Deserialize(File.ReadAllText(reportPath)); }
            catch (Exception ex) { return new { error = $"Failed to deserialize report: {ex.Message}" }; }

            ReportBundleInfo info;
            try { info = report.GetBundleInfo(bundleName); }
            catch (Exception ex) { return new { error = ex.Message }; }

            return new
            {
                bundleName = info.BundleName,
                fileName = info.FileName,
                fileSize = info.FileSize,
                fileHash = info.FileHash,
                fileCRC = info.FileCRC,
                encrypted = info.Encrypted,
                tags = info.Tags,
                dependBundles = info.DependBundles,
                referenceBundles = info.ReferenceBundles,
                bundleContents = info.BundleContents?.Select(a => new
                {
                    assetPath = a.AssetPath,
                    assetGUID = a.AssetGUID,
                    fileExtension = a.FileExtension
                }).ToArray()
            };
#endif
        }

        [UnitySkill("yooasset_list_independ_assets",
            "List IndependAssets from a BuildReport — assets not referenced by any other asset, candidates for cleanup.",
            Category = SkillCategory.YooAsset, Operation = SkillOperation.Analyze,
            Tags = new[] { "yooasset", "report", "independent", "orphan", "cleanup" },
            Outputs = new[] { "total", "returned", "items" },
            ReadOnly = true,
            RequiresPackages = new[] { "com.tuyoogame.yooasset" })]
        public static object ListIndependAssets(string reportPath, int limit = 100, int offset = 0)
        {
#if !YOO_ASSET
            return NoYooAsset();
#else
            if (string.IsNullOrEmpty(reportPath)) return new { error = "reportPath is required." };
            if (!File.Exists(reportPath))         return new { error = $"Report file not found: {reportPath}" };

            BuildReport report;
            try { report = BuildReport.Deserialize(File.ReadAllText(reportPath)); }
            catch (Exception ex) { return new { error = $"Failed to deserialize report: {ex.Message}" }; }

            var all = report.IndependAssets ?? new List<ReportIndependAsset>();
            var page = all.Skip(Math.Max(0, offset)).Take(Math.Max(1, limit)).ToArray();

            return new
            {
                reportPath,
                total = all.Count,
                returned = page.Length,
                offset,
                limit,
                items = page.Select(a => new
                {
                    assetPath = a.AssetPath,
                    assetGUID = a.AssetGUID,
                    assetType = a.AssetType,
                    fileSize = a.FileSize
                }).ToArray()
            };
#endif
        }
    }
}

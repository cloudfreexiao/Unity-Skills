using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnitySkills
{
    /// <summary>
    /// Shader Graph asset creation, inspection, and constrained editing skills.
    /// </summary>
    public static class ShaderGraphSkills
    {
        [UnitySkill("shadergraph_list_templates", "List Shader Graph templates shipped by the installed Shader Graph package",
            Category = SkillCategory.ShaderGraph, Operation = SkillOperation.Query,
            Tags = new[] { "shadergraph", "template", "list", "graph", "subgraph" },
            Outputs = new[] { "count", "templates" },
            ReadOnly = true,
            RequiresPackages = new[] { "com.unity.shadergraph" })]
        public static object ShaderGraphListTemplates(bool includeSubGraphs = false, string filter = null)
        {
            if (!ShaderGraphReflectionHelper.HasPackageFolder)
                return ShaderGraphReflectionHelper.NoShaderGraph();

            var templates = ShaderGraphReflectionHelper.GetTemplateDescriptors(includeSubGraphs)
                .Select(Newtonsoft.Json.Linq.JObject.FromObject)
                .Where(item =>
                    string.IsNullOrWhiteSpace(filter) ||
                    item["name"]?.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item["group"]?.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();

            return new
            {
                success = true,
                count = templates.Length,
                templates
            };
        }

        [UnitySkill("shadergraph_create_graph", "Create a Shader Graph asset from a package template",
            Category = SkillCategory.ShaderGraph, Operation = SkillOperation.Create,
            Tags = new[] { "shadergraph", "create", "graph", "template", "asset" },
            Outputs = new[] { "path", "templatePath", "graph" },
            TracksWorkflow = true,
            MutatesAssets = true,
            RequiresPackages = new[] { "com.unity.shadergraph" })]
        public static object ShaderGraphCreateGraph(string savePath, string templateName = "Unlit Simple", string templatePath = null)
        {
            if (!ShaderGraphReflectionHelper.HasPackageFolder)
                return ShaderGraphReflectionHelper.NoShaderGraph();
            if (Validate.Required(savePath, "savePath") is object err) return err;

            var fileName = Path.GetFileNameWithoutExtension(savePath);
            var resolvedPath = RenderPipelineSkillsCommon.ResolveAssetSavePath(savePath, string.IsNullOrWhiteSpace(fileName) ? "New Shader Graph" : fileName, ".shadergraph");
            if (Validate.SafePath(resolvedPath, "savePath") is object pathErr) return pathErr;
            if (File.Exists(resolvedPath))
                return new { error = $"Asset already exists: {resolvedPath}" };

            var useBlankGraphFallback = false;
            string resolvedTemplatePath = null;
            string warning = null;

            if (!string.IsNullOrWhiteSpace(templatePath))
            {
                resolvedTemplatePath = ShaderGraphReflectionHelper.ResolveTemplatePath(templatePath, false, out var directTemplateError);
                if (!string.IsNullOrWhiteSpace(directTemplateError))
                    return new { error = directTemplateError };
                useBlankGraphFallback = string.Equals(resolvedTemplatePath, "builtin:blank", StringComparison.OrdinalIgnoreCase);
            }
            else if (ShaderGraphReflectionHelper.HasTemplateDirectory)
            {
                resolvedTemplatePath = ShaderGraphReflectionHelper.ResolveTemplatePath(templateName, false, out var namedTemplateError);
                if (!string.IsNullOrWhiteSpace(namedTemplateError))
                    return new { error = namedTemplateError };
                useBlankGraphFallback = string.Equals(resolvedTemplatePath, "builtin:blank", StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                useBlankGraphFallback = true;
                resolvedTemplatePath = "builtin:blank";
                if (!string.IsNullOrWhiteSpace(templateName) &&
                    !string.Equals(templateName, "Blank Shader Graph", StringComparison.OrdinalIgnoreCase))
                {
                    warning = $"Installed Shader Graph package does not expose GraphTemplates. Created a blank graph instead of '{templateName}'.";
                }
            }

            RenderPipelineSkillsCommon.EnsureAssetFolderExists(resolvedPath);
            if (useBlankGraphFallback)
            {
                if (!ShaderGraphReflectionHelper.TryCreateBlankGraph(resolvedPath, "Shader Graphs", out var createError))
                    return new { error = createError };
            }
            else
            {
                if (!ShaderGraphReflectionHelper.TryCopyTemplate(resolvedTemplatePath, resolvedPath, out var copyError))
                    return new { error = copyError };
            }

            var asset = AssetDatabase.LoadMainAssetAtPath(resolvedPath);
            if (asset != null)
                WorkflowManager.SnapshotCreatedAsset(asset);

            if (!ShaderGraphReflectionHelper.TryReadGraphDocument(resolvedPath, out var document, out var readError))
                return new { success = true, path = resolvedPath, templatePath = resolvedTemplatePath, creationMode = useBlankGraphFallback ? "BlankFallback" : "TemplateCopy", warning = warning ?? readError };

            return new
            {
                success = true,
                path = resolvedPath,
                templatePath = resolvedTemplatePath,
                creationMode = useBlankGraphFallback ? "BlankFallback" : "TemplateCopy",
                warning,
                graph = ShaderGraphReflectionHelper.DescribeGraphInfo(document)
            };
        }

        [UnitySkill("shadergraph_create_subgraph", "Create a blank Shader Sub Graph asset with a configured output slot",
            Category = SkillCategory.ShaderGraph, Operation = SkillOperation.Create,
            Tags = new[] { "shadergraph", "create", "subgraph", "asset" },
            Outputs = new[] { "path", "graph" },
            TracksWorkflow = true,
            MutatesAssets = true,
            RequiresPackages = new[] { "com.unity.shadergraph" })]
        public static object ShaderGraphCreateSubGraph(string savePath, string outputType = "Vector4", string graphPath = "Sub Graphs")
        {
            if (!ShaderGraphReflectionHelper.IsShaderGraphInstalled)
                return ShaderGraphReflectionHelper.NoShaderGraph();
            if (Validate.Required(savePath, "savePath") is object err) return err;

            var fileName = Path.GetFileNameWithoutExtension(savePath);
            var resolvedPath = RenderPipelineSkillsCommon.ResolveAssetSavePath(savePath, string.IsNullOrWhiteSpace(fileName) ? "New Shader Sub Graph" : fileName, ".shadersubgraph");
            if (Validate.SafePath(resolvedPath, "savePath") is object pathErr) return pathErr;
            if (File.Exists(resolvedPath))
                return new { error = $"Asset already exists: {resolvedPath}" };

            RenderPipelineSkillsCommon.EnsureAssetFolderExists(resolvedPath);
            if (!ShaderGraphReflectionHelper.TryCreateBlankSubGraph(resolvedPath, outputType, graphPath, out var createError))
                return new { error = createError };

            var asset = AssetDatabase.LoadMainAssetAtPath(resolvedPath);
            if (asset != null)
                WorkflowManager.SnapshotCreatedAsset(asset);

            if (!ShaderGraphReflectionHelper.TryReadGraphDocument(resolvedPath, out var document, out var readError))
                return new { success = true, path = resolvedPath, warning = readError };

            return new
            {
                success = true,
                path = resolvedPath,
                graph = ShaderGraphReflectionHelper.DescribeGraphInfo(document)
            };
        }

        [UnitySkill("shadergraph_list_assets", "List Shader Graph and Sub Graph assets in the project",
            Category = SkillCategory.ShaderGraph, Operation = SkillOperation.Query,
            Tags = new[] { "shadergraph", "list", "assets", "graph", "subgraph" },
            Outputs = new[] { "count", "assets" },
            ReadOnly = true)]
        public static object ShaderGraphListAssets(string filter = null, bool includeSubGraphs = true, int limit = 100)
        {
            var assets = AssetDatabase.GetAllAssetPaths()
                .Where(path =>
                    path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                    (path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase) ||
                     (includeSubGraphs && path.EndsWith(".shadersubgraph", StringComparison.OrdinalIgnoreCase))))
                .Where(path => string.IsNullOrWhiteSpace(filter) || path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, limit))
                .Select(path => new
                {
                    path,
                    name = Path.GetFileNameWithoutExtension(path),
                    kind = path.EndsWith(".shadersubgraph", StringComparison.OrdinalIgnoreCase) ? "SubGraph" : "Graph"
                })
                .ToArray();

            return new
            {
                success = true,
                count = assets.Length,
                assets
            };
        }

        [UnitySkill("shadergraph_get_info", "Get a high-level summary of a Shader Graph or Sub Graph asset",
            Category = SkillCategory.ShaderGraph, Operation = SkillOperation.Query,
            Tags = new[] { "shadergraph", "inspect", "info", "graph", "subgraph" },
            Outputs = new[] { "assetPath", "kind", "propertyCount", "keywordCount", "nodeCount", "edgeCount" },
            ReadOnly = true,
            RequiresInput = new[] { "assetPath" })]
        public static object ShaderGraphGetInfo(string assetPath)
        {
            if (Validate.Required(assetPath, "assetPath") is object err) return err;
            if (Validate.SafePath(assetPath, "assetPath") is object pathErr) return pathErr;

            if (!ShaderGraphReflectionHelper.TryReadGraphDocument(assetPath, out var document, out var error))
                return new { error };

            return ShaderGraphReflectionHelper.DescribeGraphInfo(document);
        }

        [UnitySkill("shadergraph_get_structure", "Inspect nodes, edges, properties, and keywords inside a Shader Graph asset",
            Category = SkillCategory.ShaderGraph, Operation = SkillOperation.Query,
            Tags = new[] { "shadergraph", "structure", "nodes", "edges", "inspect" },
            Outputs = new[] { "nodes", "edges", "properties", "keywords" },
            ReadOnly = true,
            RequiresInput = new[] { "assetPath" })]
        public static object ShaderGraphGetStructure(string assetPath, int maxNodes = 200, int maxEdges = 200)
        {
            if (Validate.Required(assetPath, "assetPath") is object err) return err;
            if (Validate.SafePath(assetPath, "assetPath") is object pathErr) return pathErr;

            if (!ShaderGraphReflectionHelper.TryReadGraphDocument(assetPath, out var document, out var error))
                return new { error };

            return ShaderGraphReflectionHelper.DescribeGraphStructure(document, maxNodes, maxEdges);
        }

        [UnitySkill("shadergraph_list_properties", "List exposed graph properties defined in a Shader Graph asset",
            Category = SkillCategory.ShaderGraph, Operation = SkillOperation.Query,
            Tags = new[] { "shadergraph", "property", "list", "inspect" },
            Outputs = new[] { "count", "properties" },
            ReadOnly = true,
            RequiresInput = new[] { "assetPath" })]
        public static object ShaderGraphListProperties(string assetPath)
        {
            if (Validate.Required(assetPath, "assetPath") is object err) return err;
            if (Validate.SafePath(assetPath, "assetPath") is object pathErr) return pathErr;

            if (!ShaderGraphReflectionHelper.TryReadGraphDocument(assetPath, out var document, out var error))
                return new { error };

            var properties = ShaderGraphReflectionHelper.GetProperties(document);
            return new
            {
                success = true,
                assetPath,
                count = properties.Length,
                properties
            };
        }

        [UnitySkill("shadergraph_add_property", "Add a constrained Shader Graph blackboard property to an existing graph",
            Category = SkillCategory.ShaderGraph, Operation = SkillOperation.Create,
            Tags = new[] { "shadergraph", "property", "add", "blackboard" },
            Outputs = new[] { "assetPath", "property" },
            TracksWorkflow = true,
            MutatesAssets = true,
            RequiresInput = new[] { "assetPath" },
            RequiresPackages = new[] { "com.unity.shadergraph" })]
        public static object ShaderGraphAddProperty(
            string assetPath,
            string propertyType,
            string displayName,
            string referenceName = null,
            object value = null,
            bool exposed = true,
            bool hidden = false)
        {
            if (Validate.Required(assetPath, "assetPath") is object assetErr) return assetErr;
            if (Validate.Required(propertyType, "propertyType") is object typeErr) return typeErr;
            if (Validate.Required(displayName, "displayName") is object nameErr) return nameErr;
            if (Validate.SafePath(assetPath, "assetPath") is object pathErr) return pathErr;

            SnapshotGraphAsset(assetPath);
            if (!ShaderGraphReflectionHelper.TryAddProperty(assetPath, propertyType, displayName, referenceName, value, exposed, hidden, out var propertyInfo, out var error))
                return new { error };

            return new
            {
                success = true,
                assetPath,
                property = propertyInfo
            };
        }

        [UnitySkill("shadergraph_update_property", "Update a constrained Shader Graph blackboard property",
            Category = SkillCategory.ShaderGraph, Operation = SkillOperation.Modify,
            Tags = new[] { "shadergraph", "property", "update", "blackboard" },
            Outputs = new[] { "assetPath", "property" },
            TracksWorkflow = true,
            MutatesAssets = true,
            RequiresInput = new[] { "assetPath" },
            RequiresPackages = new[] { "com.unity.shadergraph" })]
        public static object ShaderGraphUpdateProperty(
            string assetPath,
            string propertyName = null,
            string referenceName = null,
            string newDisplayName = null,
            string newReferenceName = null,
            object value = null,
            bool? exposed = null,
            bool? hidden = null)
        {
            if (Validate.Required(assetPath, "assetPath") is object assetErr) return assetErr;
            if (Validate.SafePath(assetPath, "assetPath") is object pathErr) return pathErr;
            if (string.IsNullOrWhiteSpace(propertyName) && string.IsNullOrWhiteSpace(referenceName))
                return new { error = "propertyName or referenceName is required" };

            SnapshotGraphAsset(assetPath);
            if (!ShaderGraphReflectionHelper.TryUpdateProperty(assetPath, propertyName, referenceName, newDisplayName, newReferenceName, value, exposed, hidden, out var propertyInfo, out var error))
                return new { error };

            return new
            {
                success = true,
                assetPath,
                property = propertyInfo
            };
        }

        [UnitySkill("shadergraph_remove_property", "Remove a Shader Graph blackboard property from an existing graph",
            Category = SkillCategory.ShaderGraph, Operation = SkillOperation.Delete,
            Tags = new[] { "shadergraph", "property", "remove", "blackboard" },
            Outputs = new[] { "assetPath", "propertyName", "referenceName" },
            TracksWorkflow = true,
            MutatesAssets = true,
            RequiresInput = new[] { "assetPath" },
            RequiresPackages = new[] { "com.unity.shadergraph" })]
        public static object ShaderGraphRemoveProperty(string assetPath, string propertyName = null, string referenceName = null)
        {
            if (Validate.Required(assetPath, "assetPath") is object assetErr) return assetErr;
            if (Validate.SafePath(assetPath, "assetPath") is object pathErr) return pathErr;
            if (string.IsNullOrWhiteSpace(propertyName) && string.IsNullOrWhiteSpace(referenceName))
                return new { error = "propertyName or referenceName is required" };

            SnapshotGraphAsset(assetPath);
            if (!ShaderGraphReflectionHelper.TryRemoveProperty(assetPath, propertyName, referenceName, out var error))
                return new { error };

            return new
            {
                success = true,
                assetPath,
                propertyName,
                referenceName
            };
        }

        [UnitySkill("shadergraph_list_keywords", "List Shader Graph keywords defined in a graph asset",
            Category = SkillCategory.ShaderGraph, Operation = SkillOperation.Query,
            Tags = new[] { "shadergraph", "keyword", "list", "inspect" },
            Outputs = new[] { "count", "keywords" },
            ReadOnly = true,
            RequiresInput = new[] { "assetPath" })]
        public static object ShaderGraphListKeywords(string assetPath)
        {
            if (Validate.Required(assetPath, "assetPath") is object err) return err;
            if (Validate.SafePath(assetPath, "assetPath") is object pathErr) return pathErr;

            if (!ShaderGraphReflectionHelper.TryReadGraphDocument(assetPath, out var document, out var error))
                return new { error };

            var keywords = ShaderGraphReflectionHelper.GetKeywords(document);
            return new
            {
                success = true,
                assetPath,
                count = keywords.Length,
                keywords
            };
        }

        [UnitySkill("shadergraph_add_keyword", "Add a Shader Graph keyword to an existing graph",
            Category = SkillCategory.ShaderGraph, Operation = SkillOperation.Create,
            Tags = new[] { "shadergraph", "keyword", "add", "blackboard" },
            Outputs = new[] { "assetPath", "keyword" },
            TracksWorkflow = true,
            MutatesAssets = true,
            RequiresInput = new[] { "assetPath" },
            RequiresPackages = new[] { "com.unity.shadergraph" })]
        public static object ShaderGraphAddKeyword(
            string assetPath,
            string keywordType = "Boolean",
            string displayName = null,
            string referenceName = null,
            string definition = "ShaderFeature",
            string scope = "Local",
            string entries = null,
            int value = 0)
        {
            if (Validate.Required(assetPath, "assetPath") is object assetErr) return assetErr;
            if (Validate.SafePath(assetPath, "assetPath") is object pathErr) return pathErr;

            SnapshotGraphAsset(assetPath);
            if (!ShaderGraphReflectionHelper.TryAddKeyword(assetPath, keywordType, displayName, referenceName, definition, scope, entries, value, out var keywordInfo, out var error))
                return new { error };

            return new
            {
                success = true,
                assetPath,
                keyword = keywordInfo
            };
        }

        [UnitySkill("shadergraph_update_keyword", "Update a Shader Graph keyword on an existing graph",
            Category = SkillCategory.ShaderGraph, Operation = SkillOperation.Modify,
            Tags = new[] { "shadergraph", "keyword", "update", "blackboard" },
            Outputs = new[] { "assetPath", "keyword" },
            TracksWorkflow = true,
            MutatesAssets = true,
            RequiresInput = new[] { "assetPath" },
            RequiresPackages = new[] { "com.unity.shadergraph" })]
        public static object ShaderGraphUpdateKeyword(
            string assetPath,
            string displayName = null,
            string referenceName = null,
            string newDisplayName = null,
            string newReferenceName = null,
            string definition = null,
            string scope = null,
            string entries = null,
            int? value = null)
        {
            if (Validate.Required(assetPath, "assetPath") is object assetErr) return assetErr;
            if (Validate.SafePath(assetPath, "assetPath") is object pathErr) return pathErr;
            if (string.IsNullOrWhiteSpace(displayName) && string.IsNullOrWhiteSpace(referenceName))
                return new { error = "displayName or referenceName is required" };

            SnapshotGraphAsset(assetPath);
            if (!ShaderGraphReflectionHelper.TryUpdateKeyword(assetPath, displayName, referenceName, newDisplayName, newReferenceName, definition, scope, entries, value, out var keywordInfo, out var error))
                return new { error };

            return new
            {
                success = true,
                assetPath,
                keyword = keywordInfo
            };
        }

        [UnitySkill("shadergraph_remove_keyword", "Remove a Shader Graph keyword from an existing graph",
            Category = SkillCategory.ShaderGraph, Operation = SkillOperation.Delete,
            Tags = new[] { "shadergraph", "keyword", "remove", "blackboard" },
            Outputs = new[] { "assetPath", "displayName", "referenceName" },
            TracksWorkflow = true,
            MutatesAssets = true,
            RequiresInput = new[] { "assetPath" },
            RequiresPackages = new[] { "com.unity.shadergraph" })]
        public static object ShaderGraphRemoveKeyword(string assetPath, string displayName = null, string referenceName = null)
        {
            if (Validate.Required(assetPath, "assetPath") is object assetErr) return assetErr;
            if (Validate.SafePath(assetPath, "assetPath") is object pathErr) return pathErr;
            if (string.IsNullOrWhiteSpace(displayName) && string.IsNullOrWhiteSpace(referenceName))
                return new { error = "displayName or referenceName is required" };

            SnapshotGraphAsset(assetPath);
            if (!ShaderGraphReflectionHelper.TryRemoveKeyword(assetPath, displayName, referenceName, out var error))
                return new { error };

            return new
            {
                success = true,
                assetPath,
                displayName,
                referenceName
            };
        }

        [UnitySkill("shadergraph_reimport", "Force reimport of a Shader Graph or Sub Graph asset after external edits",
            Category = SkillCategory.ShaderGraph, Operation = SkillOperation.Execute,
            Tags = new[] { "shadergraph", "reimport", "refresh", "asset" },
            Outputs = new[] { "assetPath" },
            TracksWorkflow = true,
            MutatesAssets = true,
            RequiresInput = new[] { "assetPath" })]
        public static object ShaderGraphReimport(string assetPath)
        {
            if (Validate.Required(assetPath, "assetPath") is object err) return err;
            if (Validate.SafePath(assetPath, "assetPath") is object pathErr) return pathErr;
            if (!File.Exists(Path.GetFullPath(assetPath)))
                return new { error = $"Asset not found: {assetPath}" };

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            return new
            {
                success = true,
                assetPath
            };
        }

        private static void SnapshotGraphAsset(string assetPath)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset != null)
                WorkflowManager.SnapshotObject(asset);
        }
    }
}

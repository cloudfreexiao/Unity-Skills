using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using PkgInfo = UnityEditor.PackageManager.PackageInfo;

namespace UnitySkills
{
    internal sealed class ShaderGraphDocument
    {
        public string AssetPath { get; set; }
        public JObject Root { get; set; }
        public Dictionary<string, JObject> ObjectsById { get; } = new Dictionary<string, JObject>(StringComparer.Ordinal);
        public List<JObject> OrderedObjects { get; } = new List<JObject>();
    }

    internal static class ShaderGraphReflectionHelper
    {
        private const BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const string PackageRoot = "Packages/com.unity.shadergraph";
        private const string TemplatesRoot = "Packages/com.unity.shadergraph/GraphTemplates";
        private const string BuiltinBlankTemplatePath = "builtin:blank";

        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);

        private static readonly Dictionary<string, string> PropertyTypeMap =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["float"] = "UnityEditor.ShaderGraph.Internal.Vector1ShaderProperty",
                ["vector1"] = "UnityEditor.ShaderGraph.Internal.Vector1ShaderProperty",
                ["vector2"] = "UnityEditor.ShaderGraph.Internal.Vector2ShaderProperty",
                ["vector3"] = "UnityEditor.ShaderGraph.Internal.Vector3ShaderProperty",
                ["vector4"] = "UnityEditor.ShaderGraph.Internal.Vector4ShaderProperty",
                ["color"] = "UnityEditor.ShaderGraph.Internal.ColorShaderProperty",
                ["boolean"] = "UnityEditor.ShaderGraph.Internal.BooleanShaderProperty",
                ["bool"] = "UnityEditor.ShaderGraph.Internal.BooleanShaderProperty",
                ["texture2d"] = "UnityEditor.ShaderGraph.Internal.Texture2DShaderProperty"
            };

        private static readonly Dictionary<int, string> KeywordTypeNames =
            new Dictionary<int, string>
            {
                [0] = "Boolean",
                [1] = "Enum"
            };

        private static readonly Dictionary<int, string> KeywordDefinitionNames =
            new Dictionary<int, string>
            {
                [0] = "ShaderFeature",
                [1] = "MultiCompile",
                [2] = "Predefined",
                [3] = "DynamicBranch"
            };

        private static readonly Dictionary<int, string> KeywordScopeNames =
            new Dictionary<int, string>
            {
                [0] = "Local",
                [1] = "Global"
            };

        public static object NoShaderGraph()
        {
            return new
            {
                error = "Shader Graph package (com.unity.shadergraph) is not installed. Install URP/HDRP with Shader Graph support via Package Manager."
            };
        }

        public static bool IsShaderGraphInstalled
        {
            get { return FindTypeInAssemblies("UnityEditor.ShaderGraph.GraphData") != null; }
        }

        public static bool HasPackageFolder
        {
            get
            {
                return TryGetPackageRoot(out _, out _);
            }
        }

        public static bool HasTemplateDirectory
        {
            get
            {
                return TryGetTemplatesDirectory(out _);
            }
        }

        public static Type FindTypeInAssemblies(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return null;

            if (TypeCache.TryGetValue(fullName, out var cached))
                return cached;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = assembly.GetType(fullName, false);
                    if (type != null)
                    {
                        TypeCache[fullName] = type;
                        return type;
                    }
                }
                catch
                {
                    // Ignore broken reflection-only assemblies.
                }
            }

            TypeCache[fullName] = null;
            return null;
        }

        public static IEnumerable<object> GetTemplateDescriptors(bool includeSubGraphs)
        {
            if (!TryGetPackageRoot(out var packageRoot, out _))
                return Enumerable.Empty<object>();

            if (!TryGetTemplatesDirectory(out var templatesDirectory))
            {
                return new[]
                {
                    new
                    {
                        name = "Blank Shader Graph",
                        path = BuiltinBlankTemplatePath,
                        group = "Builtin",
                        kind = "Graph",
                        source = "BuiltinFallback"
                    }
                };
            }

            return Directory.EnumerateFiles(templatesDirectory, "*.*", SearchOption.AllDirectories)
                .Where(fullPath =>
                    fullPath.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase) ||
                    (includeSubGraphs && fullPath.EndsWith(".shadersubgraph", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(fullPath => fullPath, StringComparer.OrdinalIgnoreCase)
                .Select(fullPath =>
                {
                    var relative = Path.GetRelativePath(packageRoot, fullPath).Replace('\\', '/');
                    var logicalPath = $"{PackageRoot}/{relative}";
                    var directory = Path.GetDirectoryName(relative)?.Replace('\\', '/');
                    var extension = Path.GetExtension(fullPath);
                    return new
                    {
                        name = Path.GetFileNameWithoutExtension(fullPath),
                        path = logicalPath,
                        group = string.IsNullOrWhiteSpace(directory) ? null : directory,
                        kind = string.Equals(extension, ".shadersubgraph", StringComparison.OrdinalIgnoreCase) ? "SubGraph" : "Graph",
                        source = "PackageTemplate"
                    };
                })
                .ToArray();
        }

        public static string ResolveTemplatePath(string templateNameOrPath, bool allowSubGraphs, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(templateNameOrPath))
                return null;

            if (string.Equals(templateNameOrPath, BuiltinBlankTemplatePath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(templateNameOrPath, "Blank Shader Graph", StringComparison.OrdinalIgnoreCase))
                return BuiltinBlankTemplatePath;

            if (templateNameOrPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                templateNameOrPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryResolveTemplateFilePath(templateNameOrPath, out var resolvedTemplateFilePath))
                {
                    error = $"Template not found: {templateNameOrPath}";
                    return null;
                }

                var isAllowed = templateNameOrPath.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase) ||
                                (allowSubGraphs && templateNameOrPath.EndsWith(".shadersubgraph", StringComparison.OrdinalIgnoreCase));
                if (!isAllowed)
                {
                    error = $"Unsupported template extension: {templateNameOrPath}";
                    return null;
                }

                return resolvedTemplateFilePath;
            }

            var templates = GetTemplateDescriptors(allowSubGraphs)
                .Cast<object>()
                .Select(x => JObject.FromObject(x))
                .Where(x => string.Equals(x["name"]?.ToString(), templateNameOrPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (templates.Length == 0)
            {
                error = $"Template '{templateNameOrPath}' not found";
                return null;
            }

            if (templates.Length > 1)
            {
                error = $"Template '{templateNameOrPath}' is ambiguous. Use templatePath instead.";
                return null;
            }

            var logicalPath = templates[0]["path"]?.ToString();
            if (!TryResolveTemplateFilePath(logicalPath, out var resolvedPath))
            {
                error = $"Template not found: {logicalPath}";
                return null;
            }

            return resolvedPath;
        }

        public static bool TryCreateBlankGraph(string assetPath, string graphPath, out string error)
        {
            error = null;
            if (!IsShaderGraphInstalled)
            {
                error = "Shader Graph package is not installed";
                return false;
            }

            try
            {
                var graphType = FindTypeInAssemblies("UnityEditor.ShaderGraph.GraphData");
                var categoryDataType = FindTypeInAssemblies("UnityEditor.ShaderGraph.CategoryData");
                if (graphType == null || categoryDataType == null)
                {
                    error = "Required Shader Graph graph creation types were not found";
                    return false;
                }

                var graph = Activator.CreateInstance(graphType, true);
                InvokeMethod(graph, "AddContexts");
                InvokeMethod(graph, "InitializeOutputs", null, null);

                var defaultCategoryMethod = categoryDataType.GetMethod("DefaultCategory", StaticFlags);
                if (defaultCategoryMethod == null)
                {
                    error = "CategoryData.DefaultCategory was not found";
                    return false;
                }

                var category = defaultCategoryMethod.Invoke(null, new object[] { null });
                InvokeMethod(graph, "AddCategory", category);
                SetMemberValue(graph, "path", string.IsNullOrWhiteSpace(graphPath) ? "Shader Graphs" : graphPath);

                return TrySaveGraphData(assetPath, graph, out error);
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        public static bool TryCopyTemplate(string templatePath, string destinationPath, out string error)
        {
            error = null;

            try
            {
                var sourceFullPath = Path.GetFullPath(templatePath);
                if (!File.Exists(sourceFullPath))
                {
                    error = $"Template file missing at path: {templatePath}";
                    return false;
                }

                var text = File.ReadAllText(sourceFullPath, Encoding.UTF8);
                var destinationFullPath = Path.GetFullPath(destinationPath);
                var destinationDirectory = Path.GetDirectoryName(destinationFullPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory) && !Directory.Exists(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);

                File.WriteAllText(destinationFullPath, text, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(destinationPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.SaveAssets();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryCreateBlankSubGraph(string assetPath, string outputTypeName, string graphPath, out string error)
        {
            error = null;
            if (!IsShaderGraphInstalled)
            {
                error = "Shader Graph package is not installed";
                return false;
            }

            try
            {
                var graphType = FindTypeInAssemblies("UnityEditor.ShaderGraph.GraphData");
                var subGraphOutputNodeType = FindTypeInAssemblies("UnityEditor.ShaderGraph.SubGraphOutputNode");
                var concreteSlotValueType = FindTypeInAssemblies("UnityEditor.ShaderGraph.ConcreteSlotValueType");
                if (graphType == null || subGraphOutputNodeType == null || concreteSlotValueType == null)
                {
                    error = "Required Shader Graph editor types were not found";
                    return false;
                }

                var graph = Activator.CreateInstance(graphType, true);
                SetMemberValue(graph, "isSubGraph", true);
                SetMemberValue(graph, "path", string.IsNullOrWhiteSpace(graphPath) ? "Sub Graphs" : graphPath);

                var outputNode = Activator.CreateInstance(subGraphOutputNodeType, true);
                InvokeMethod(graph, "AddNode", outputNode);
                SetMemberValue(graph, "outputNode", outputNode);

                if (!EnumTryParse(concreteSlotValueType, outputTypeName, out var slotTypeValue))
                {
                    error = $"Invalid outputType '{outputTypeName}'. Valid values: {string.Join(", ", Enum.GetNames(concreteSlotValueType))}";
                    return false;
                }

                InvokeMethod(outputNode, "AddSlot", slotTypeValue);
                return TrySaveGraphData(assetPath, graph, out error);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool TryLoadGraphData(string assetPath, out object graph, out string error)
        {
            graph = null;
            error = null;

            if (!IsShaderGraphInstalled)
            {
                error = "Shader Graph package is not installed";
                return false;
            }

            try
            {
                var fullPath = Path.GetFullPath(assetPath);
                if (!File.Exists(fullPath))
                {
                    error = $"Shader Graph asset not found: {assetPath}";
                    return false;
                }

                var graphType = FindTypeInAssemblies("UnityEditor.ShaderGraph.GraphData");
                var messageManagerType = FindTypeInAssemblies("UnityEditor.Graphing.Util.MessageManager");
                var multiJsonType = FindTypeInAssemblies("UnityEditor.ShaderGraph.Serialization.MultiJson");
                if (graphType == null || multiJsonType == null)
                {
                    error = "Required Shader Graph serialization types were not found";
                    return false;
                }

                graph = Activator.CreateInstance(graphType, true);
                if (messageManagerType != null)
                    SetMemberValue(graph, "messageManager", Activator.CreateInstance(messageManagerType, true));
                SetMemberValue(graph, "assetGuid", AssetDatabase.AssetPathToGUID(assetPath));

                var deserializeMethod = multiJsonType.GetMethods(StaticFlags)
                    .FirstOrDefault(method => string.Equals(method.Name, "Deserialize", StringComparison.Ordinal));
                if (deserializeMethod == null)
                {
                    error = "MultiJson.Deserialize was not found";
                    return false;
                }

                var text = File.ReadAllText(fullPath, Encoding.UTF8);
                deserializeMethod.MakeGenericMethod(graphType).Invoke(null, new object[] { graph, text, null, false });
                InvokeMethod(graph, "OnEnable");
                InvokeMethod(graph, "ValidateGraph");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        public static bool TrySaveGraphData(string assetPath, object graph, out string error)
        {
            error = null;

            try
            {
                InvokeMethod(graph, "ValidateGraph");

                var fileUtilitiesType = FindTypeInAssemblies("UnityEditor.ShaderGraph.FileUtilities");
                if (fileUtilitiesType == null)
                {
                    error = "Shader Graph FileUtilities type was not found";
                    return false;
                }

                var writeMethod = fileUtilitiesType.GetMethod("WriteShaderGraphToDisk", StaticFlags);
                if (writeMethod == null)
                {
                    error = "WriteShaderGraphToDisk was not found";
                    return false;
                }

                var result = writeMethod.Invoke(null, new[] { assetPath, graph });
                if (result == null)
                {
                    error = $"Failed to save Shader Graph asset: {assetPath}";
                    return false;
                }

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.SaveAssets();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        public static bool TryAddProperty(
            string assetPath,
            string propertyType,
            string displayName,
            string referenceName,
            object value,
            bool exposed,
            bool hidden,
            out object propertyInfo,
            out string error)
        {
            propertyInfo = null;
            if (!TryLoadGraphData(assetPath, out var graph, out error))
                return false;

            var property = CreatePropertyInstance(graph, propertyType, displayName, referenceName, value, exposed, hidden, out error);
            if (property == null)
                return false;

            InvokeMethod(graph, "AddGraphInput", property, -1);
            if (!TrySaveGraphData(assetPath, graph, out error))
                return false;

            propertyInfo = TryReadGraphDocument(assetPath, out var document, out error)
                ? FindProperty(document, displayName, referenceName)
                : null;
            return true;
        }

        public static bool TryUpdateProperty(
            string assetPath,
            string propertyName,
            string referenceName,
            string newDisplayName,
            string newReferenceName,
            object value,
            bool? exposed,
            bool? hidden,
            out object propertyInfo,
            out string error)
        {
            propertyInfo = null;
            if (!TryLoadGraphData(assetPath, out var graph, out error))
                return false;

            var property = FindGraphInput(graph, "properties", propertyName, referenceName);
            if (property == null)
            {
                error = $"Property not found: {propertyName ?? referenceName}";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(newDisplayName))
                InvokeMethod(property, "SetDisplayNameAndSanitizeForGraph", graph, newDisplayName);
            if (!string.IsNullOrWhiteSpace(newReferenceName))
                InvokeMethod(property, "SetReferenceNameAndSanitizeForGraph", graph, newReferenceName);
            if (value != null && !TryAssignPropertyValue(property, value, out error))
                return false;
            if (exposed.HasValue)
                SetMemberValue(property, "m_GeneratePropertyBlock", exposed.Value);
            if (hidden.HasValue)
                SetMemberValue(property, "hidden", hidden.Value);

            if (!TrySaveGraphData(assetPath, graph, out error))
                return false;

            if (!TryReadGraphDocument(assetPath, out var document, out error))
                return false;

            propertyInfo = FindProperty(
                document,
                string.IsNullOrWhiteSpace(newDisplayName) ? propertyName : newDisplayName,
                string.IsNullOrWhiteSpace(newReferenceName) ? referenceName : newReferenceName);
            return true;
        }

        public static bool TryRemoveProperty(string assetPath, string propertyName, string referenceName, out string error)
        {
            if (!TryLoadGraphData(assetPath, out var graph, out error))
                return false;

            var property = FindGraphInput(graph, "properties", propertyName, referenceName);
            if (property == null)
            {
                error = $"Property not found: {propertyName ?? referenceName}";
                return false;
            }

            InvokeMethod(graph, "RemoveGraphInput", property);
            return TrySaveGraphData(assetPath, graph, out error);
        }

        public static bool TryAddKeyword(
            string assetPath,
            string keywordType,
            string displayName,
            string referenceName,
            string definition,
            string scope,
            string entries,
            int value,
            out object keywordInfo,
            out string error)
        {
            keywordInfo = null;
            if (!TryLoadGraphData(assetPath, out var graph, out error))
                return false;

            var effectiveDisplayName = string.IsNullOrWhiteSpace(displayName) ? (string.IsNullOrWhiteSpace(keywordType) ? "Boolean" : keywordType) : displayName;
            var keyword = CreateKeywordInstance(graph, keywordType, displayName, referenceName, definition, scope, entries, value, out error);
            if (keyword == null)
                return false;

            InvokeMethod(graph, "AddGraphInput", keyword, -1);
            if (!TrySaveGraphData(assetPath, graph, out error))
                return false;

            keywordInfo = TryReadGraphDocument(assetPath, out var document, out error)
                ? FindKeyword(document, effectiveDisplayName, referenceName)
                : null;
            return true;
        }

        public static bool TryUpdateKeyword(
            string assetPath,
            string displayName,
            string referenceName,
            string newDisplayName,
            string newReferenceName,
            string definition,
            string scope,
            string entries,
            int? value,
            out object keywordInfo,
            out string error)
        {
            keywordInfo = null;
            if (!TryLoadGraphData(assetPath, out var graph, out error))
                return false;

            var keyword = FindGraphInput(graph, "keywords", displayName, referenceName);
            if (keyword == null)
            {
                error = $"Keyword not found: {displayName ?? referenceName}";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(newDisplayName))
                InvokeMethod(keyword, "SetDisplayNameAndSanitizeForGraph", graph, newDisplayName);
            if (!string.IsNullOrWhiteSpace(newReferenceName))
                InvokeMethod(keyword, "SetReferenceNameAndSanitizeForGraph", graph, newReferenceName);
            if (!string.IsNullOrWhiteSpace(definition) && !TrySetEnumMember(keyword, "keywordDefinition", definition, out error))
                return false;
            if (!string.IsNullOrWhiteSpace(scope) && !TrySetEnumMember(keyword, "keywordScope", scope, out error))
                return false;
            if (!string.IsNullOrWhiteSpace(entries) && !TryAssignKeywordEntries(keyword, entries, out error))
                return false;
            if (value.HasValue)
                SetMemberValue(keyword, "value", value.Value);

            if (!TrySaveGraphData(assetPath, graph, out error))
                return false;

            if (!TryReadGraphDocument(assetPath, out var document, out error))
                return false;

            keywordInfo = FindKeyword(
                document,
                string.IsNullOrWhiteSpace(newDisplayName) ? displayName : newDisplayName,
                string.IsNullOrWhiteSpace(newReferenceName) ? referenceName : newReferenceName);
            return true;
        }

        public static bool TryRemoveKeyword(string assetPath, string displayName, string referenceName, out string error)
        {
            if (!TryLoadGraphData(assetPath, out var graph, out error))
                return false;

            var keyword = FindGraphInput(graph, "keywords", displayName, referenceName);
            if (keyword == null)
            {
                error = $"Keyword not found: {displayName ?? referenceName}";
                return false;
            }

            InvokeMethod(graph, "RemoveGraphInput", keyword);
            return TrySaveGraphData(assetPath, graph, out error);
        }

        public static bool TryReadGraphDocument(string assetPath, out ShaderGraphDocument document, out string error)
        {
            document = null;
            error = null;

            try
            {
                var fullPath = Path.GetFullPath(assetPath);
                if (!File.Exists(fullPath))
                {
                    error = $"Shader Graph asset not found: {assetPath}";
                    return false;
                }

                var text = File.ReadAllText(fullPath, Encoding.UTF8);
                var objects = ParseMultiJson(text);
                if (objects.Count == 0)
                {
                    error = $"No JSON objects found in Shader Graph asset: {assetPath}";
                    return false;
                }

                document = new ShaderGraphDocument
                {
                    AssetPath = assetPath,
                    Root = objects[0]
                };

                foreach (var obj in objects)
                {
                    document.OrderedObjects.Add(obj);
                    var objectId = obj["m_ObjectId"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(objectId))
                        document.ObjectsById[objectId] = obj;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static object DescribeGraphInfo(ShaderGraphDocument document)
        {
            var propertyIds = ExtractReferenceIds(document.Root["m_Properties"]);
            var keywordIds = ExtractReferenceIds(document.Root["m_Keywords"]);
            var nodeIds = ExtractReferenceIds(document.Root["m_Nodes"]);
            var edgeCount = document.Root["m_Edges"]?.Values<JToken>().Count() ?? 0;
            var targetIds = ExtractReferenceIds(document.Root["m_ActiveTargets"]);

            var targetTypes = targetIds
                .Select(id => document.ObjectsById.TryGetValue(id, out var target) ? ShortTypeName(target["m_Type"]?.ToString()) : null)
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var nodeTypeCounts = nodeIds
                .Select(id => document.ObjectsById.TryGetValue(id, out var node) ? ShortTypeName(node["m_Type"]?.ToString()) : null)
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .GroupBy(type => type, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new
                {
                    type = group.Key,
                    count = group.Count()
                })
                .ToArray();

            return new
            {
                success = true,
                assetPath = document.AssetPath,
                kind = document.AssetPath.EndsWith(".shadersubgraph", StringComparison.OrdinalIgnoreCase) ? "SubGraph" : "Graph",
                graphType = ShortTypeName(document.Root["m_Type"]?.ToString()),
                graphPath = document.Root["m_Path"]?.ToString(),
                precision = document.Root["m_GraphPrecision"]?.ToObject<int?>(),
                previewMode = document.Root["m_PreviewMode"]?.ToObject<int?>(),
                propertyCount = propertyIds.Count,
                keywordCount = keywordIds.Count,
                nodeCount = nodeIds.Count,
                edgeCount,
                targetTypes,
                nodeTypeCounts
            };
        }

        public static object DescribeGraphStructure(ShaderGraphDocument document, int maxNodes, int maxEdges)
        {
            var nodes = ExtractReferenceIds(document.Root["m_Nodes"])
                .Select(id => document.ObjectsById.TryGetValue(id, out var node) ? DescribeNode(node) : null)
                .Where(node => node != null)
                .Take(Math.Max(1, maxNodes))
                .ToArray();

            var edges = (document.Root["m_Edges"] as JArray ?? new JArray())
                .Select(edge => DescribeEdge(document, edge as JObject))
                .Where(edge => edge != null)
                .Take(Math.Max(1, maxEdges))
                .ToArray();

            return new
            {
                success = true,
                assetPath = document.AssetPath,
                nodes,
                edges,
                properties = GetProperties(document),
                keywords = GetKeywords(document)
            };
        }

        public static object[] GetProperties(ShaderGraphDocument document)
        {
            return ExtractReferenceIds(document.Root["m_Properties"])
                .Select(id => document.ObjectsById.TryGetValue(id, out var property) ? DescribeProperty(property) : null)
                .Where(property => property != null)
                .ToArray();
        }

        public static object[] GetKeywords(ShaderGraphDocument document)
        {
            return ExtractReferenceIds(document.Root["m_Keywords"])
                .Select(id => document.ObjectsById.TryGetValue(id, out var keyword) ? DescribeKeyword(keyword) : null)
                .Where(keyword => keyword != null)
                .ToArray();
        }

        public static object FindProperty(ShaderGraphDocument document, string displayName, string referenceName)
        {
            return GetProperties(document)
                .Select(JObject.FromObject)
                .FirstOrDefault(item => MatchesNamedItem(item, displayName, referenceName));
        }

        public static object FindKeyword(ShaderGraphDocument document, string displayName, string referenceName)
        {
            return GetKeywords(document)
                .Select(JObject.FromObject)
                .FirstOrDefault(item => MatchesNamedItem(item, displayName, referenceName));
        }

        private static object CreatePropertyInstance(
            object graph,
            string propertyType,
            string displayName,
            string referenceName,
            object value,
            bool exposed,
            bool hidden,
            out string error)
        {
            error = null;

            if (!PropertyTypeMap.TryGetValue(propertyType ?? string.Empty, out var propertyTypeName))
            {
                error = $"Unsupported propertyType '{propertyType}'. Supported values: {string.Join(", ", PropertyTypeMap.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))}";
                return null;
            }

            var propertyRuntimeType = FindTypeInAssemblies(propertyTypeName);
            if (propertyRuntimeType == null)
            {
                error = $"Shader Graph property type not found: {propertyTypeName}";
                return null;
            }

            var property = Activator.CreateInstance(propertyRuntimeType, true);
            SetMemberValue(property, "displayName", displayName);
            SetMemberValue(property, "m_GeneratePropertyBlock", exposed);
            SetMemberValue(property, "hidden", hidden);

            if (!string.IsNullOrWhiteSpace(referenceName))
                SetMemberValue(property, "m_OverrideReferenceName", referenceName);

            if (value != null && !TryAssignPropertyValue(property, value, out error))
                return null;

            return property;
        }

        private static bool TryAssignPropertyValue(object property, object value, out string error)
        {
            error = null;
            var propertyTypeName = property.GetType().Name;

            try
            {
                switch (propertyTypeName)
                {
                    case "Vector1ShaderProperty":
                        SetMemberValue(property, "value", ConvertToFloat(value));
                        return true;
                    case "Vector2ShaderProperty":
                        SetMemberValue(property, "value", ConvertToVector4(value, 2));
                        return true;
                    case "Vector3ShaderProperty":
                        SetMemberValue(property, "value", ConvertToVector4(value, 3));
                        return true;
                    case "Vector4ShaderProperty":
                        SetMemberValue(property, "value", ConvertToVector4(value, 4));
                        return true;
                    case "ColorShaderProperty":
                        SetMemberValue(property, "value", ConvertToColor(value));
                        return true;
                    case "BooleanShaderProperty":
                        SetMemberValue(property, "value", ConvertToBool(value));
                        return true;
                    case "Texture2DShaderProperty":
                        return TryAssignTexturePropertyValue(property, value, out error);
                    default:
                        error = $"Unsupported property runtime type: {propertyTypeName}";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TryAssignTexturePropertyValue(object property, object value, out string error)
        {
            error = null;

            if (value == null)
                return true;

            var assetPath = value.ToString();
            if (string.IsNullOrWhiteSpace(assetPath))
                return true;

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
            {
                error = $"Texture2D not found: {assetPath}";
                return false;
            }

            var serializableTextureType = FindTypeInAssemblies("UnityEditor.ShaderGraph.Internal.SerializableTexture");
            if (serializableTextureType == null)
            {
                error = "SerializableTexture type was not found";
                return false;
            }

            var serializableTexture = Activator.CreateInstance(serializableTextureType, true);
            SetMemberValue(serializableTexture, "texture", texture);
            SetMemberValue(property, "value", serializableTexture);
            return true;
        }

        private static object CreateKeywordInstance(
            object graph,
            string keywordTypeName,
            string displayName,
            string referenceName,
            string definition,
            string scope,
            string entries,
            int value,
            out string error)
        {
            error = null;

            var shaderKeywordType = FindTypeInAssemblies("UnityEditor.ShaderGraph.ShaderKeyword");
            var keywordTypeEnum = FindTypeInAssemblies("UnityEditor.ShaderGraph.KeywordType");
            if (shaderKeywordType == null || keywordTypeEnum == null)
            {
                error = "ShaderKeyword runtime types were not found";
                return null;
            }

            if (!EnumTryParse(keywordTypeEnum, string.IsNullOrWhiteSpace(keywordTypeName) ? "Boolean" : keywordTypeName, out var keywordTypeValue))
            {
                error = $"Invalid keywordType '{keywordTypeName}'. Valid values: {string.Join(", ", Enum.GetNames(keywordTypeEnum))}";
                return null;
            }

            var keyword = Activator.CreateInstance(shaderKeywordType, new[] { keywordTypeValue });
            SetMemberValue(keyword, "displayName", string.IsNullOrWhiteSpace(displayName) ? keywordTypeValue.ToString() : displayName);

            if (!string.IsNullOrWhiteSpace(referenceName))
                SetMemberValue(keyword, "m_OverrideReferenceName", referenceName);
            if (!string.IsNullOrWhiteSpace(definition) && !TrySetEnumMember(keyword, "keywordDefinition", definition, out error))
                return null;
            if (!string.IsNullOrWhiteSpace(scope) && !TrySetEnumMember(keyword, "keywordScope", scope, out error))
                return null;

            if (!string.IsNullOrWhiteSpace(entries) && !TryAssignKeywordEntries(keyword, entries, out error))
                return null;

            SetMemberValue(keyword, "value", value);
            return keyword;
        }

        private static bool TryAssignKeywordEntries(object keyword, string entries, out string error)
        {
            error = null;

            var parsedEntries = ParseKeywordEntries(entries);
            if (parsedEntries.Count == 0)
            {
                error = "Keyword entries are empty";
                return false;
            }

            var keywordEntryType = FindTypeInAssemblies("UnityEditor.ShaderGraph.KeywordEntry");
            if (keywordEntryType == null)
            {
                error = "KeywordEntry type was not found";
                return false;
            }

            var listType = typeof(List<>).MakeGenericType(keywordEntryType);
            var list = Activator.CreateInstance(listType);
            var addMethod = listType.GetMethod("Add");
            var constructor = keywordEntryType.GetConstructor(InstanceFlags, null, new[] { typeof(int), typeof(string), typeof(string) }, null);
            if (constructor == null || addMethod == null)
            {
                error = "KeywordEntry constructor was not found";
                return false;
            }

            for (var i = 0; i < parsedEntries.Count; i++)
            {
                var entry = constructor.Invoke(new object[] { i + 1, parsedEntries[i].displayName, parsedEntries[i].referenceName });
                addMethod.Invoke(list, new[] { entry });
            }

            SetMemberValue(keyword, "entries", list);
            SetMemberValue(keyword, "keywordType", Enum.Parse(FindTypeInAssemblies("UnityEditor.ShaderGraph.KeywordType"), "Enum"));
            return true;
        }

        private static List<(string displayName, string referenceName)> ParseKeywordEntries(string entries)
        {
            var result = new List<(string displayName, string referenceName)>();
            if (string.IsNullOrWhiteSpace(entries))
                return result;

            try
            {
                if (entries.TrimStart().StartsWith("[", StringComparison.Ordinal))
                {
                    var token = JToken.Parse(entries);
                    foreach (var item in token.Children())
                    {
                        if (item.Type == JTokenType.String)
                        {
                            var displayName = item.ToString();
                            result.Add((displayName, SanitizeKeywordReferenceName(displayName)));
                        }
                        else if (item.Type == JTokenType.Object)
                        {
                            var displayName = item["displayName"]?.ToString() ?? item["name"]?.ToString();
                            var referenceName = item["referenceName"]?.ToString();
                            if (!string.IsNullOrWhiteSpace(displayName))
                                result.Add((displayName, string.IsNullOrWhiteSpace(referenceName) ? SanitizeKeywordReferenceName(displayName) : referenceName));
                        }
                    }
                }
                else
                {
                    foreach (var item in entries.Split(','))
                    {
                        var displayName = item.Trim();
                        if (!string.IsNullOrWhiteSpace(displayName))
                            result.Add((displayName, SanitizeKeywordReferenceName(displayName)));
                    }
                }
            }
            catch
            {
                return new List<(string displayName, string referenceName)>();
            }

            return result;
        }

        private static string SanitizeKeywordReferenceName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "VALUE";

            var chars = input.Trim().Select(ch => char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : '_').ToArray();
            return new string(chars);
        }

        private static object FindGraphInput(object graph, string memberName, string displayName, string referenceName)
        {
            var enumerable = GetMemberValue(graph, memberName) as IEnumerable;
            if (enumerable == null)
                return null;

            foreach (var item in enumerable)
            {
                var currentDisplayName = GetMemberValue(item, "displayName")?.ToString();
                var currentReferenceName = GetMemberValue(item, "referenceName")?.ToString();
                if ((!string.IsNullOrWhiteSpace(displayName) && string.Equals(currentDisplayName, displayName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(referenceName) && string.Equals(currentReferenceName, referenceName, StringComparison.OrdinalIgnoreCase)))
                {
                    return item;
                }
            }

            return null;
        }

        private static List<JObject> ParseMultiJson(string text)
        {
            var result = new List<JObject>();
            var start = -1;
            var depth = 0;
            var inString = false;
            var escape = false;

            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (start < 0)
                {
                    if (char.IsWhiteSpace(ch))
                        continue;
                    if (ch == '{')
                    {
                        start = i;
                        depth = 1;
                        inString = false;
                        escape = false;
                    }
                    continue;
                }

                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                    }
                    else if (ch == '\\')
                    {
                        escape = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    continue;
                }

                if (ch == '{')
                {
                    depth++;
                    continue;
                }

                if (ch != '}')
                    continue;

                depth--;
                if (depth != 0)
                    continue;

                var json = text.Substring(start, i - start + 1);
                result.Add(JObject.Parse(json));
                start = -1;
            }

            return result;
        }

        private static List<string> ExtractReferenceIds(JToken token)
        {
            if (!(token is JArray array))
                return new List<string>();

            return array
                .Select(item => item?["m_Id"]?.ToString())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();
        }

        private static object DescribeNode(JObject node)
        {
            return new
            {
                id = node["m_ObjectId"]?.ToString(),
                type = ShortTypeName(node["m_Type"]?.ToString()),
                name = node["m_Name"]?.ToString(),
                slotCount = (node["m_Slots"] as JArray)?.Count ?? 0
            };
        }

        private static object DescribeEdge(ShaderGraphDocument document, JObject edge)
        {
            if (edge == null)
                return null;

            var outputNodeId = edge["m_OutputSlot"]?["m_Node"]?["m_Id"]?.ToString();
            var inputNodeId = edge["m_InputSlot"]?["m_Node"]?["m_Id"]?.ToString();

            document.ObjectsById.TryGetValue(outputNodeId ?? string.Empty, out var outputNode);
            document.ObjectsById.TryGetValue(inputNodeId ?? string.Empty, out var inputNode);

            return new
            {
                outputNodeId,
                outputNode = outputNode?["m_Name"]?.ToString() ?? ShortTypeName(outputNode?["m_Type"]?.ToString()),
                outputSlotId = edge["m_OutputSlot"]?["m_SlotId"]?.ToObject<int?>(),
                inputNodeId,
                inputNode = inputNode?["m_Name"]?.ToString() ?? ShortTypeName(inputNode?["m_Type"]?.ToString()),
                inputSlotId = edge["m_InputSlot"]?["m_SlotId"]?.ToObject<int?>()
            };
        }

        private static object DescribeProperty(JObject property)
        {
            return new
            {
                id = property["m_ObjectId"]?.ToString(),
                type = NormalizePropertyTypeName(ShortTypeName(property["m_Type"]?.ToString())),
                fullType = property["m_Type"]?.ToString(),
                displayName = property["m_Name"]?.ToString(),
                referenceName = FirstNonEmpty(property["m_OverrideReferenceName"]?.ToString(), property["m_DefaultReferenceName"]?.ToString()),
                exposed = property["m_GeneratePropertyBlock"]?.ToObject<bool?>(),
                hidden = property["m_Hidden"]?.ToObject<bool?>(),
                guid = property["m_Guid"]?["m_GuidSerialized"]?.ToString(),
                value = ConvertTokenValue(property["m_Value"])
            };
        }

        private static object DescribeKeyword(JObject keyword)
        {
            var entries = (keyword["m_Entries"] as JArray)?.Select(entry => new
            {
                id = entry["id"]?.ToObject<int?>(),
                displayName = entry["displayName"]?.ToString(),
                referenceName = entry["referenceName"]?.ToString()
            }).ToArray() ?? Array.Empty<object>();

            var keywordType = keyword["m_KeywordType"]?.ToObject<int?>();
            var definition = keyword["m_KeywordDefinition"]?.ToObject<int?>();
            var scope = keyword["m_KeywordScope"]?.ToObject<int?>();

            return new
            {
                id = keyword["m_ObjectId"]?.ToString(),
                displayName = keyword["m_Name"]?.ToString(),
                referenceName = FirstNonEmpty(keyword["m_OverrideReferenceName"]?.ToString(), keyword["m_DefaultReferenceName"]?.ToString()),
                keywordType = keywordType.HasValue && KeywordTypeNames.TryGetValue(keywordType.Value, out var resolvedKeywordType) ? resolvedKeywordType : keywordType?.ToString(),
                definition = definition.HasValue && KeywordDefinitionNames.TryGetValue(definition.Value, out var resolvedDefinition) ? resolvedDefinition : definition?.ToString(),
                scope = scope.HasValue && KeywordScopeNames.TryGetValue(scope.Value, out var resolvedScope) ? resolvedScope : scope?.ToString(),
                value = keyword["m_Value"]?.ToObject<int?>(),
                entries
            };
        }

        private static object ConvertTokenValue(JToken token)
        {
            if (token == null)
                return null;

            if (token.Type == JTokenType.Object)
                return token.ToObject<Dictionary<string, object>>();
            if (token.Type == JTokenType.Array)
                return token.ToObject<object[]>();
            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer || token.Type == JTokenType.String || token.Type == JTokenType.Boolean)
                return ((JValue)token).Value;

            return token.ToString();
        }

        private static bool MatchesNamedItem(JObject item, string displayName, string referenceName)
        {
            return (!string.IsNullOrWhiteSpace(displayName) && string.Equals(item["displayName"]?.ToString(), displayName, StringComparison.OrdinalIgnoreCase)) ||
                   (!string.IsNullOrWhiteSpace(referenceName) && string.Equals(item["referenceName"]?.ToString(), referenceName, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizePropertyTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return typeName;

            if (typeName.EndsWith("ShaderProperty", StringComparison.Ordinal))
                return typeName.Substring(0, typeName.Length - "ShaderProperty".Length);
            return typeName;
        }

        private static string ShortTypeName(string fullTypeName)
        {
            if (string.IsNullOrWhiteSpace(fullTypeName))
                return fullTypeName;

            var lastDot = fullTypeName.LastIndexOf('.');
            return lastDot >= 0 ? fullTypeName.Substring(lastDot + 1) : fullTypeName;
        }

        private static string FirstNonEmpty(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) ? left : right;
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null)
                return null;

            var type = instance.GetType();
            var property = GetPropertyRecursive(type, memberName);
            if (property != null)
                return property.GetValue(instance, null);

            var field = GetFieldRecursive(type, memberName);
            return field?.GetValue(instance);
        }

        private static void SetMemberValue(object instance, string memberName, object value)
        {
            var type = instance.GetType();
            var property = GetPropertyRecursive(type, memberName);
            if (property != null)
            {
                property.SetValue(instance, ChangeType(value, property.PropertyType), null);
                return;
            }

            var field = GetFieldRecursive(type, memberName);
            if (field != null)
            {
                field.SetValue(instance, ChangeType(value, field.FieldType));
                return;
            }

            throw new MissingMemberException(type.FullName, memberName);
        }

        private static object InvokeMethod(object instance, string methodName, params object[] arguments)
        {
            var type = instance.GetType();
            var methods = GetMethodsRecursive(type, methodName).ToArray();
            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length != arguments.Length)
                    continue;

                try
                {
                    var invokeArguments = new object[arguments.Length];
                    for (var i = 0; i < parameters.Length; i++)
                        invokeArguments[i] = ChangeType(arguments[i], parameters[i].ParameterType);
                    return method.Invoke(instance, invokeArguments);
                }
                catch
                {
                    // Try the next overload.
                }
            }

            throw new MissingMethodException(type.FullName, methodName);
        }

        private static bool TrySetEnumMember(object instance, string memberName, string enumName, out string error)
        {
            error = null;
            var type = instance.GetType();
            var property = GetPropertyRecursive(type, memberName);
            var targetType = property != null ? property.PropertyType : GetFieldRecursive(type, memberName)?.FieldType;
            if (targetType == null || !targetType.IsEnum)
            {
                error = $"Enum member '{memberName}' was not found";
                return false;
            }

            if (!EnumTryParse(targetType, enumName, out var parsed))
            {
                error = $"Invalid {memberName} '{enumName}'. Valid values: {string.Join(", ", Enum.GetNames(targetType))}";
                return false;
            }

            SetMemberValue(instance, memberName, parsed);
            return true;
        }

        private static bool EnumTryParse(Type enumType, string enumName, out object parsed)
        {
            parsed = null;
            if (enumType == null || !enumType.IsEnum || string.IsNullOrWhiteSpace(enumName))
                return false;

            foreach (var name in Enum.GetNames(enumType))
            {
                if (string.Equals(name, enumName, StringComparison.OrdinalIgnoreCase))
                {
                    parsed = Enum.Parse(enumType, name);
                    return true;
                }
            }

            return false;
        }

        private static object ChangeType(object value, Type targetType)
        {
            if (targetType == null)
                return value;

            if (value == null)
                return null;

            if (targetType.IsInstanceOfType(value))
                return value;

            if (targetType.IsEnum)
            {
                if (value is string enumName)
                    return Enum.Parse(targetType, enumName, true);
                return Enum.ToObject(targetType, value);
            }

            if (targetType == typeof(string))
                return value.ToString();

            if (targetType == typeof(int))
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(float))
                return Convert.ToSingle(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(bool))
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);

            return value;
        }

        private static bool AssetPathExists(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return false;

            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
                return true;

            return File.Exists(Path.GetFullPath(assetPath));
        }

        private static bool TryResolveTemplateFilePath(string templatePath, out string resolvedPath)
        {
            resolvedPath = null;
            if (string.IsNullOrWhiteSpace(templatePath))
                return false;

            if (string.Equals(templatePath, BuiltinBlankTemplatePath, StringComparison.OrdinalIgnoreCase))
            {
                resolvedPath = BuiltinBlankTemplatePath;
                return true;
            }

            if (Path.IsPathRooted(templatePath))
            {
                resolvedPath = Path.GetFullPath(templatePath);
                return File.Exists(resolvedPath);
            }

            if (templatePath.StartsWith(PackageRoot, StringComparison.OrdinalIgnoreCase))
            {
                if (!TryGetPackageRoot(out var packageRoot, out _))
                    return false;

                var relative = templatePath.Substring(PackageRoot.Length).TrimStart('/', '\\');
                resolvedPath = Path.GetFullPath(Path.Combine(packageRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
                return File.Exists(resolvedPath);
            }

            resolvedPath = Path.GetFullPath(templatePath);
            return File.Exists(resolvedPath);
        }

        private static bool TryGetTemplatesDirectory(out string templatesDirectory)
        {
            templatesDirectory = null;
            if (!TryGetPackageRoot(out var packageRoot, out _))
                return false;

            templatesDirectory = Path.Combine(packageRoot, "GraphTemplates");
            return Directory.Exists(templatesDirectory);
        }

        private static bool TryGetPackageRoot(out string packageRoot, out string packageVersion)
        {
            packageRoot = null;
            packageVersion = null;

            var graphDataType = FindTypeInAssemblies("UnityEditor.ShaderGraph.GraphData");
            if (graphDataType == null)
                return false;

            try
            {
                var packageInfo = PkgInfo.FindForAssembly(graphDataType.Assembly);
                if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
                {
                    packageRoot = Path.GetFullPath(packageInfo.resolvedPath);
                    packageVersion = packageInfo.version;
                    return Directory.Exists(packageRoot);
                }
            }
            catch
            {
                // Fall through to heuristic path resolution.
            }

            try
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                if (!string.IsNullOrWhiteSpace(projectRoot))
                {
                    var packageCacheRoot = Path.Combine(projectRoot, "Library", "PackageCache");
                    if (Directory.Exists(packageCacheRoot))
                    {
                        var candidates = Directory.GetDirectories(packageCacheRoot, "com.unity.shadergraph@*")
                            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                        if (candidates.Length > 0)
                        {
                            packageRoot = Path.GetFullPath(candidates[0]);
                            var directoryName = Path.GetFileName(packageRoot);
                            var atIndex = directoryName.IndexOf('@');
                            if (atIndex >= 0 && atIndex < directoryName.Length - 1)
                                packageVersion = directoryName.Substring(atIndex + 1);
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Ignore filesystem probing failures.
            }

            return false;
        }

        private static PropertyInfo GetPropertyRecursive(Type type, string memberName)
        {
            while (type != null)
            {
                var property = type.GetProperty(memberName, InstanceFlags);
                if (property != null)
                    return property;
                type = type.BaseType;
            }

            return null;
        }

        private static FieldInfo GetFieldRecursive(Type type, string memberName)
        {
            while (type != null)
            {
                var field = type.GetField(memberName, InstanceFlags);
                if (field != null)
                    return field;
                type = type.BaseType;
            }

            return null;
        }

        private static IEnumerable<MethodInfo> GetMethodsRecursive(Type type, string methodName)
        {
            while (type != null)
            {
                foreach (var method in type.GetMethods(InstanceFlags).Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal)))
                    yield return method;
                type = type.BaseType;
            }
        }

        private static float ConvertToFloat(object value)
        {
            if (value is JToken token)
                value = ConvertTokenValue(token);
            return Convert.ToSingle(value, CultureInfo.InvariantCulture);
        }

        private static bool ConvertToBool(object value)
        {
            if (value is JToken token)
                value = ConvertTokenValue(token);

            if (value is bool boolValue)
                return boolValue;
            if (value is string stringValue)
            {
                if (bool.TryParse(stringValue, out var parsedBool))
                    return parsedBool;
                if (int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
                    return parsedInt != 0;
            }

            return Convert.ToInt32(value, CultureInfo.InvariantCulture) != 0;
        }

        private static object ConvertToVector4(object value, int dimensions)
        {
            if (value is JToken token)
                value = token;

            var components = new float[4];
            if (value is JObject obj)
            {
                components[0] = obj["x"]?.ToObject<float?>() ?? obj["r"]?.ToObject<float?>() ?? 0f;
                components[1] = obj["y"]?.ToObject<float?>() ?? obj["g"]?.ToObject<float?>() ?? 0f;
                components[2] = obj["z"]?.ToObject<float?>() ?? obj["b"]?.ToObject<float?>() ?? 0f;
                components[3] = obj["w"]?.ToObject<float?>() ?? obj["a"]?.ToObject<float?>() ?? 0f;
            }
            else if (value is JArray array)
            {
                for (var i = 0; i < Math.Min(array.Count, 4); i++)
                    components[i] = array[i].ToObject<float>();
            }
            else if (value is string stringValue)
            {
                var parts = stringValue.Split(',')
                    .Select(part => part.Trim())
                    .Where(part => !string.IsNullOrWhiteSpace(part))
                    .ToArray();
                for (var i = 0; i < Math.Min(parts.Length, 4); i++)
                    components[i] = float.Parse(parts[i], CultureInfo.InvariantCulture);
            }
            else
            {
                components[0] = Convert.ToSingle(value, CultureInfo.InvariantCulture);
            }

            if (dimensions == 2)
                return new Vector4(components[0], components[1], 0f, 0f);
            if (dimensions == 3)
                return new Vector4(components[0], components[1], components[2], 0f);
            return new Vector4(components[0], components[1], components[2], components[3]);
        }

        private static Color ConvertToColor(object value)
        {
            if (value is JToken token)
                value = token;

            if (value is string html && ColorUtility.TryParseHtmlString(html, out var parsedColor))
                return parsedColor;

            if (value is JObject obj)
            {
                return new Color(
                    obj["r"]?.ToObject<float?>() ?? obj["x"]?.ToObject<float?>() ?? 0f,
                    obj["g"]?.ToObject<float?>() ?? obj["y"]?.ToObject<float?>() ?? 0f,
                    obj["b"]?.ToObject<float?>() ?? obj["z"]?.ToObject<float?>() ?? 0f,
                    obj["a"]?.ToObject<float?>() ?? obj["w"]?.ToObject<float?>() ?? 1f);
            }

            if (value is JArray array)
            {
                var components = array.Select(item => item.ToObject<float>()).ToArray();
                return new Color(
                    components.Length > 0 ? components[0] : 0f,
                    components.Length > 1 ? components[1] : 0f,
                    components.Length > 2 ? components[2] : 0f,
                    components.Length > 3 ? components[3] : 1f);
            }

            throw new InvalidOperationException("Color value must be an HTML string, object, or array");
        }
    }
}

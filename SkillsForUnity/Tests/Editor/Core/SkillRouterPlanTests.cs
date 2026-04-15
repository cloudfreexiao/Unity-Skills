using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnitySkills.Tests.Core
{
    [TestFixture]
    public class SkillRouterPlanTests
    {
        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObjectFinder.InvalidateCache();
            SkillRouter.Refresh();
        }

        [TearDown]
        public void TearDown()
        {
            GameObjectFinder.InvalidateCache();
        }

        [Test]
        public void DryRun_GameObjectCreate_WithUnknownPrimitive_ReportsSemanticError()
        {
            var json = SkillRouter.DryRun("gameobject_create", "{\"name\":\"Cube\",\"primitiveType\":\"Nope\"}");
            var obj = JObject.Parse(json);

            Assert.AreEqual("dryRun", obj["status"]?.ToString());
            Assert.IsFalse(obj["valid"]?.Value<bool>() ?? true);
            StringAssert.Contains("Unknown primitive type", obj["validation"]?["semanticErrors"]?[0]?["error"]?.ToString());
        }

        [Test]
        public void Plan_GameObjectCreate_WithParent_ReturnsSemanticCreateChange()
        {
            var parent = new GameObject("Parent");
            GameObjectFinder.InvalidateCache();

            var json = SkillRouter.Plan("gameobject_create", "{\"name\":\"Child\",\"parentName\":\"Parent\"}");
            var obj = JObject.Parse(json);

            Assert.AreEqual("plan", obj["status"]?.ToString());
            Assert.AreEqual("semantic", obj["planLevel"]?.ToString());
            Assert.IsTrue(obj["valid"]?.Value<bool>() ?? false);
            Assert.AreEqual("Parent/Child", obj["changes"]?["create"]?[0]?["predictedPath"]?.ToString());
        }

        [Test]
        public void Plan_ComponentAdd_WithDuplicateSingleInstance_AddsWarning()
        {
            var go = new GameObject("Actor");
            go.AddComponent<BoxCollider>();
            GameObjectFinder.InvalidateCache();

            var json = SkillRouter.Plan("component_add", "{\"name\":\"Actor\",\"componentType\":\"BoxCollider\"}");
            var obj = JObject.Parse(json);

            Assert.AreEqual("plan", obj["status"]?.ToString());
            Assert.IsTrue((obj["validation"]?["warnings"] as JArray)?.Count > 0);
        }

        [Test]
        public void Plan_AssetImport_ForScriptDomainAsset_IncludesServerAvailability()
        {
            var json = SkillRouter.Plan("asset_import", "{\"sourcePath\":\"C:/temp/Example.cs\",\"destinationPath\":\"Assets/Example.cs\"}");
            var obj = JObject.Parse(json);

            Assert.AreEqual("plan", obj["status"]?.ToString());
            Assert.IsTrue(obj["serverAvailability"]?["mayDisconnect"]?.Value<bool>() ?? false);
        }

        [Test]
        public void Plan_GameObjectDeleteBatch_WithStringItems_ProducesBatchPreview()
        {
            new GameObject("A");
            new GameObject("B");
            GameObjectFinder.InvalidateCache();

            var json = SkillRouter.Plan("gameobject_delete_batch", "{\"items\":\"[\\\"A\\\",\\\"B\\\"]\"}");
            var obj = JObject.Parse(json);

            Assert.AreEqual("plan", obj["status"]?.ToString());
            Assert.AreEqual(2, obj["batchPreview"]?["totalItems"]?.Value<int>());
            Assert.AreEqual(2, (obj["changes"]?["delete"] as JArray)?.Count);
        }

        // === New tests: Declarative metadata ===

        [Test]
        public void DryRun_ReturnsDeclarativeMetadata_ForAnnotatedSkill()
        {
            var json = SkillRouter.DryRun("script_create", "{\"scriptName\":\"TestScript\"}");
            var obj = JObject.Parse(json);

            Assert.AreEqual("dryRun", obj["status"]?.ToString());
            Assert.IsTrue(obj["impact"]?["mutatesAssets"]?.Value<bool>() ?? false);
            Assert.IsTrue(obj["impact"]?["mayTriggerReload"]?.Value<bool>() ?? false);
            Assert.AreEqual("high", obj["impact"]?["riskLevel"]?.ToString());
        }

        [Test]
        public void DryRun_ReadOnlySkill_ReportsNoMutations()
        {
            var json = SkillRouter.DryRun("scene_component_stats", "{}");
            var obj = JObject.Parse(json);

            Assert.AreEqual("dryRun", obj["status"]?.ToString());
            Assert.IsFalse(obj["impact"]?["mutatesScene"]?.Value<bool>() ?? true);
            Assert.IsFalse(obj["impact"]?["mutatesAssets"]?.Value<bool>() ?? true);
            Assert.IsFalse(obj["impact"]?["mayTriggerReload"]?.Value<bool>() ?? true);
        }

        [Test]
        public void DryRun_ScriptReplace_ReportsReloadRisk()
        {
            var json = SkillRouter.DryRun("script_replace", "{\"scriptPath\":\"Assets/Scripts/Test.cs\",\"find\":\"A\",\"replace\":\"B\"}");
            var obj = JObject.Parse(json);

            Assert.AreEqual("dryRun", obj["status"]?.ToString());
            Assert.IsTrue(obj["impact"]?["mutatesAssets"]?.Value<bool>() ?? false);
            Assert.IsTrue(obj["impact"]?["mayTriggerReload"]?.Value<bool>() ?? false);
            Assert.AreEqual("high", obj["impact"]?["riskLevel"]?.ToString());
        }

        // === New tests: Scene/Prefab/Script semantic planners ===

        [Test]
        public void Plan_SceneCreate_ReturnsSemanticPlan()
        {
            var json = SkillRouter.Plan("scene_create", "{\"scenePath\":\"Assets/Scenes/TestPlan.unity\"}");
            var obj = JObject.Parse(json);

            Assert.AreEqual("plan", obj["status"]?.ToString());
            Assert.AreEqual("semantic", obj["planLevel"]?.ToString());
            Assert.IsNotNull(obj["changes"]?["create"]?[0]?["path"]);
        }

        [Test]
        public void Plan_ScriptCreate_WarnsServerAvailability()
        {
            var json = SkillRouter.Plan("script_create", "{\"scriptName\":\"PlanTestScript\",\"folder\":\"Assets/Scripts\"}");
            var obj = JObject.Parse(json);

            Assert.AreEqual("plan", obj["status"]?.ToString());
            Assert.AreEqual("semantic", obj["planLevel"]?.ToString());
            Assert.IsTrue(obj["serverAvailability"]?["mayDisconnect"]?.Value<bool>() ?? false);
        }

        [Test]
        public void Plan_ScriptCreate_WithInvalidClassName_ReportsSemanticError()
        {
            var json = SkillRouter.Plan("script_create", "{\"scriptName\":\"123Invalid\"}");
            var obj = JObject.Parse(json);

            Assert.AreEqual("plan", obj["status"]?.ToString());
            Assert.IsFalse(obj["valid"]?.Value<bool>() ?? true);
            StringAssert.Contains("not a valid C# class name", obj["validation"]?["semanticErrors"]?[0]?["error"]?.ToString());
        }

        [Test]
        public void Plan_PrefabCreate_ReturnsSemanticPlan()
        {
            var go = new GameObject("PlanTestObj");
            GameObjectFinder.InvalidateCache();

            var json = SkillRouter.Plan("prefab_create", "{\"name\":\"PlanTestObj\",\"savePath\":\"Assets/Prefabs/Test.prefab\"}");
            var obj = JObject.Parse(json);

            Assert.AreEqual("plan", obj["status"]?.ToString());
            Assert.AreEqual("semantic", obj["planLevel"]?.ToString());
            Assert.IsNotNull(obj["changes"]?["create"]?[0]?["path"]);
        }

        // === New test: workflow_plan ===

        [Test]
        public void WorkflowPlan_AggregatesRiskAndDetectsDependencies()
        {
            var skillsJson = "[{\"name\":\"gameobject_create\",\"params\":{\"name\":\"Player\",\"primitiveType\":\"Cube\"}},{\"name\":\"component_add\",\"params\":{\"name\":\"Player\",\"componentType\":\"Rigidbody\"}}]";
            var requestJson = "{\"skillsJson\":" + Newtonsoft.Json.JsonConvert.SerializeObject(skillsJson) + "}";

            var result = SkillRouter.Execute("workflow_plan", requestJson);
            var obj = JObject.Parse(result);
            var payload = obj["result"] as JObject ?? obj;

            Assert.AreEqual(2, payload["totalSteps"]?.Value<int>());
            Assert.AreEqual("medium", payload["totalRisk"]?.ToString());
            Assert.IsTrue((payload["dependencies"] as JArray)?.Count > 0);
        }
    }
}

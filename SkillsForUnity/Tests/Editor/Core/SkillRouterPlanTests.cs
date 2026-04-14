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
    }
}

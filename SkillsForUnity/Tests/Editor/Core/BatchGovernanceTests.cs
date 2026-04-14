using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnitySkills.Tests.Core
{
    [TestFixture]
    public class BatchGovernanceTests
    {
        private static JObject ToJObject(object result)
        {
            return JObject.Parse(JsonConvert.SerializeObject(result));
        }

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObjectFinder.InvalidateCache();
        }

        [TearDown]
        public void TearDown()
        {
            GameObjectFinder.InvalidateCache();
        }

        [Test]
        public void BatchQueryGameObjects_FiltersByName()
        {
            new GameObject("PlayerRoot");
            new GameObject("EnemyRoot");
            GameObjectFinder.InvalidateCache();

            var result = BatchSkills.BatchQueryGameObjects("{\"name\":\"Player\",\"includeInactive\":true}");
            var json = ToJObject(result);

            Assert.IsTrue(json["success"]?.Value<bool>() ?? false);
            Assert.AreEqual(1, json["count"]?.Value<int>());
            Assert.AreEqual("PlayerRoot", json["objects"]?[0]?["name"]?.ToString());
        }

        [Test]
        public void BatchPreviewRename_ThenExecuteSync_RenamesObjectsAndCreatesReport()
        {
            new GameObject("CubeA");
            new GameObject("CubeB");
            GameObjectFinder.InvalidateCache();

            var preview = ToJObject(BatchSkills.BatchPreviewRename("{\"name\":\"Cube\",\"includeInactive\":true}", mode: "prefix", prefix: "Renamed_"));
            var token = preview["confirmToken"]?.ToString();
            Assert.IsNotNull(token);
            Assert.AreEqual(2, preview["executableCount"]?.Value<int>());

            var execution = ToJObject(BatchSkills.BatchExecute(token, runAsync: false, chunkSize: 10));
            Assert.AreEqual("completed", execution["status"]?.ToString());
            Assert.IsNotNull(execution["reportId"]?.ToString());
            Assert.IsNotNull(GameObject.Find("Renamed_CubeA"));
            Assert.IsNotNull(GameObject.Find("Renamed_CubeB"));
        }

        [Test]
        public void BatchPreviewSetProperty_WhenAlreadyAtTargetValue_ReturnsSkip()
        {
            var go = new GameObject("Main Light");
            var light = go.AddComponent<Light>();
            light.intensity = 1f;
            GameObjectFinder.InvalidateCache();

            var preview = ToJObject(BatchSkills.BatchPreviewSetProperty(
                "{\"name\":\"Main Light\",\"includeInactive\":true}",
                componentType: "Light",
                propertyName: "intensity",
                value: "1"));

            Assert.AreEqual(0, preview["executableCount"]?.Value<int>());
            Assert.AreEqual(1, preview["skipCount"]?.Value<int>());
            Assert.AreEqual("already_target_value", preview["skipReasons"]?[0]?["reason"]?.ToString());
        }

        [Test]
        public void BatchCleanupTempObjects_AsyncJobCompletes()
        {
            new GameObject("Temp_Helper_1");
            new GameObject("Temp_Helper_2");
            GameObjectFinder.InvalidateCache();

            var preview = ToJObject(BatchSkills.BatchCleanupTempObjects("{\"includeInactive\":true}"));
            var token = preview["confirmToken"]?.ToString();
            Assert.IsNotNull(token);
            Assert.AreEqual(2, preview["executableCount"]?.Value<int>());

            var accepted = ToJObject(BatchSkills.BatchExecute(token, runAsync: true, chunkSize: 1));
            var jobId = accepted["jobId"]?.ToString();
            Assert.AreEqual("accepted", accepted["status"]?.ToString());
            Assert.IsNotNull(jobId);

            var waited = ToJObject(BatchSkills.JobWait(jobId, 5000));
            Assert.AreEqual("completed", waited["status"]?.ToString());
            Assert.IsNotNull(waited["reportId"]?.ToString());
            Assert.IsNull(GameObject.Find("Temp_Helper_1"));
            Assert.IsNull(GameObject.Find("Temp_Helper_2"));
        }
    }
}

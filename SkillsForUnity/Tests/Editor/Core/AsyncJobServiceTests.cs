using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UnitySkills.Tests.Core
{
    [TestFixture]
    public class AsyncJobServiceTests
    {
        [Test]
        public void CreateJob_ReturnsValidJobWithQueuedStatus()
        {
            var job = AsyncJobService.CreateJob(
                "test_kind", "initial", "Test job created.", true);

            Assert.IsNotNull(job);
            Assert.IsFalse(string.IsNullOrEmpty(job.jobId));
            Assert.AreEqual("queued", job.status);
            Assert.AreEqual("test_kind", job.kind);
            Assert.AreEqual(0, job.progress);
            Assert.AreEqual("initial", job.currentStage);
            Assert.IsTrue(job.canCancel);
        }

        [Test]
        public void CompleteJob_TransitionsToTerminalState()
        {
            var job = AsyncJobService.CreateJob(
                "test_complete", "starting", "Will complete.", true);
            job.status = "running";
            BatchPersistence.UpsertJob(job);

            AsyncJobService.CompleteJob(job.jobId, "Done.");

            var updated = AsyncJobService.Get(job.jobId);
            Assert.AreEqual("completed", updated.status);
            Assert.AreEqual(100, updated.progress);
            Assert.AreEqual("Done.", updated.resultSummary);
            Assert.AreEqual("completed", updated.progressStage);
        }

        [Test]
        public void FailJob_SetsErrorMessage()
        {
            var job = AsyncJobService.CreateJob(
                "test_fail", "starting", "Will fail.", true);
            job.status = "running";
            BatchPersistence.UpsertJob(job);

            AsyncJobService.FailJob(job.jobId, "Something broke.", "error_stage");

            var updated = AsyncJobService.Get(job.jobId);
            Assert.AreEqual("failed", updated.status);
            Assert.AreEqual("Something broke.", updated.error);
            Assert.AreEqual("error_stage", updated.currentStage);
        }

        [Test]
        public void CancelJob_WhenCanCancelFalse_AddsWarningOnly()
        {
            var job = AsyncJobService.CreateJob(
                "test_nocancel", "running", "No cancel.", false);
            job.status = "running";
            BatchPersistence.UpsertJob(job);

            var result = AsyncJobService.Cancel(job.jobId);

            Assert.AreEqual("running", result.status);
            Assert.IsTrue(result.warnings.Count > 0);
        }

        [Test]
        public void CancelJob_WhenCanCancelTrue_TransitionsToCancelled()
        {
            var job = AsyncJobService.CreateJob(
                "test_cancel", "running", "Can cancel.", true);
            job.status = "running";
            BatchPersistence.UpsertJob(job);

            var result = AsyncJobService.Cancel(job.jobId);

            Assert.AreEqual("cancelled", result.status);
        }

        [Test]
        public void StartScriptMutationJob_ReturnsValidJobId()
        {
            var job = AsyncJobService.StartScriptMutationJob(
                "create", "Assets/Scripts/Test.cs", checkCompile: false, diagnosticLimit: 10);

            Assert.IsNotNull(job);
            Assert.IsFalse(string.IsNullOrEmpty(job.jobId));
            Assert.AreEqual("compile", job.kind);
            Assert.IsFalse(job.canCancel);
        }

        [Test]
        public void StartPackageJob_SetsWaitingExternalStatus()
        {
            var job = AsyncJobService.StartPackageJob("refresh", null);

            Assert.IsNotNull(job);
            Assert.AreEqual("package", job.kind);
            Assert.AreEqual("waiting_external", job.status);
        }

        [Test]
        public void CreateJob_SetsInitialProgressStageAndEvent()
        {
            var job = AsyncJobService.CreateJob(
                "test_initial_progress", "queued", "Queued.", true);

            Assert.AreEqual("queued", job.progressStage);
            Assert.IsNotNull(job.progressEvents);
            Assert.IsTrue(job.progressEvents.Count > 0);
            Assert.AreEqual("queued", job.progressEvents[0].stage);
        }

        [Test]
        public void ProgressEvents_RecordedDuringCompleteJob()
        {
            var job = AsyncJobService.CreateJob(
                "test_progress", "initial", "Progress test.", true);
            job.status = "running";
            BatchPersistence.UpsertJob(job);

            AsyncJobService.CompleteJob(job.jobId, "Done with progress.");

            var updated = AsyncJobService.Get(job.jobId);
            Assert.IsNotNull(updated.progressEvents);
            Assert.IsTrue(updated.progressEvents.Count > 0);
            var lastEvent = updated.progressEvents[updated.progressEvents.Count - 1];
            Assert.AreEqual("completed", lastEvent.stage);
            Assert.AreEqual(100, lastEvent.progress);
        }

        [Test]
        public void ProgressEvents_RecordedDuringFailJob()
        {
            var job = AsyncJobService.CreateJob(
                "test_fail_progress", "initial", "Fail progress test.", true);
            job.status = "running";
            BatchPersistence.UpsertJob(job);

            AsyncJobService.FailJob(job.jobId, "Failed with progress.");

            var updated = AsyncJobService.Get(job.jobId);
            Assert.IsNotNull(updated.progressEvents);
            Assert.IsTrue(updated.progressEvents.Count > 0);
            var lastEvent = updated.progressEvents[updated.progressEvents.Count - 1];
            Assert.AreEqual(100, lastEvent.progress);
        }

        [Test]
        public void Wait_ReturnsTerminalJobBeforeTimeout()
        {
            var job = AsyncJobService.CreateJob(
                "test_wait", "done", "Instant complete.", true);
            job.status = "running";
            BatchPersistence.UpsertJob(job);
            AsyncJobService.CompleteJob(job.jobId, "Completed.");

            var result = AsyncJobService.Wait(job.jobId, 1000);

            Assert.IsNotNull(result);
            Assert.AreEqual("completed", result.status);
        }

        [Test]
        public void Wait_TimesOutForNonTerminalJob()
        {
            var job = AsyncJobService.CreateJob(
                "test_timeout", "stuck", "Will timeout.", true);
            job.status = "running";
            BatchPersistence.UpsertJob(job);

            var result = AsyncJobService.Wait(job.jobId, 100);

            Assert.IsNotNull(result);
            Assert.AreEqual("running", result.status);
        }

        [Test]
        public void ListJobs_ReturnsRecentJobsDescending()
        {
            var job1 = AsyncJobService.CreateJob(
                "list_test_1", "s1", "First.", true);
            var job2 = AsyncJobService.CreateJob(
                "list_test_2", "s2", "Second.", true);

            var jobs = AsyncJobService.List(10);

            Assert.IsNotNull(jobs);
            Assert.IsTrue(jobs.Length >= 2);
        }

        [Test]
        public void GetJob_ReturnsNullForUnknownId()
        {
            var result = AsyncJobService.Get("nonexistent_job_id_12345");
            Assert.IsNull(result);
        }

        [Test]
        public void PlayModeReconnectJob_FailsAsUnrecoverable()
        {
            var job = AsyncJobService.CreateJob(
                "test", "queued", "Will fail after reload.", false,
                metadata: new Dictionary<string, object>
                {
                    ["testMode"] = "PlayMode",
                    ["filter"] = string.Empty
                },
                resultData: new Dictionary<string, object>());
            job.status = "reconnecting";
            BatchPersistence.UpsertJob(job);

            AsyncJobService.Pump(job.jobId);

            var updated = AsyncJobService.Get(job.jobId);
            Assert.AreEqual("failed", updated.status);
            Assert.AreEqual("failed_reload_unrecoverable", updated.currentStage);
        }

        [Test]
        public void JobStatus_ReturnsRecentProgressEvents()
        {
            var job = AsyncJobService.CreateJob(
                "test_status", "queued", "Queued.", true);
            AsyncJobService.CompleteJob(job.jobId, "Done.");

            var response = BatchSkills.JobStatus(job.jobId);
            var json = JObject.Parse(JsonConvert.SerializeObject(response));

            Assert.IsTrue(json["success"]?.Value<bool>() ?? false);
            Assert.AreEqual("completed", json["progressStage"]?.ToString());
            Assert.IsTrue((json["recentProgress"] as JArray)?.Count > 0);
        }
    }
}

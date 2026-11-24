using System.Collections.Concurrent;
using System.Text.Json;
using SesKayit.Core;
using Xunit;

namespace SesKayit.Tests;

public class ProcessingQueueTests
{
    [Fact]
    public async Task MixedJobsAreFifoNeverOverlapAndContinueAfterFailureWithDurableHistory()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new ConcurrentQueue<string>(); int active = 0, maximum = 0;
        var queue = new ProcessingQueue(root, async (job, report) =>
        {
            int now = Interlocked.Increment(ref active); maximum = Math.Max(maximum, now); order.Enqueue(job.Id);
            try
            {
                report("STT başladı · " + job.Id);
                if (job.Id == "first") { entered.TrySetResult(); await release.Task; throw new IOException("Servis kapalı"); }
                report("LLM özeti hazırlanıyor"); await Task.Yield(); report("Özet kaydedildi: " + job.Id + ".md");
            }
            finally { Interlocked.Decrement(ref active); }
        });
        try
        {
            queue.Enqueue(new() { Id = "first", Sources = ["one.m4a"] });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queue.Enqueue(new() { Id = "second", Kind = ProcessingKind.TextSummary, Sources = ["two.md"] });
            queue.Enqueue(new() { Id = "third", Kind = ProcessingKind.AudioSummary, Sources = ["three.m4a"] });
            Assert.False(queue.Enqueue(new() { Id = "second" }));
            Assert.Equal(2, queue.Snapshot().Count(j => j.Status == ProcessingStatus.Queued));
            Assert.Equal("STT", queue.Snapshot().Single(j => j.Id == "first").Phase);
            release.SetResult(); await queue.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { "first", "second", "third" }, order.ToArray()); Assert.Equal(1, maximum);
            var restored = new ProcessingQueue(root, (_, _) => throw new Exception("Completed and failed jobs must not run automatically"));
            restored.ResumeInterrupted(); await restored.Completion;
            Assert.Equal(1, restored.Snapshot().Count(j => j.Status == ProcessingStatus.Failed));
            Assert.Equal(2, restored.Snapshot().Count(j => j.Status == ProcessingStatus.Completed));
            Assert.Contains(restored.Snapshot().Single(j => j.Id == "second").Events, e => e.Message.StartsWith("LLM özeti"));
            Assert.Contains("second.md", restored.Snapshot().Single(j => j.Id == "second").Outputs);
        }
        finally { release.TrySetResult(); await queue.Completion; if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InterruptedJobsRecoverInStoredOrderOnlyOnceAndRetryRetainsEarlierEvents()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            foreach (var job in new[]
            {
                new ProcessingJob { Id = "running", Sequence = 2, Sources = ["b.md"], Status = ProcessingStatus.Running, Attempts = 1 },
                new ProcessingJob { Id = "waiting", Sequence = 1, Sources = ["a.md"], Status = ProcessingStatus.Queued },
                new ProcessingJob { Id = "failed", Sequence = 3, Sources = ["c.md"], Status = ProcessingStatus.Failed, Attempts = 1, Events = [new(DateTimeOffset.Now, "Önceki hata")] }
            }) File.WriteAllText(Path.Combine(root, job.Id + ".json"), JsonSerializer.Serialize(job));
            var order = new ConcurrentQueue<string>();
            var queue = new ProcessingQueue(root, (job, _) => { order.Enqueue(job.Id); return Task.CompletedTask; });
            queue.ResumeInterrupted(); queue.ResumeInterrupted(); await queue.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { "waiting", "running" }, order.ToArray());
            Assert.True(queue.Retry("failed", new() { LlmPrompt = "Güncel", LlmModel = "new-model" }, "Yeni profil"));
            await queue.Completion;
            var retried = queue.Snapshot().Single(j => j.Id == "failed");
            Assert.Equal(2, retried.Attempts); Assert.Equal("Güncel", retried.Settings.LlmPrompt);
            Assert.Contains(retried.Events, e => e.Message == "Önceki hata");
            Assert.False(queue.Retry("failed", new(), ""));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task QueuedInputsAndSettingsAreSnapshotsWhileLaterPromptsChange()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = new ConcurrentQueue<ProcessingJob>();
        var queue = new ProcessingQueue(root, async (job, _) => { await release.Task; seen.Enqueue(job); });
        try
        {
            string[] files = ["original.md"]; var settings = new PostProcessingSettings { LlmPrompt = "İlk prompt" };
            queue.Enqueue(new() { Sources = files, Settings = settings }); files[0] = "changed.md";
            settings = settings with { LlmPrompt = "Yeni prompt" }; queue.Enqueue(new() { Sources = ["next.md"], Settings = settings });
            var snapshot = queue.Snapshot(); snapshot[1].Sources[0] = "tampered.md";
            release.SetResult(); await queue.Completion;
            Assert.Equal("original.md", seen.First().Sources[0]); Assert.Equal("İlk prompt", seen.First().Settings.LlmPrompt);
            Assert.Equal("Yeni prompt", seen.Last().Settings.LlmPrompt);
        }
        finally { release.TrySetResult(); await queue.Completion; if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}

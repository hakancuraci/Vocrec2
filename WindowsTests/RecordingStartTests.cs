using NAudio.Wave;
using SesKayit.Core;
using Xunit;

namespace SesKayit.WindowsTests;

public class RecordingStartTests
{
    private static readonly ObservedDevice[] Devices = [
        new("speakers", "Hoparlör", false, true, false),
        new("default-mic", "Dahili mikrofon", true, true, false),
        new("idle-usb-mic", "USB mikrofon", true, false, false)
    ];
    private static ActivitySnapshot EmptyActivity => new([], [], [], []);

    [Fact]
    public Task FirstClickIsKeptWhileInitialDevicesLoadEvenIfActivityScanNeverFinishes() => OnUiThread(async () =>
    {
        var catalog = new TaskCompletionSource<ObservedDevice[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activity = new TaskCompletionSource<ActivitySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<RecordingRequest>();
        int reads = 0;
        string directory = Path.Combine(Path.GetTempPath(), "vocrec-start-" + Guid.NewGuid().ToString("N"));
        using var form = new MainForm(() => { reads++; return catalog.Task; }, () => activity.Task,
            request => { requests.Add(request); return new FakeSession(request); }, Path.Combine(directory, "settings.json"));
        try
        {
            var deviceRefresh = form.RefreshChannelsAsync();
            var activityRefresh = form.RefreshActivitiesAsync();
            var firstClick = form.StartAsync();
            Assert.False(firstClick.IsCompleted);
            catalog.SetResult(Devices);
            await Task.WhenAll(deviceRefresh, firstClick).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, reads); // Reuse the in-flight device read, rather than starting another scan.
            var request = Assert.Single(requests);
            Assert.True(request.Sources.Select(s => s.Id).ToHashSet().SetEquals(Devices.Select(d => d.Id)));
            Assert.False(activityRefresh.IsCompleted);
            await form.StopAsync();
            activity.SetResult(EmptyActivity);
            await activityRefresh;
        }
        finally { catalog.TrySetResult(Devices); activity.TrySetResult(EmptyActivity); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    });

    [Fact]
    public Task ExistingSourcesStartImmediatelyDuringAnotherDeviceAndActivityRefresh() => OnUiThread(async () =>
    {
        int reads = 0;
        var nextDevices = new TaskCompletionSource<ObservedDevice[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activity = new TaskCompletionSource<ActivitySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<RecordingRequest>();
        string directory = Path.Combine(Path.GetTempPath(), "vocrec-start-" + Guid.NewGuid().ToString("N"));
        using var form = new MainForm(() => ++reads == 1 ? Task.FromResult(Devices) : nextDevices.Task, () => activity.Task,
            request => { requests.Add(request); return new FakeSession(request); }, Path.Combine(directory, "settings.json"));
        try
        {
            await form.RefreshChannelsAsync();
            var refresh = form.RefreshChannelsAsync();
            Assert.False(refresh.IsCompleted);
            await form.StartAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Single(requests);
            Assert.False(nextDevices.Task.IsCompleted);
            Assert.False(activity.Task.IsCompleted);
            nextDevices.SetResult(Devices);
            await refresh;
            await form.StopAsync();
        }
        finally { nextDevices.TrySetResult(Devices); activity.TrySetResult(EmptyActivity); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    });

    [Fact]
    public Task RepeatedClicksDuringStartupDoNotCreateDuplicateRecordings() => OnUiThread(async () =>
    {
        var catalog = new TaskCompletionSource<ObservedDevice[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<RecordingRequest>();
        string directory = Path.Combine(Path.GetTempPath(), "vocrec-start-" + Guid.NewGuid().ToString("N"));
        using var form = new MainForm(() => catalog.Task, () => Task.FromResult(EmptyActivity),
            request => { requests.Add(request); return new FakeSession(request); }, Path.Combine(directory, "settings.json"));
        try
        {
            var firstClick = form.StartAsync();
            await form.StartAsync();
            catalog.SetResult(Devices);
            await firstClick.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(requests);
            await form.StopAsync();
        }
        finally { catalog.TrySetResult(Devices); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    });

    private static Task OnUiThread(Func<Task> assertion)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var app = new ApplicationContext();
                using var kickoff = new System.Windows.Forms.Timer { Interval = 1 };
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                kickoff.Tick += async (_, _) =>
                {
                    kickoff.Stop();
                    try { await assertion(); completed.TrySetResult(); }
                    catch (Exception ex) { completed.TrySetException(ex); }
                    finally { app.ExitThread(); }
                };
                kickoff.Start(); Application.Run(app);
            }
            catch (Exception ex) { completed.TrySetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completed.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private sealed class FakeSession : IRecordingSession
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool disposed;
        public FakeSession(RecordingRequest request)
        {
            Automatic = request.Automatic;
            OutputPath = Path.Combine(request.Folder, "test.m4a");
        }
        public bool Automatic { get; }
        public AudioMixer Mixer { get; } = new([new(new Silence(), false)]);
        public MixerInput[] Inputs => Mixer.CurrentInputs;
        public Task Completion => completion.Task;
        public string OutputPath { get; }
        public string PartialPath => OutputPath + ".partial";
        public Exception? CaptureError => null;
        public string? Warning => null;
        public void SyncSources(IEnumerable<DeviceChoice> choices) { }
        public Task StopAsync() { Mixer.Finish(); completion.TrySetResult(); return Task.CompletedTask; }
        public void Dispose() { if (disposed) return; disposed = true; completion.TrySetResult(); Mixer.Dispose(); }
    }

    private sealed class Silence : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        public int Read(float[] buffer, int offset, int count) { Array.Clear(buffer, offset, count); return count; }
    }
}

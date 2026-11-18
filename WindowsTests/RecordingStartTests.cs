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

    [Fact]
    public Task ElapsedCounterTracksMediaFramesFreezesAfterStopAndResetsOnRestart() => OnUiThread(async () =>
    {
        FakeSession? recording = null;
        string directory = Path.Combine(Path.GetTempPath(), "vocrec-clock-" + Guid.NewGuid().ToString("N"));
        using var form = new MainForm(() => Task.FromResult(Devices), () => Task.FromResult(EmptyActivity),
            request => recording = new FakeSession(request), Path.Combine(directory, "settings.json"));
        try
        {
            var clock = Assert.IsType<Label>(Descendants(form).Single(c => c.AccessibleName == "Kayıt süresi"));
            await form.StartAsync();
            Assert.Equal("00:00:00", clock.Text);
            var mixer = recording!.Mixer;
            await Task.Run(() => { var pcm = new byte[48000 * 4 * 2]; Assert.Equal(pcm.Length, mixer.Read(pcm, 0, pcm.Length)); });
            await form.RefreshRecordingStatusAsync();
            Assert.Equal("00:00:02", clock.Text);
            await form.StopAsync();
            await Task.Delay(1100);
            Assert.Equal("00:00:02", clock.Text);
            await form.StartAsync();
            Assert.Equal("00:00:00", clock.Text);
            await form.StopAsync();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    });

    [Theory]
    [InlineData(1f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public Task LargerClockAndHeaderFontsStayReadableAtMinimumWindowSize(float scale) => OnUiThread(() =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "vocrec-layout-" + Guid.NewGuid().ToString("N"));
        using var form = new MainForm(() => Task.FromResult(Devices), () => Task.FromResult(EmptyActivity),
            request => new FakeSession(request), Path.Combine(directory, "settings.json"));
        form.ShowInTaskbar = false;
        form.Show(); form.Size = form.MinimumSize;
        var controls = Descendants(form).ToArray();
        var clock = Assert.IsType<Label>(controls.Single(c => c.AccessibleName == "Kayıt süresi"));
        var title = Assert.IsType<Label>(controls.Single(c => c.AccessibleName == "Uygulama başlığı"));
        var tagline = Assert.IsType<Label>(controls.Single(c => c.AccessibleName == "Uygulama açıklaması"));
        using var clockFont = new Font(clock.Font.FontFamily, clock.Font.Size * scale, clock.Font.Style);
        using var titleFont = new Font(title.Font.FontFamily, title.Font.Size * scale, title.Font.Style);
        using var taglineFont = new Font(tagline.Font.FontFamily, tagline.Font.Size * scale, tagline.Font.Style);
        clock.Font = clockFont; title.Font = titleFont; tagline.Font = taglineFont;
        clock.Text = "123:45:56"; form.PerformLayout();
        foreach (var label in new[] { clock, title, tagline })
        {
            Assert.False(label.AutoEllipsis);
            Assert.True(label.ClientSize.Width >= label.PreferredSize.Width, $"Text width: {label.AccessibleName} ({scale}) actual={label.ClientSize} preferred={label.PreferredSize}");
            Assert.True(label.ClientSize.Height >= label.PreferredSize.Height, $"Text height: {label.AccessibleName} ({scale}) actual={label.ClientSize} preferred={label.PreferredSize}");
            Assert.True(form.ClientRectangle.Contains(form.RectangleToClient(label.RectangleToScreen(label.ClientRectangle))));
        }
        Assert.True(tagline.Top >= title.Bottom + 3);
        form.Close();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
        return Task.CompletedTask;
    });

    [Fact]
    public Task StyledQualitySelectionControlsRecordingAndPersists() => OnUiThread(async () =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "vocrec-quality-" + Guid.NewGuid().ToString("N"));
        string settingsFile = Path.Combine(directory, "settings.json");
        var requests = new List<RecordingRequest>();
        try
        {
            using (var form = new MainForm(() => Task.FromResult(Devices), () => Task.FromResult(EmptyActivity),
                request => { requests.Add(request); return new FakeSession(request); }, settingsFile))
            {
                form.ShowInTaskbar = false; form.Show(); await form.RefreshChannelsAsync();
                var selector = Descendants(form).Single(c => c.AccessibleName == "Kayıt ses kalitesi");
                selector.AccessibilityObject.Value = "128 kb/sn · Konuşma";
                await form.StartAsync();
                Assert.Equal(128000, Assert.Single(requests).Bitrate);
                await form.StopAsync(); form.Close();
            }
            using var restored = new MainForm(() => Task.FromResult(Devices), () => Task.FromResult(EmptyActivity),
                request => new FakeSession(request), settingsFile);
            Assert.Equal("128 kb/sn · Konuşma", Descendants(restored).Single(c => c.AccessibleName == "Kayıt ses kalitesi").AccessibilityObject.Value);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    });

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

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

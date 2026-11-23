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

    [Fact]
    public Task OutputFoldersSurviveCloseWithIncompleteLlmSettings() => OnUiThread(() =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "vocrec-folders-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "settings.json");
        try
        {
            using (var form = new MainForm(() => Task.FromResult(Devices), () => Task.FromResult(EmptyActivity), request => new FakeSession(request), file))
            {
                form.ShowInTaskbar = false; form.Show();
                Descendants(form).OfType<Button>().Single(c => c.Name == "ProcessingTab").PerformClick();
                var controls = Descendants(form).ToArray();
                ((TextBox)controls.Single(c => c.AccessibleName == "STT metin çıktı klasörü")).Text = Path.Combine(directory, "custom-text");
                ((TextBox)controls.Single(c => c.AccessibleName == "LLM özet çıktı klasörü")).Text = Path.Combine(directory, "custom-summary");
                ((TextBox)controls.Single(c => c.AccessibleName == "Servis URL")).Text = "incomplete-url";
                Assert.NotNull(form.Icon);
                form.Close(); // No explicit save click: this reproduced the reported bug.
            }
            using var restored = new MainForm(() => Task.FromResult(Devices), () => Task.FromResult(EmptyActivity), request => new FakeSession(request), file);
            var restoredControls = Descendants(restored).ToArray();
            Assert.Equal(Path.Combine(directory, "custom-text"), restoredControls.Single(c => c.AccessibleName == "STT metin çıktı klasörü").Text);
            Assert.Equal(Path.Combine(directory, "custom-summary"), restoredControls.Single(c => c.AccessibleName == "LLM özet çıktı klasörü").Text);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task MultipleLlmProfilesPersistSelectionAndSeparateEncryptedTokens() => OnUiThread(() =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "vocrec-profiles-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "settings.json");
        try
        {
            using (var form = new MainForm(() => Task.FromResult(Devices), () => Task.FromResult(EmptyActivity), request => new FakeSession(request), file))
            {
                form.ShowInTaskbar = false; form.Show();
                Descendants(form).OfType<Button>().Single(c => c.Name == "ProcessingTab").PerformClick();
                void Set(string label, string value) => ((TextBox)Descendants(form).Single(c => c.AccessibleName == label)).Text = value;
                Set("STT metin çıktı klasörü", Path.Combine(directory, "text"));
                Set("LLM özet çıktı klasörü", Path.Combine(directory, "summary"));
                Set("Profil adı", "Yerel Qwen"); Set("Servis URL", "http://localhost:8000"); Set("Model", "qwen-a");
                Set("Bearer token · isteğe bağlı", "fake-token-a"); Set("Özet prompt", "Birinci prompt");
                Descendants(form).OfType<Button>().Single(c => c.Text == "Ayarları kaydet").PerformClick();
                Descendants(form).OfType<Button>().Single(c => c.Name == "AddLlmProfile").PerformClick();
                Set("Profil adı", "Uzak GLM"); Set("Servis URL", "https://example.test/v1"); Set("Model", "glm-b");
                Set("Bearer token · isteğe bağlı", "fake-token-b"); Set("Özet prompt", "İkinci prompt");
                Descendants(form).OfType<Button>().Single(c => c.Text == "Ayarları kaydet").PerformClick();
                var picker = Descendants(form).Single(c => c.AccessibleName == "Kayıtlı LLM profili");
                picker.AccessibilityObject.Value = "Yerel Qwen";
                Assert.Equal("qwen-a", Descendants(form).Single(c => c.AccessibleName == "Model").Text);
                Assert.Equal("fake-token-a", Descendants(form).Single(c => c.AccessibleName == "Bearer token · isteğe bağlı").Text);
                form.Close();
            }
            string json = File.ReadAllText(file);
            Assert.DoesNotContain("fake-token-a", json); Assert.DoesNotContain("fake-token-b", json);
            using var restored = new MainForm(() => Task.FromResult(Devices), () => Task.FromResult(EmptyActivity), request => new FakeSession(request), file);
            var controls = Descendants(restored).ToArray();
            var profiles = (StudioChoice)controls.Single(c => c.AccessibleName == "Kayıtlı LLM profili");
            Assert.Equal(2, profiles.Items.Count); Assert.Equal("Yerel Qwen", profiles.AccessibilityObject.Value);
            Assert.Equal("Birinci prompt", controls.Single(c => c.AccessibleName == "Özet prompt").Text);
            profiles.AccessibilityObject.Value = "Uzak GLM";
            Assert.Equal("glm-b", controls.Single(c => c.AccessibleName == "Model").Text);
            Assert.Equal("fake-token-b", controls.Single(c => c.AccessibleName == "Bearer token · isteğe bağlı").Text);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LegacySingleLlmSettingsMigrateToDefaultProfile() => OnUiThread(() =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "vocrec-legacy-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            var legacy = new PostProcessingSettings { TranscriptFolder = Path.Combine(directory, "texts"), SummaryFolder = Path.Combine(directory, "summaries"), LlmUrl = "http://localhost", LlmModel = "legacy-model", LlmPrompt = "Eski prompt", ProtectedLlmBearerToken = LlmTokenProtection.Protect("legacy-fake-token") };
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(new { Processing = legacy }));
            using var form = new MainForm(() => Task.FromResult(Devices), () => Task.FromResult(EmptyActivity), request => new FakeSession(request), file);
            var controls = Descendants(form).ToArray();
            var picker = (StudioChoice)controls.Single(c => c.AccessibleName == "Kayıtlı LLM profili");
            Assert.Single(picker.Items); Assert.Equal("Varsayılan", picker.AccessibilityObject.Value);
            Assert.Equal("legacy-model", controls.Single(c => c.AccessibleName == "Model").Text);
            Assert.Equal("legacy-fake-token", controls.Single(c => c.AccessibleName == "Bearer token · isteğe bağlı").Text);
            Assert.Equal(legacy.TranscriptFolder, controls.Single(c => c.AccessibleName == "STT metin çıktı klasörü").Text);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("Kayıtlı LLM profili")]
    [InlineData("Kayıt ses kalitesi")]
    public Task DropdownCanOpenCancelReopenSelectAndDisposeWithItsWindow(string label) => OnUiThread(() =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "vocrec-dropdown-" + Guid.NewGuid().ToString("N"));
        var nativeErrors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        ThreadExceptionEventHandler errors = (_, e) => nativeErrors.Enqueue(e.Exception);
        Application.ThreadException += errors;
        try
        {
            using var form = new MainForm(() => Task.FromResult(Devices), () => Task.FromResult(EmptyActivity),
                request => new FakeSession(request), Path.Combine(directory, "settings.json"));
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = Point.Empty; form.TopMost = true;
            form.Show(); form.Activate(); form.BringToFront(); Application.DoEvents();
            if (label == "Kayıtlı LLM profili")
            {
                Descendants(form).OfType<Button>().Single(c => c.Name == "ProcessingTab").PerformClick();
                Descendants(form).OfType<Button>().Single(c => c.Name == "AddLlmProfile").PerformClick();
            }
            var choice = (StudioChoice)Descendants(form).Single(c => c.AccessibleName == label);
            var popup = Assert.IsType<ContextMenuStrip>(choice.ContextMenuStrip);
            int selected = choice.SelectedIndex, opening = 0, opened = 0;
            var closes = new List<string>();
            popup.Opening += (_, _) => opening++;
            popup.Opened += (_, _) => opened++;
            popup.Closed += (_, e) => closes.Add(e.CloseReason.ToString());
            void OpenPopup()
            {
                choice.Focus();
                // UI Automation invokes the same opening action as the arrow click,
                // without relying on physical cursor input on a headless runner.
                choice.AccessibilityObject.DoDefaultAction();
                Application.DoEvents();
                Assert.Empty(nativeErrors);
                Assert.True(popup.Visible, $"Dropdown must open: opening={opening}, opened={opened}, closed={string.Join(",", closes)}, handle={choice.IsHandleCreated}, visible={choice.Visible}.");
            }
            for (int attempt = 0; attempt < 5; attempt++)
            {
                OpenPopup();
                popup.Close(attempt % 2 == 0 ? ToolStripDropDownCloseReason.Keyboard : ToolStripDropDownCloseReason.AppClicked);
                Application.DoEvents();
                Assert.False(popup.IsDisposed); Assert.Equal(selected, choice.SelectedIndex);
            }
            OpenPopup();
            ((ToolStripMenuItem)popup.Items[0]).PerformClick();
            popup.Close(ToolStripDropDownCloseReason.ItemClicked); Application.DoEvents();
            Assert.Equal(0, choice.SelectedIndex); Assert.False(popup.IsDisposed);
            OpenPopup();
            form.Close(); Application.DoEvents();
            Assert.True(popup.IsDisposed);
            Assert.Empty(nativeErrors);
        }
        finally
        {
            Application.ThreadException -= errors;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LongPromptPasteCanReplaceExistingTextAndSurviveRestart(bool startWithSavedLongPrompt) => OnUiThread(() =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "vocrec-paste-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(directory, "settings.json");
        string longLines = string.Concat(Enumerable.Repeat("Talimat: Türkçe özetle; tüm kararları, aksiyonları ve tarihleri koru.\r\n", 1200));
        string longSingleLine = string.Concat(Enumerable.Repeat("Yeni uzun prompt: çğışöü ve ayrıntılı proje notları korunmalı. ", 1400));
        string finalPrompt = longSingleLine + "\r\n" + longLines;
        var nativeErrors = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        ThreadExceptionEventHandler errors = (_, e) => nativeErrors.Enqueue(e.Exception);
        IDataObject? previousClipboard = Clipboard.GetDataObject();
        Application.ThreadException += errors;
        Directory.CreateDirectory(directory);
        try
        {
            var initial = new PostProcessingSettings
            {
                TranscriptFolder = Path.Combine(directory, "texts"), SummaryFolder = Path.Combine(directory, "summaries"),
                LlmPrompt = startWithSavedLongPrompt ? longLines : ""
            };
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(new { Processing = initial }));
            using (var form = new MainForm(() => Task.FromResult(Devices), () => Task.FromResult(EmptyActivity),
                request => new FakeSession(request), file))
            {
                form.ShowInTaskbar = false; form.Show();
                Descendants(form).OfType<Button>().Single(c => c.Name == "ProcessingTab").PerformClick();
                Application.DoEvents();
                var prompt = (TextBox)Descendants(form).Single(c => c.AccessibleName == "Özet prompt");
                Assert.True(longLines.Length > 32767 && longSingleLine.Length > 32767);
                Assert.Equal(initial.LlmPrompt.Length, prompt.TextLength);
                IntPtr originalHandle = prompt.Handle;
                int destroyed = 0;
                prompt.HandleDestroyed += (_, _) => destroyed++;
                foreach (string payload in new[] { longLines, longSingleLine, "Kısa bir prompt.", "", finalPrompt })
                {
                    prompt.Focus(); prompt.SelectAll();
                    if (payload.Length == 0) prompt.Clear();
                    else
                    {
                        Clipboard.SetDataObject(payload, true, 10, 25);
                        prompt.Paste(); // Exercise the native WM_PASTE / EN_CHANGE path.
                    }
                    Application.DoEvents();
                    Assert.Empty(nativeErrors);
                    Assert.Equal(payload.Length, prompt.TextLength);
                    Assert.True(prompt.Text == payload, "Pasted prompt must remain intact.");
                    Assert.Equal(originalHandle, prompt.Handle);
                    Assert.Equal(0, destroyed);
                }
                form.Size = form.MinimumSize; Application.DoEvents();
                Assert.Equal(originalHandle, prompt.Handle);
                Assert.Equal(0, destroyed);
                form.Close();
            }
            using var restored = new MainForm(() => Task.FromResult(Devices), () => Task.FromResult(EmptyActivity),
                request => new FakeSession(request), file);
            restored.ShowInTaskbar = false; restored.Show();
            Descendants(restored).OfType<Button>().Single(c => c.Name == "ProcessingTab").PerformClick();
            Application.DoEvents();
            var reopenedPrompt = Descendants(restored).Single(c => c.AccessibleName == "Özet prompt");
            Assert.Equal(finalPrompt.Length, reopenedPrompt.Text.Length);
            Assert.True(reopenedPrompt.Text == finalPrompt, "Saved long prompt must survive restarting the form.");
            restored.Close(); Application.DoEvents();
            Assert.Empty(nativeErrors);
        }
        finally
        {
            Application.ThreadException -= errors;
            try
            {
                if (previousClipboard != null) Clipboard.SetDataObject(previousClipboard, true, 10, 25);
                else Clipboard.Clear();
            }
            catch (System.Runtime.InteropServices.ExternalException) { /* Best effort clipboard restoration. */ }
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public Task RecordingNameCanBeEditedDuringCaptureAndPersistsWithPendingJobs(bool enterDuringRecording, bool clearDuringRecording) => OnUiThread(async () =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "vocrec-record-name-" + Guid.NewGuid().ToString("N"));
        string settingsFile = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        var options = new PostProcessingSettings { TranscriptFolder = Path.Combine(directory, "text"), SummaryFolder = Path.Combine(directory, "summary") };
        File.WriteAllText(settingsFile, System.Text.Json.JsonSerializer.Serialize(new { Folder = directory, Processing = options }));
        const string title = "Haftalık Proje Toplantısı";
        string datedTitle = RecordingDates.WithPrefix(title, DateOnly.FromDateTime(DateTime.Now));
        FakeSession? recording = null;
        bool named = !clearDuringRecording;
        string? processedFile = null;
        try
        {
            // Existing content must survive; a new recording uses the next available name.
            File.WriteAllText(Path.Combine(directory, datedTitle + ".m4a"), "previous audio");
            using (var form = new MainForm(() => Task.FromResult(Devices), () => Task.FromResult(EmptyActivity),
                request => recording = new FakeSession(request), settingsFile,
                (file, _, _, useName) =>
                {
                    processedFile = file; Assert.Equal(named, useName);
                    return Task.FromException(new IOException("Simulated unavailable STT; leave the job pending."));
                }))
            {
                form.ShowInTaskbar = false; form.Show(); form.Size = form.MinimumSize;
                var input = Assert.IsType<TextBox>(Descendants(form).Single(c => c.AccessibleName == "Kayıt adı"));
                Assert.True(input.Visible);
                Assert.True(form.ClientRectangle.Contains(form.RectangleToClient(input.RectangleToScreen(input.ClientRectangle))));
                if (!enterDuringRecording) input.Text = title;
                await form.StartAsync(); Assert.True(input.Enabled);
                if (enterDuringRecording) input.Text = title;
                if (clearDuringRecording) input.Text = "   ";
                File.WriteAllText(recording!.OutputPath, "new audio");
                await form.StopAsync(); await form.ProcessingCompletion.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal("", input.Text); Assert.True(input.Enabled);
                Assert.Equal(named ? datedTitle + "_2.m4a" : RecordingDates.WithPrefix("test.m4a", DateOnly.FromDateTime(DateTime.Now)), Path.GetFileName(processedFile));
                Assert.Equal("new audio", File.ReadAllText(processedFile!));
                Assert.Equal("previous audio", File.ReadAllText(Path.Combine(directory, datedTitle + ".m4a")));
                using var pending = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(directory, "pending"), "*.json"))));
                Assert.Equal(named, pending.RootElement.GetProperty("UseRecordingName").GetBoolean());
                Assert.Equal(processedFile, pending.RootElement.GetProperty("Audio").GetString());
                form.Close();
            }
            var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var restored = new MainForm(() => Task.FromResult(Devices), () => Task.FromResult(EmptyActivity),
                request => new FakeSession(request), settingsFile,
                (file, _, _, useName) =>
                {
                    Assert.Equal(named, useName); Assert.Equal(processedFile, file);
                    resumed.SetResult(); return Task.CompletedTask;
                });
            restored.ShowInTaskbar = false; restored.Show();
            await resumed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await restored.ProcessingCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(Directory.GetFiles(Path.Combine(directory, "pending"), "*.json"));
            Assert.Equal("", Descendants(restored).Single(c => c.AccessibleName == "Kayıt adı").Text);
            restored.Close();
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
            OutputPath = Path.Combine(request.Folder, RecordingDates.WithPrefix("test.m4a", DateOnly.FromDateTime(DateTime.Now)));
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

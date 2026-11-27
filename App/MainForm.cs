using System.Diagnostics;
using System.Text.Json;
using SesKayit.Core;

namespace SesKayit;

public sealed partial class MainForm : Form
{
    private static readonly Color Bg = StudioTheme.Background;
    private readonly StudioSwitch automatic = new();
    private readonly ListBox outputApps = new ActivityFeed(), micApps = new ActivityFeed();
    private readonly Dictionary<DevicePicker, Label> emptySources = [];
    private readonly Label recordState = LabelText("● HAZIR", 9);
    private readonly ToolTip hints = new() { AutoPopDelay = 12000 };
    private readonly System.Windows.Forms.Timer discoveryTimer = new() { Interval = 1000 };
    private bool refreshingDevices, refreshingActivity, devicesLoaded, updatingDevices;
    private Task<ObservedDevice[]>? deviceRead;
    private ObservedDevice[] endpointSnapshot = [];
    private string? playbackDefaultId;
    private readonly Func<Task<ObservedDevice[]>> readDevices;
    private readonly Func<Task<ActivitySnapshot>> readActivity;
    private readonly Func<RecordingRequest, IRecordingSession> createRecording;
    private readonly Func<string, PostProcessingSettings, Action<string>, bool, Task> processRecording;
    private readonly DevicePicker outputs = new(), microphones = new();
    private readonly TextBox folder = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = StudioTheme.Field, ForeColor = StudioTheme.Ink };
    private readonly TextBox recordingName = new() { AccessibleName = "Kayıt adı", MaxLength = 120, PlaceholderText = "Örn. Haftalık proje toplantısı" };
    private readonly StudioChoice quality = new() { Dock = DockStyle.Fill };
    private readonly VolumeSlider systemGain = new(), micGain = new();
    private readonly LevelMeter systemMeter = new(), micMeter = new();
    private readonly StudioButton start = MakeButton("●  Kaydı başlat"), stop = MakeButton("■  Kaydı bitir");
    private readonly StudioButton browse = MakeButton("Kayıt klasörünü değiştir"), open = MakeButton("Klasörü görüntüle"), refresh = MakeButton("Yenile");
    private readonly Label status = LabelText("Hazır · Kaynakları seçip kaydı başlatın.");
    private readonly Label elapsed = LabelText("00:00:00", 34), size = LabelText("192 kb/sn · yaklaşık 86 MB / saat");
    private readonly Label peakInfo = LabelText("Seviyeler kayıt sırasında görünür.");
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    private readonly List<Control> configuration = [];
    private IRecordingSession? session;
    private bool busy, closingAllowed, closingRequested;
    private string? lastFile;
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vocrec2", "settings.json");
    private Settings settings = new();

    public MainForm() : this(
        () => Task.Run(AudioDeviceCatalog.Read),
        () => Task.Run(AudioActivityMonitor.Scan),
        request => new RecordingSession(request.Sources, request.Folder, request.Bitrate, request.SystemGain, request.MicGain, request.Automatic, request.EnhanceMicrophone))
    { }

    internal MainForm(Func<Task<ObservedDevice[]>> readDevices, Func<Task<ActivitySnapshot>> readActivity,
        Func<RecordingRequest, IRecordingSession> createRecording, string? settingsFile = null,
        Func<string, PostProcessingSettings, Action<string>, bool, Task>? processRecording = null,
        Func<ProcessingJob, Action<string>, Task>? processManual = null)
    {
        this.readDevices = readDevices;
        this.readActivity = readActivity;
        this.createRecording = createRecording;
        this.processRecording = processRecording ?? ProcessRecordingAsync;
        if (settingsFile != null) settingsPath = settingsFile;
        this.processManual = processManual ?? ProcessManualAsync;
        processingQueue = new ProcessingQueue(Path.Combine(Path.GetDirectoryName(settingsPath)!, "history"), ExecuteJobAsync);
        Text = $"Vocrec2 v{AppVersion.Number} · Ses kayıt stüdyosu";
        var windowIcon = (Icon)AppLogo.Icon.Clone(); Icon = windowIcon;
        Disposed += (_, _) => windowIcon.Dispose();
        BackColor = Bg; ForeColor = StudioTheme.Ink; Font = new Font("Segoe UI", 10);
        FormBorderStyle = FormBorderStyle.None;
        MinimumSize = new Size(960, 720); Size = new Size(1020, 760); StartPosition = FormStartPosition.CenterScreen;
        BuildLayout();
        InitializeProcessingControls();
        InitializeHistory();
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { InitialDirectory = folder.Text, Description = "Ses kayıtlarının kaydedileceği klasörü seçin", UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) { folder.Text = dialog.SelectedPath; hints.SetToolTip(folder, folder.Text); SaveSettings(); }
        };
        quality.Items.AddRange(["128 kb/sn · Konuşma", "160 kb/sn · Dengeli", "192 kb/sn · Yüksek kalite"]);
        quality.SelectedIndexChanged += (_, _) => size.Text = $"Yaklaşık {Bitrate() * 3600 / 8_000_000d:0} MB / saat";
        quality.SelectedIndex = 2;
        configuration.AddRange([folder, browse, quality]);

        LoadSettings();
        refresh.Click += async (_, _) => await RefreshChannelsAsync();
        automatic.CheckedChanged += async (_, _) => await ChangeAutomaticModeAsync();
        outputs.ItemChecksChanged += (_, _) => SourceSelectionChanged(outputSelection: true);
        microphones.ItemChecksChanged += (_, _) => SourceSelectionChanged(outputSelection: false);
        discoveryTimer.Tick += async (_, _) => await RefreshChannelsAsync();
        open.Click += (_, _) => { try { var path = folder.Text; Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { ShowError(ex); } };
        start.Click += async (_, _) => await StartAsync();
        stop.Click += async (_, _) => await StopAsync();
        systemGain.ValueChanged += (_, _) => { if (session != null) Volatile.Write(ref session.Mixer.SystemGain, systemGain.Value / 100f); };
        micGain.ValueChanged += (_, _) => { if (session != null) Volatile.Write(ref session.Mixer.MicrophoneGain, micGain.Value / 100f); };
        timer.Tick += async (_, _) => await RefreshRecordingStatusAsync();
        FormClosing += async (_, e) =>
        {
            if (closingAllowed) return;
            if (busy) { e.Cancel = true; return; }
            closingRequested = true;
            if (session != null)
            {
                e.Cancel = true; await StopAsync(); busy = true; start.Enabled = false; await processingTask; closingAllowed = true; Close();
            }
            else if (!processingTask.IsCompleted)
            {
                e.Cancel = true; busy = true; start.Enabled = false;
                status.Text = "Metin/özet işlemleri tamamlanıyor…";
                await processingTask; closingAllowed = true; SaveSettings(); Close();
            }
            else SaveSettings();
        };
        Shown += async (_, _) => { discoveryTimer.Start(); await RefreshChannelsAsync(); };
        UpdateSelectionControls();
    }

    internal async Task RefreshRecordingStatusAsync()
    {
        var current = session;
        if (current is null || busy) return;
        // Use one media-time snapshot, including when a minute/hour boundary is crossed.
        var duration = current.Mixer.Duration;
        DisplayDuration(duration);
        size.Text = $"{FileSize(current.PartialPath):0.0} MB kaydedildi · {Bitrate() / 1000} kb/sn";
        systemMeter.Value = Level(current, false); micMeter.Value = Level(current, true);
        bool limited = Interlocked.Read(ref current.Mixer.LimitedSamples) > 0;
        peakInfo.Text = limited ? "Tepe sınırına ulaşıldı. Daha temiz ses için seviyeleri azaltın." : "Kayıt sürüyor · seviyeleri canlı ayarlayabilirsiniz.";
        peakInfo.ForeColor = limited ? Color.FromArgb(255, 196, 110) : Color.Silver;
        if (systemGain.Value == 0 && endpointSnapshot.Any(d => !d.Microphone && d.PlaybackPeak > 0.001f))
        { peakInfo.Text = "Bilgisayar sesi seviyesi %0 · sesi kaydetmek için yükseltin."; peakInfo.ForeColor = Color.FromArgb(255, 196, 110); }
        if (current.Completion.IsCompleted) await StopAsync();
        else if (current.CaptureError != null) await RefreshChannelsAsync();
    }

    private void DisplayDuration(TimeSpan duration) => elapsed.Text = $"{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";

    internal async Task StartAsync()
    {
        if (busy || session != null) return;
        string path;
        try { if (string.IsNullOrWhiteSpace(folder.Text)) throw new IOException("Kayıt klasörünü seçin."); path = Path.GetFullPath(folder.Text); }
        catch (Exception ex) { ShowError(ex); return; }
        busy = true; recordingName.Enabled = false; SetRecording(true); stop.Enabled = false; recordState.Text = "● HAZIRLANIYOR"; status.Text = "Ses kaynakları açılıyor…";
        int bitrate = Bitrate(); float sg = systemGain.Value / 100f, mg = micGain.Value / 100f; bool auto = automatic.Checked;
        try
        {
            // Keep this click pending only for endpoint enumeration, never for application scans.
            // If endpoints are already displayed, start immediately even during a refresh.
            if (!devicesLoaded || deviceRead?.IsCompleted == true) ApplyDevices(await ReadDevicesAsync(refresh: true));
            var selected = SelectedSources();
            if (selected.Length == 0) throw new InvalidOperationException("Kaydedilecek ses kaynağı yok. Bir ses çıkışı veya mikrofon bağlayın ya da elle kaynak seçin.");
            SaveSettings();
            var request = new RecordingRequest(selected, path, bitrate, sg, mg, auto, settings.Processing.EnhanceMicrophone);
            session = await Task.Run(() => createRecording(request));
            lastFile = null; elapsed.Text = "00:00:00"; status.Text = "● Kayıt yapılıyor"; recordState.Text = "● KAYITTA"; timer.Start(); stop.Enabled = true;
        }
        catch (Exception ex) { SetRecording(false); status.Text = "Kayıt başlatılamadı."; ShowError(ex); }
        finally { busy = false; recordingName.Enabled = true; UpdateSelectionControls(); }
    }

    internal async Task StopAsync()
    {
        if (busy || session is null) return;
        string? chosenName = RecordingFileNames.Normalize(recordingName.Text);
        busy = true; recordingName.Enabled = false; timer.Stop(); stop.Enabled = false; status.Text = "Dosya tamamlanıyor…";
        var current = session;
        string captureWarning = "";
        try { await current.StopAsync(); lastFile = current.OutputPath; status.Text = "Kaydedildi: " + Path.GetFileName(lastFile); }
        catch (Exception ex)
        {
            if (File.Exists(current.OutputPath)) lastFile = current.OutputPath;
            status.Text = "Kayıt hatayla durdu.";
            ShowError(new IOException(ex.Message + (lastFile is null ? $"\nKısmi dosya (tamamlanmamış olabilir):\n{current.PartialPath}" : ""), ex));
        }
        finally
        {
            if (current.Mixer.Duration >= TimeSpan.FromSeconds(5) && Interlocked.Read(ref current.Mixer.NonSilentSamples) == 0)
                captureWarning = "Kayıt dosyası korundu ancak kaydedilen ses tamamen sessiz. Kulaklık çıkışını ve ses seviyelerini kontrol edin; STT/LLM başlatılmadı.";
            DisplayDuration(current.Mixer.Duration); current.Dispose(); session = null; busy = false; recordingName.Enabled = true; SetRecording(false); systemMeter.Value = micMeter.Value = 0; SaveSettings();
        }
        if (lastFile != null && File.Exists(lastFile))
        {
            if (chosenName != null)
            {
                try
                {
                    lastFile = RecordingFileNames.RenameCompleted(lastFile, chosenName, settings.Processing);
                    status.Text = "Kaydedildi: " + Path.GetFileName(lastFile);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                { ShowError(new IOException("Kayıt adı değiştirilemedi; ses özgün adıyla korundu: " + lastFile, ex)); }
            }
            QueueProcessing(lastFile, settings.Processing, useRecordingName: chosenName != null, captureWarning: captureWarning);
            if (captureWarning.Length > 0) status.Text = captureWarning;
            recordingName.Clear();
        }
    }

    private Task<ObservedDevice[]> ReadDevicesAsync(bool refresh = false)
    {
        if (deviceRead == null || (deviceRead.IsCompleted && (refresh || deviceRead.IsFaulted || deviceRead.IsCanceled))) deviceRead = readDevices();
        return deviceRead;
    }

    private void ApplyDevices(ObservedDevice[] devices)
    {
        updatingDevices = true;
        try
        {
            var chosen = AutomaticSources.Select(devices);
            string? nextDefault = devices.FirstOrDefault(d => !d.Microphone && d.PlaybackDefault)?.Id;
            HashSet<string>? manualOutputIds = null;
            if (!automatic.Checked && settings.FollowDefaultPlayback)
            {
                manualOutputIds = outputs.CheckedItems.Cast<DeviceChoice>().Select(d => d.Id).ToHashSet();
                if (!devicesLoaded) manualOutputIds.UnionWith(settings.Devices);
                bool follow = manualOutputIds.Contains(playbackDefaultId ?? "") || (!devicesLoaded &&
                    (settings.Devices.Length == 0 || manualOutputIds.Contains(nextDefault ?? ""))) || pendingDefaultFollow;
                if (follow && nextDefault != null)
                {
                    if (playbackDefaultId != nextDefault) manualOutputIds.Remove(playbackDefaultId ?? "");
                    manualOutputIds.Add(nextDefault);
                }
                pendingDefaultFollow = follow;
            }
            endpointSnapshot = devices;
            UpdateDevices(outputs, devices.Where(d => !d.Microphone), chosen, manualOutputIds);
            UpdateDevices(microphones, devices.Where(d => d.Microphone), chosen);
            playbackDefaultId = nextDefault ?? playbackDefaultId;
            devicesLoaded = true;
        }
        finally { updatingDevices = false; }
    }

    private async Task ChangeAutomaticModeAsync()
    {
        if (busy || IsDisposed) return;
        if (automatic.Checked)
        {
            settings.FollowDefaultPlayback = true; pendingDefaultFollow = true;
            updatingDevices = true;
            try
            {
                foreach (var picker in new[] { outputs, microphones })
                    for (int i = 0; i < picker.Items.Count; i++) picker.SetItemChecked(i, true);
            }
            finally { updatingDevices = false; }
        }
        UpdateSelectionControls(); SynchronizeRecordingSources(); SaveSettings();
        await RefreshChannelsAsync();
    }

    private bool pendingDefaultFollow;
    private void SourceSelectionChanged(bool outputSelection)
    {
        if (updatingDevices || busy || IsDisposed) return;
        if (outputSelection) { settings.FollowDefaultPlayback = false; pendingDefaultFollow = false; }
        // Explicit selection takes precedence over automatic discovery on later scans.
        if (automatic.Checked) { automatic.Checked = false; return; }
        SynchronizeRecordingSources(); SaveSettings();
    }

    private void SynchronizeRecordingSources()
    {
        var current = session;
        if (current == null || busy) return;
        try
        {
            current.Automatic = automatic.Checked;
            current.SyncSources(SelectedSources());
            status.Text = current.Warning ?? (current.Inputs.Length == 0
                ? "● Kayıt sürüyor · kaynak seçilene kadar sessizlik kaydediliyor"
                : $"● {(automatic.Checked ? "Otomatik" : "Elle")} kayıt · {current.Inputs.Length} kaynak");
            var selectedIds = SelectedSources().Select(d => d.Id).ToHashSet();
            var audibleDefault = endpointSnapshot.Where(d => !d.Microphone && d.PlaybackPeak > 0.001f && !selectedIds.Contains(d.Id))
                .OrderByDescending(d => d.PlaybackDefault).FirstOrDefault();
            if (audibleDefault != null && !SelectedSources().Any(d => d.Id == audibleDefault.Id))
                status.Text = "Windows sesi " + audibleDefault.Name + " çıkışında. Kaydetmek için bu çıkışı seçin veya otomatik keşfi açın.";
        }
        catch (Exception ex) { status.Text = "Kaynak değiştirilemedi; kayıt sürüyor: " + ex.Message; }
    }

    internal async Task RefreshChannelsAsync()
    {
        if (refreshingDevices || busy || IsDisposed) return;
        refreshingDevices = true;
        try
        {
            var devices = await ReadDevicesAsync(refresh: true);
            if (IsDisposed || busy) return;
            ApplyDevices(devices);
            var current = session;
            if (current != null) SynchronizeRecordingSources();
            else if (current == null && lastFile == null)
                status.Text = automatic.Checked ? "Hazır · Otomatik seçim açık; kayıt için Kaydı başlat'a basın." : "Hazır · Kaynakları elle seçebilirsiniz.";
        }
        catch (Exception ex) { status.Text = "Cihaz listesi okunamadı: " + ex.Message; }
        finally { refreshingDevices = false; }
        _ = RefreshActivitiesAsync();
    }

    internal async Task RefreshActivitiesAsync()
    {
        if (refreshingActivity || IsDisposed) return;
        refreshingActivity = true;
        try
        {
            var snapshot = await readActivity();
            if (IsDisposed) return;
            // Activity is informational. A slow/stale result must never change selections or start state.
            UpdateActivities(outputApps, snapshot.Outputs, "Ses üreten uygulama oturumu bekleniyor…");
            UpdateActivities(micApps, snapshot.Microphones, "Başka uygulamanın mikrofon oturumu yok.");
            if (snapshot.Warnings.Length > 0) hints.SetToolTip(outputApps, string.Join("\n", snapshot.Warnings));
        }
        catch (Exception ex)
        {
            if (!IsDisposed) UpdateActivities(outputApps, ["Uygulama bilgisi okunamadı; kayıt etkilenmez. " + ex.Message], "");
        }
        finally { refreshingActivity = false; }
    }

    private DeviceChoice[] SelectedSources() => outputs.CheckedItems.Cast<DeviceChoice>()
        .Concat(microphones.CheckedItems.Cast<DeviceChoice>()).Select(choice =>
        {
            var observed = endpointSnapshot.FirstOrDefault(d => d.Id == choice.Id);
            return choice with { PlaybackPeak = observed?.PlaybackPeak ?? 0, FormatSignature = observed?.FormatSignature ?? "" };
        }).ToArray();

    private void UpdateDevices(DevicePicker list, IEnumerable<ObservedDevice> observed, HashSet<string> automaticIds, HashSet<string>? manualIds = null)
    {
        var previous = list.CheckedItems.Cast<DeviceChoice>().Select(d => d.Id).ToHashSet();
        if (!devicesLoaded) previous.UnionWith(settings.Devices);
        if (manualIds != null && devicesLoaded) previous = manualIds;
        var choices = observed.Select(d => new DeviceChoice(d.Id,
            d.Name + (d.PlaybackDefault ? "  (Windows ses çıkışı)" : d.CommunicationDefault ? "  (iletişim varsayılanı)" : ""), d.Microphone)).ToArray();
        if (!devicesLoaded && settings.Devices.Length == 0) previous.UnionWith(choices.Select(c => c.Id));
        if (emptySources.TryGetValue(list, out var empty)) { empty.Visible = choices.Length == 0; list.Visible = choices.Length > 0; }
        var old = list.Items.Cast<DeviceChoice>().ToArray();
        if (!old.SequenceEqual(choices))
        {
            int top = list.TopIndex;
            list.BeginUpdate(); list.Items.Clear();
            foreach (var choice in choices) list.Items.Add(choice);
            if (list.Items.Count > 0) list.TopIndex = Math.Min(top, list.Items.Count - 1);
            list.EndUpdate();
        }
        for (int i = 0; i < choices.Length; i++)
            list.SetItemChecked(i, automatic.Checked ? automaticIds.Contains(choices[i].Id) : previous.Contains(choices[i].Id));
    }

    private static void UpdateActivities(ListBox list, string[] lines, string emptyText)
    {
        string[] display = lines.Length == 0 ? [emptyText] : lines;
        if (list.Items.Cast<string>().SequenceEqual(display)) return;
        list.BeginUpdate(); list.Items.Clear(); list.Items.AddRange(display); list.EndUpdate();
    }

    private void UpdateSelectionControls()
    {
        outputs.Enabled = microphones.Enabled = automatic.Enabled = refresh.Enabled = !busy;
    }
    private void SetRecording(bool value)
    {
        foreach (var control in configuration) control.Enabled = !value;
        UpdateSelectionControls();
        start.Enabled = !value; stop.Enabled = value;
        recordState.Text = value ? "● KAYITTA" : "● HAZIR";
        recordState.ForeColor = value ? StudioTheme.Mint : StudioTheme.Muted;
    }
    private int Bitrate() => new[] { 128000, 160000, 192000 }[Math.Clamp(quality.SelectedIndex, 0, 2)];
    private static double FileSize(string path) { try { return new FileInfo(path).Length / 1_000_000d; } catch { return 0; } }
    private static int Level(IRecordingSession current, bool mic) => (int)(Math.Clamp(current.Inputs.Where(i => i.Microphone == mic).Select(i => Volatile.Read(ref i.Peak)).DefaultIfEmpty().Max(), 0, 1) * 100);
    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, "Ses Kayıt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    private void LoadSettings()
    {
        try { if (File.Exists(settingsPath)) settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath)) ?? new(); } catch { settings = new(); }
        settings.Processing ??= new();
        InitializeLlmProfiles();
        folder.Text = settings.Folder; hints.SetToolTip(folder, folder.Text); automatic.Checked = settings.Automatic;
        // Preserve the previous 192 kb/s default when loading older index-based settings.
        int preferredBitrate = settings.Bitrate ?? (settings.Quality == 0 ? 128000 : 192000);
        quality.SelectedIndex = preferredBitrate switch { 128000 => 0, 160000 => 1, _ => 2 };
        systemGain.Value = Math.Clamp(settings.SystemGain, 0, 100); micGain.Value = Math.Clamp(settings.MicGain, 0, 100);
    }
    private void SaveSettings()
    {
        try
        {
            CaptureOutputPreferences();
            try { SaveCurrentProfileDraft(); }
            catch (ArgumentException ex) { status.Text = "LLM token kaydedilemedi: " + ex.Message; }
            settings.Folder = folder.Text; settings.Automatic = automatic.Checked; settings.Bitrate = Bitrate();
            settings.SystemGain = systemGain.Value; settings.MicGain = micGain.Value;
            settings.Devices = outputs.CheckedItems.Cast<DeviceChoice>().Concat(microphones.CheckedItems.Cast<DeviceChoice>()).Select(d => d.Id).ToArray();
            PersistSettings();
        }
        catch (Exception ex) { status.Text = "Ayarlar kaydedilemedi: " + ex.Message; }
    }
    protected override void Dispose(bool disposing) { if (disposing) { historyTimer.Dispose(); processingHub?.Dispose(); timer.Dispose(); discoveryTimer.Dispose(); hints.Dispose(); session?.Dispose(); } base.Dispose(disposing); }
    private static Label LabelText(string value, int fontSize = 10) => new() { Text = value, Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = StudioTheme.Muted, Font = new Font("Segoe UI", fontSize), AutoEllipsis = true, Margin = Padding.Empty };
    private static StudioButton MakeButton(string text) => new() { Text = text, Dock = DockStyle.Fill };

    private sealed class Settings
    {
        public Settings() { }
        public PostProcessingSettings Processing { get; set; } = new();
        public List<LlmProfile> LlmProfiles { get; set; } = [];
        public string SelectedLlmProfileId { get; set; } = "";
        public bool Automatic { get; set; } = true;
        public bool FollowDefaultPlayback { get; set; } = true;
        public string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "SesKayit");
        public int Quality { get; set; } = 1;
        public int? Bitrate { get; set; }
        public int SystemGain { get; set; } = 70;
        public int MicGain { get; set; } = 70;
        public string[] Devices { get; set; } = [];
    }
}

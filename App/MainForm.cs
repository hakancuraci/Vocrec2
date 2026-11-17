using System.Diagnostics;
using System.Text.Json;
using NAudio.CoreAudioApi;

namespace SesKayit;

public sealed class MainForm : Form
{
    private static readonly Color Bg = Color.FromArgb(17, 23, 35), PanelBg = Color.FromArgb(27, 36, 52);
    private readonly CheckedListBox outputs = DeviceList(), microphones = DeviceList();
    private readonly TextBox folder = new() { Dock = DockStyle.Fill };
    private readonly ComboBox quality = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TrackBar systemGain = GainSlider(), micGain = GainSlider();
    private readonly ProgressBar systemMeter = Meter(), micMeter = Meter();
    private readonly Button start = MakeButton("●  Kaydı başlat"), stop = MakeButton("■  Durdur ve kaydet");
    private readonly Label status = LabelText("Hazır · Kaynakları seçip kaydı başlatın.");
    private readonly Label elapsed = LabelText("00:00:00", 34), size = LabelText("192 kb/sn · yaklaşık 86 MB / saat");
    private readonly Label peakInfo = LabelText("Seviye göstergeleri kayıt sırasında çalışır.");
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    private readonly List<Control> configuration = [];
    private RecordingSession? session;
    private bool busy, closingAllowed;
    private string? lastFile;
    private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SesKayit", "settings.json");
    private Settings settings = new();

    public MainForm()
    {
        Text = "Ses Kayıt · Sistem + Mikrofon";
        BackColor = Bg; ForeColor = Color.White; Font = new Font("Segoe UI", 10);
        MinimumSize = new Size(820, 800); Size = new Size(1000, 900); StartPosition = FormStartPosition.CenterScreen;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 9 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 92, 74, 95, 55, 60, 42 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        Controls.Add(root);
        root.Controls.Add(LabelText("SES KAYIT", 25), 0, 0);
        root.Controls.Add(LabelText("Bilgisayarın sesi ve mikrofonların, tek bir kayıtta.\nWindows WASAPI  /  48 kHz stereo  /  AAC (.m4a)"), 0, 1);

        var sources = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        sources.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); sources.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        sources.Controls.Add(SourcePanel("01  BİLGİSAYAR SESİ", "YouTube / Teams'in kullandığı çıkışları işaretleyin.", outputs), 0, 0);
        sources.Controls.Add(SourcePanel("02  MİKROFONLAR", "Aynı anda birden fazla mikrofon seçebilirsiniz.", microphones), 1, 0);
        root.Controls.Add(sources, 0, 2);

        var levels = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        levels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); levels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        levels.Controls.Add(LevelPanel("Bilgisayar seviyesi", systemGain, systemMeter), 0, 0);
        levels.Controls.Add(LevelPanel("Mikrofon seviyesi", micGain, micMeter), 1, 0);
        root.Controls.Add(levels, 0, 3);

        var destination = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        destination.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); destination.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        destination.Controls.Add(LabelText("KAYIT KLASÖRÜ"), 0, 0);
        destination.Controls.Add(folder, 0, 1);
        var browse = MakeButton("Klasör seç…");
        browse.Click += (_, _) => { using var dialog = new FolderBrowserDialog { InitialDirectory = folder.Text, Description = "Kayıt klasörünü seçin", UseDescriptionForTitle = true }; if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath; };
        destination.Controls.Add(browse, 1, 1); root.Controls.Add(destination, 0, 4);

        quality.Items.AddRange(["128 kb/sn — Konuşma · ~58 MB/saat", "160 kb/sn — Dengeli · ~72 MB/saat", "192 kb/sn — Yüksek kalite · ~86 MB/saat"]);
        quality.SelectedIndexChanged += (_, _) => size.Text = $"{Bitrate() / 1000} kb/sn · yaklaşık {Bitrate() * 3600 / 8_000_000d:0} MB / saat";
        quality.SelectedIndex = 2;
        var encoding = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        encoding.Controls.Add(LabelText("SES KALİTESİ"), 0, 0); encoding.Controls.Add(quality, 0, 1);
        encoding.Controls.Add(LabelText("Tek stereo dosya • Sıkıştırarak anlık kayıt • Kayıt sırasında seviye ayarı"), 0, 2);
        root.Controls.Add(encoding, 0, 5);
        var info = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240)); info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        info.Controls.Add(elapsed); info.Controls.Add(size); root.Controls.Add(info, 0, 6);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        start.BackColor = Color.FromArgb(32, 131, 105); start.Width = 175; stop.Width = 185; stop.Enabled = false;
        var refresh = MakeButton("Cihazları yenile"); var open = MakeButton("Klasörü aç");
        actions.Controls.AddRange([start, stop, refresh, open]); root.Controls.Add(actions, 0, 7);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        footer.Controls.Add(status); footer.Controls.Add(peakInfo); root.Controls.Add(footer, 0, 8);
        configuration.AddRange([outputs, microphones, folder, browse, quality, refresh]);

        LoadSettings();
        refresh.Click += (_, _) => RefreshDevices();
        open.Click += (_, _) => { try { var path = lastFile is null ? folder.Text : Path.GetDirectoryName(lastFile)!; if (!Directory.Exists(path)) throw new IOException("Klasör henüz oluşturulmadı."); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { ShowError(ex); } };
        start.Click += async (_, _) => await StartAsync();
        stop.Click += async (_, _) => await StopAsync();
        systemGain.ValueChanged += (_, _) => { if (session != null) Volatile.Write(ref session.Mixer.SystemGain, systemGain.Value / 100f); };
        micGain.ValueChanged += (_, _) => { if (session != null) Volatile.Write(ref session.Mixer.MicrophoneGain, micGain.Value / 100f); };
        timer.Tick += async (_, _) =>
        {
            var current = session;
            if (current is null || busy) return;
            elapsed.Text = $"{(int)current.Mixer.Duration.TotalHours:00}:{current.Mixer.Duration.Minutes:00}:{current.Mixer.Duration.Seconds:00}";
            size.Text = $"{FileSize(current.PartialPath):0.0} MB · {Bitrate() / 1000} kb/sn";
            systemMeter.Value = Level(current, false); micMeter.Value = Level(current, true);
            bool limited = Interlocked.Read(ref current.Mixer.LimitedSamples) > 0;
            peakInfo.Text = limited ? "Tepe sınırına ulaşıldı. Daha temiz ses için seviyeleri azaltın." : "Kayıt sürüyor · Sistem ve mikrofon seviyelerini buradan ayarlayabilirsiniz.";
            peakInfo.ForeColor = limited ? Color.FromArgb(255, 196, 110) : Color.Silver;
            if (current.CaptureError != null || current.Completion.IsCompleted) await StopAsync();
        };
        FormClosing += async (_, e) =>
        {
            if (closingAllowed) return;
            if (busy) { e.Cancel = true; return; }
            if (session != null)
            {
                e.Cancel = true; await StopAsync(); closingAllowed = true; Close();
            }
            else SaveSettings();
        };
        Shown += (_, _) => RefreshDevices();
    }

    private async Task StartAsync()
    {
        if (busy || session != null) return;
        var selected = outputs.CheckedItems.Cast<DeviceChoice>().Concat(microphones.CheckedItems.Cast<DeviceChoice>()).ToArray();
        if (selected.Length == 0) { ShowError(new Exception("En az bir çıkış veya mikrofon seçin.")); return; }
        string path;
        try { if (string.IsNullOrWhiteSpace(folder.Text)) throw new IOException("Kayıt klasörünü seçin."); path = Path.GetFullPath(folder.Text); }
        catch (Exception ex) { ShowError(ex); return; }
        SaveSettings(); busy = true; SetRecording(true); stop.Enabled = false; status.Text = "Ses kaynakları açılıyor…";
        int bitrate = Bitrate(); float sg = systemGain.Value / 100f, mg = micGain.Value / 100f;
        try
        {
            session = await Task.Run(() => new RecordingSession(selected, path, bitrate, sg, mg));
            lastFile = null; elapsed.Text = "00:00:00"; status.Text = "● Kayıt yapılıyor"; timer.Start(); stop.Enabled = true;
        }
        catch (Exception ex) { SetRecording(false); status.Text = "Kayıt başlatılamadı."; ShowError(ex); }
        finally { busy = false; }
    }

    private async Task StopAsync()
    {
        if (busy || session is null) return;
        busy = true; timer.Stop(); stop.Enabled = false; status.Text = "Dosya tamamlanıyor…";
        var current = session;
        try { await current.StopAsync(); lastFile = current.OutputPath; status.Text = "Kaydedildi: " + Path.GetFileName(lastFile); }
        catch (Exception ex)
        {
            if (File.Exists(current.OutputPath)) lastFile = current.OutputPath;
            status.Text = "Kayıt hatayla durdu.";
            ShowError(new IOException(ex.Message + (lastFile is null ? $"\nKısmi dosya (tamamlanmamış olabilir):\n{current.PartialPath}" : ""), ex));
        }
        finally { current.Dispose(); session = null; busy = false; SetRecording(false); systemMeter.Value = micMeter.Value = 0; SaveSettings(); }
    }

    private void RefreshDevices()
    {
        try
        {
            var checkedIds = outputs.CheckedItems.Cast<DeviceChoice>().Concat(microphones.CheckedItems.Cast<DeviceChoice>()).Select(d => d.Id).ToHashSet();
            bool first = outputs.Items.Count + microphones.Items.Count == 0;
            if (first && settings.Devices.Length > 0) checkedIds.UnionWith(settings.Devices);
            using var enumerator = new MMDeviceEnumerator();
            outputs.Items.Clear(); microphones.Items.Clear();
            foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
            {
                string? defaultId = null;
                try { using var d = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia); defaultId = d.ID; } catch { /* A machine may have no default endpoint. */ }
                foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
                {
                    using (device)
                    {
                        bool mic = flow == DataFlow.Capture;
                        var list = mic ? microphones : outputs;
                        string label = device.FriendlyName + (device.ID == defaultId ? "  (varsayılan)" : "");
                        list.Items.Add(new DeviceChoice(device.ID, label, mic), checkedIds.Contains(device.ID) || (first && settings.Devices.Length == 0 && device.ID == defaultId));
                    }
                }
            }
            status.Text = "Hazır · Sesin yönlendirildiği çıkışları seçin.";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void SetRecording(bool value) { foreach (var control in configuration) control.Enabled = !value; start.Enabled = !value; stop.Enabled = value; }
    private int Bitrate() => new[] { 128000, 160000, 192000 }[Math.Clamp(quality.SelectedIndex, 0, 2)];
    private static double FileSize(string path) { try { return new FileInfo(path).Length / 1_000_000d; } catch { return 0; } }
    private static int Level(RecordingSession current, bool mic) => (int)(Math.Clamp(current.Inputs.Where(i => i.Microphone == mic).Select(i => Volatile.Read(ref i.Peak)).DefaultIfEmpty().Max(), 0, 1) * 100);
    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, "Ses Kayıt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    private void LoadSettings()
    {
        try { if (File.Exists(settingsPath)) settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath)) ?? new(); } catch { settings = new(); }
        folder.Text = settings.Folder;
        // Preserve the previous 192 kb/s default when loading older index-based settings.
        int preferredBitrate = settings.Bitrate ?? (settings.Quality == 0 ? 128000 : 192000);
        quality.SelectedIndex = preferredBitrate switch { 128000 => 0, 160000 => 1, _ => 2 };
        systemGain.Value = Math.Clamp(settings.SystemGain, 0, 100); micGain.Value = Math.Clamp(settings.MicGain, 0, 100);
    }
    private void SaveSettings()
    {
        try
        {
            settings = new Settings { Folder = folder.Text, Bitrate = Bitrate(), SystemGain = systemGain.Value, MicGain = micGain.Value,
                Devices = outputs.CheckedItems.Cast<DeviceChoice>().Concat(microphones.CheckedItems.Cast<DeviceChoice>()).Select(d => d.Id).ToArray() };
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings));
        }
        catch { /* Saving preferences must not interrupt a recording. */ }
    }
    protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); session?.Dispose(); } base.Dispose(disposing); }
    private static Label LabelText(string value, int fontSize = 10) => new() { Text = value, Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(210, 219, 232), Font = new Font("Segoe UI", fontSize), AutoEllipsis = true };
    private static Button MakeButton(string text) => new() { Text = text, Width = 130, Height = 40, FlatStyle = FlatStyle.Flat, BackColor = PanelBg, ForeColor = Color.White, Margin = new Padding(0, 5, 10, 5), Cursor = Cursors.Hand };
    private static CheckedListBox DeviceList() => new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, BackColor = PanelBg, ForeColor = Color.White, BorderStyle = BorderStyle.None, HorizontalScrollbar = true };
    private static TrackBar GainSlider() => new() { Minimum = 0, Maximum = 100, Value = 70, TickFrequency = 10, Dock = DockStyle.Fill, AutoSize = false, Height = 30 };
    private static ProgressBar Meter() => new() { Dock = DockStyle.Fill, Maximum = 100, Height = 8, Style = ProgressBarStyle.Continuous };
    private static Control SourcePanel(string title, string help, Control list)
    {
        var p = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = PanelBg, Margin = new Padding(0, 8, 12, 8), RowCount = 3, ColumnCount = 1 };
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); p.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        p.Controls.Add(LabelText(title, 12)); p.Controls.Add(LabelText(help, 9)); p.Controls.Add(list); return p;
    }
    private static Control LevelPanel(string title, TrackBar gain, ProgressBar meter)
    {
        var p = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(0, 0, 12, 8) };
        var label = LabelText(title + " · %70"); gain.ValueChanged += (_, _) => label.Text = $"{title} · %{gain.Value}";
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); p.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        p.Controls.Add(label); p.Controls.Add(gain); p.Controls.Add(meter); return p;
    }
    private sealed class Settings
    {
        public Settings() { }
        public string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "SesKayit");
        public int Quality { get; set; } = 1;
        public int? Bitrate { get; set; }
        public int SystemGain { get; set; } = 70;
        public int MicGain { get; set; } = 70;
        public string[] Devices { get; set; } = [];
    }
}

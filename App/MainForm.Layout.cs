using System.Runtime.InteropServices;

namespace SesKayit;

public sealed partial class MainForm
{
    private Action? updateTextLayout;
    private readonly Panel recordingPage = new() { Dock = DockStyle.Fill, AccessibleName = "Kayıt çalışma alanı" };
    private readonly Panel processingPage = new() { Dock = DockStyle.Fill, AccessibleName = "STT ve LLM çalışma alanı" };
    private readonly StudioButton recordingTab = MakeButton("Kayıt");
    private readonly StudioButton processingTab = MakeButton("STT / LLM ayarları");

    private void SelectWorkspace(bool processing)
    {
        recordingPage.Visible = !processing; processingPage.Visible = processing;
        (processing ? processingPage : recordingPage).BringToFront();
        recordingTab.Selected = !processing; processingTab.Selected = processing;
        recordingTab.AccessibleDescription = processing ? "Kayıt sekmesini aç" : "Seçili sekme";
        processingTab.AccessibleDescription = processing ? "Seçili sekme" : "STT ve LLM ayarlarını aç";
        recordingTab.Invalidate(); processingTab.Invalidate();
    }

    private void BuildLayout()
    {
        SuspendLayout();
        var root = Grid(1, 4); root.Padding = new Padding(22, 4, 22, 18);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        Controls.Add(root);
        AddWindowChrome(); root.BringToFront();

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
        var mark = new BrandMark { Location = new Point(0, 13) }; header.Controls.Add(mark);
        var name = Heading("Vocrec2", 25); name.Dock = DockStyle.None; name.AutoSize = true; name.AutoEllipsis = false;
        name.AccessibleName = "Uygulama başlığı"; name.Padding = new Padding(0, 3, 0, 3); header.Controls.Add(name);
        var tagline = LabelText("Sistem sesi ve mikrofon. Tek bir kayıtta.", 9); tagline.Dock = DockStyle.None; tagline.AutoSize = true; tagline.AutoEllipsis = false;
        tagline.AccessibleName = "Uygulama açıklaması"; header.Controls.Add(tagline);
        var tag = LabelText($"v{AppVersion.Number}\nCommit: {AppVersion.ShortCommit}", 9); tag.TextAlign = ContentAlignment.MiddleRight;
        tag.AccessibleName = "Uygulama sürümü";
        hints.SetToolTip(tag, $"Sürüm: {AppVersion.Number}\nKaynak commit: {AppVersion.Commit}\nRepodaki Version.props ve commit geçmişiyle karşılaştırabilirsiniz.");
        tag.Dock = DockStyle.Right; tag.Width = 205; header.Controls.Add(tag); root.Controls.Add(header, 0, 0);

        var automationCard = new StudioCard { Dock = DockStyle.Fill, Padding = new Padding(16, 8, 16, 8), Margin = new Padding(0, 8, 0, 10) };
        var automationGrid = Grid(2, 3);
        automationGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); automationGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        automationGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 21)); automationGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 17)); automationGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        automationGrid.Controls.Add(Heading("Otomatik kanal keşfi", 11), 0, 0);
        automationGrid.Controls.Add(LabelText("Tüm ses çıkışları ve mikrofonlar seçili gelir. Tek tıkla kayıt.", 9), 0, 1);
        automationGrid.Controls.Add(LabelText("Teams’te sessize almak yerel mikrofon kaydını durdurmaz.", 8), 0, 2);
        automationGrid.Controls.Add(automatic, 1, 0); automationGrid.SetRowSpan(automatic, 3);
        automationCard.Controls.Add(automationGrid);
        automationCard.Margin = new Padding(0, 0, 0, 10);

        var body = Grid(2, 1);
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        var channels = Grid(1, 2);
        channels.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); channels.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        channels.Controls.Add(ChannelCard("Bilgisayar sesi", "SİSTEM", "Seçili çıkışlardaki tüm sesler kaydedilir.", outputs, outputApps, systemGain, systemMeter), 0, 0);
        channels.Controls.Add(ChannelCard("Mikrofonlar", "GİRİŞ", "Tüm mikrofonlar hazır; elle seçim için otomatik modu kapatın.", microphones, micApps, micGain, micMeter), 0, 1);
        body.Controls.Add(channels, 0, 0);

        var recordingSettings = Grid(1, 2);
        recordingSettings.RowStyles.Add(new RowStyle(SizeType.Percent, 57)); recordingSettings.RowStyles.Add(new RowStyle(SizeType.Percent, 43));
        recordingSettings.Controls.Add(DestinationCard(), 0, 0); recordingSettings.Controls.Add(QualityCard(), 0, 1);
        body.Controls.Add(recordingSettings, 1, 0);
        var recordingStack = Grid(1, 2);
        recordingStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        recordingStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        recordingStack.Controls.Add(automationCard, 0, 0); recordingStack.Controls.Add(body, 0, 1);
        recordingPage.Controls.Add(recordingStack);
        processingPage.Controls.Add(ProcessingCard());
        var workspace = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        workspace.Controls.Add(recordingPage); workspace.Controls.Add(processingPage);
        root.Controls.Add(workspace, 0, 2);

        var navigation = Grid(2, 1);
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var segments = new StudioCard { Dock = DockStyle.Fill, Padding = new Padding(4), Margin = new Padding(0, 0, 0, 10) };
        var segmentGrid = Grid(2, 1);
        segmentGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38)); segmentGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        recordingTab.Name = "RecordingTab"; processingTab.Name = "ProcessingTab";
        recordingTab.AccessibleRole = processingTab.AccessibleRole = AccessibleRole.PageTab;
        recordingTab.AccessibleName = "Kayıt sekmesi"; processingTab.AccessibleName = "STT ve LLM ayarları sekmesi";
        recordingTab.Margin = new Padding(0, 0, 4, 0); processingTab.Margin = Padding.Empty;
        segmentGrid.Controls.Add(recordingTab, 0, 0); segmentGrid.Controls.Add(processingTab, 1, 0); segments.Controls.Add(segmentGrid);
        recordingTab.Click += (_, _) => SelectWorkspace(false); processingTab.Click += (_, _) => SelectWorkspace(true);
        recordingTab.KeyDown += (_, e) => { if (e.KeyCode == Keys.Right) { processingTab.Focus(); SelectWorkspace(true); e.Handled = true; } };
        processingTab.KeyDown += (_, e) => { if (e.KeyCode == Keys.Left) { recordingTab.Focus(); SelectWorkspace(false); e.Handled = true; } };
        navigation.Controls.Add(segments, 0, 0);
        var flowHint = LabelText("Kayıt  →  Metin  →  Özet", 9); flowHint.TextAlign = ContentAlignment.MiddleRight;
        flowHint.Margin = new Padding(10, 0, 0, 10); navigation.Controls.Add(flowHint, 1, 0);
        root.Controls.Add(navigation, 0, 1);
        SelectWorkspace(false);

        var recording = new StudioCard { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0), Padding = new Padding(16, 10, 16, 8) };
        var recordingGrid = Grid(1, 2);
        recordingGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); recordingGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        var transport = Grid(5, 1);
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174));
        elapsed.Font = new Font("Consolas", 27); elapsed.ForeColor = StudioTheme.Ink;
        elapsed.AutoSize = true; elapsed.AutoEllipsis = false;
        elapsed.Padding = new Padding(4, 2, 18, 2); elapsed.AccessibleName = "Kayıt süresi";
        transport.Controls.Add(elapsed, 0, 0);
        recordState.ForeColor = StudioTheme.Muted; transport.Controls.Add(recordState, 1, 0);
        refresh.Font = new Font("Segoe UI", 9); refresh.Margin = new Padding(0, 5, 8, 5); transport.Controls.Add(refresh, 2, 0);
        stop.Margin = new Padding(0, 5, 8, 5); stop.Enabled = false; transport.Controls.Add(stop, 3, 0);
        start.Primary = true; start.Margin = new Padding(0, 5, 0, 5); transport.Controls.Add(start, 4, 0);
        recordingGrid.Controls.Add(transport, 0, 0); status.Font = new Font("Segoe UI", 9); recordingGrid.Controls.Add(status, 0, 1);
        recording.Controls.Add(recordingGrid); root.Controls.Add(recording, 0, 3);
        hints.SetToolTip(browse, "Yeni kayıtların kaydedileceği klasörü seçer.");
        hints.SetToolTip(open, "Seçili kayıt klasörünü Dosya Gezgini’nde açar. Kayıt konumunu değiştirmez.");
        hints.SetToolTip(automatic, "Tüm ses çıkışlarını ve mikrofonları seçer. Uygulama taraması kayıt başlatmayı engellemez.");
        bool updating = false;
        void FitTextLayout()
        {
            if (updating || IsDisposed) return;
            updating = true;
            try
            {
                name.Location = new Point(56, 5);
                tagline.Location = new Point(58, name.Bottom + 3);
                int headerHeight = Math.Max(72, tagline.Bottom + 9);
                if (root.RowStyles[0].Height != headerHeight) root.RowStyles[0].Height = headerHeight;
                mark.Top = Math.Max(5, (headerHeight - mark.Height) / 2);
                int footerHeight = Math.Max(108, elapsed.PreferredHeight + status.PreferredHeight + recording.Padding.Vertical + recording.Margin.Vertical + 10);
                if (root.RowStyles[3].Height != footerHeight) root.RowStyles[3].Height = footerHeight;
            }
            finally { updating = false; }
        }
        updateTextLayout = FitTextLayout;
        name.SizeChanged += (_, _) => FitTextLayout();
        tagline.SizeChanged += (_, _) => FitTextLayout();
        elapsed.FontChanged += (_, _) => FitTextLayout();
        status.FontChanged += (_, _) => FitTextLayout();
        Shown += (_, _) => FitTextLayout();
        FitTextLayout();
        ResumeLayout(true);
    }

    private Control DestinationCard()
    {
        var card = new StudioCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(16, 12, 16, 12) };
        var grid = Grid(1, 6);
        foreach (int h in new[] { 25, 20, 40, 42, 32 }) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.Controls.Add(Heading("Kayıt konumu"), 0, 0);
        grid.Controls.Add(LabelText("Ses dosyaları bu klasöre kaydedilir.", 9), 0, 1);
        var path = new StudioCard { Dock = DockStyle.Fill, BackColor = StudioTheme.Field, Padding = new Padding(10, 10, 10, 6), Margin = new Padding(0, 0, 0, 6) };
        folder.Font = new Font("Segoe UI", 9); folder.AccessibleName = "Seçili kayıt klasörü"; path.Controls.Add(folder); grid.Controls.Add(path, 0, 2);
        browse.Accent = true; browse.Margin = new Padding(0, 2, 0, 5); grid.Controls.Add(browse, 0, 3);
        open.Font = new Font("Segoe UI", 9); open.Margin = new Padding(0, 0, 0, 2); grid.Controls.Add(open, 0, 4);
        grid.Controls.Add(LabelText("Görüntülemek konumu değiştirmez.", 8), 0, 5);
        card.Controls.Add(grid); return card;
    }

    private Control QualityCard()
    {
        var card = new StudioCard { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(16, 12, 16, 10) };
        var grid = Grid(1, 5);
        foreach (int h in new[] { 28, 36, 26, 24 }) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.Controls.Add(Heading("Ses kalitesi"), 0, 0);
        quality.Font = new Font("Segoe UI", 10); quality.AccessibleName = "Kayıt ses kalitesi";
        grid.Controls.Add(quality, 0, 1);
        size.Font = new Font("Segoe UI", 9); size.ForeColor = StudioTheme.Purple; grid.Controls.Add(size, 0, 2);
        grid.Controls.Add(LabelText("AAC / .m4a     ·     48 kHz stereo", 9), 0, 3);
        peakInfo.Font = new Font("Segoe UI", 8); grid.Controls.Add(peakInfo, 0, 4);
        card.Controls.Add(grid); return card;
    }

    private Control ChannelCard(string title, string kind, string description, DevicePicker devices, ListBox activity, VolumeSlider gain, LevelMeter meter)
    {
        var card = new StudioCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 10), Padding = new Padding(16, 10, 16, 10) };
        var grid = Grid(1, 5);
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 27)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
        var heading = Grid(2, 1);
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        heading.Controls.Add(Heading(title), 0, 0);
        var badge = LabelText(kind, 8); badge.ForeColor = StudioTheme.Purple; badge.TextAlign = ContentAlignment.MiddleRight; heading.Controls.Add(badge, 1, 0);
        grid.Controls.Add(heading, 0, 0); grid.Controls.Add(LabelText(description, 9), 0, 1);
        var deviceHost = new Panel { Dock = DockStyle.Fill };
        deviceHost.Controls.Add(devices);
        var empty = LabelText("Ses aygıtı bekleniyor…", 9); empty.ForeColor = StudioTheme.Muted;
        deviceHost.Controls.Add(empty); empty.BringToFront(); emptySources[devices] = empty;
        grid.Controls.Add(deviceHost, 0, 2); grid.Controls.Add(activity, 0, 3);
        var levels = Grid(1, 3);
        levels.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); levels.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); levels.RowStyles.Add(new RowStyle(SizeType.Absolute, 7));
        var captions = Grid(2, 1);
        captions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); captions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        captions.Controls.Add(LabelText("Kayıt seviyesi", 8), 0, 0);
        var caption = LabelText($"%{gain.Value}", 8); caption.TextAlign = ContentAlignment.MiddleRight; captions.Controls.Add(caption, 1, 0);
        gain.AccessibleName = title + " kayıt seviyesi";
        gain.ValueChanged += (_, _) => caption.Text = $"%{gain.Value}";
        levels.Controls.Add(captions, 0, 0); levels.Controls.Add(gain, 0, 1); levels.Controls.Add(meter, 0, 2);
        grid.Controls.Add(levels, 0, 4); card.Controls.Add(grid); return card;
    }

    private static TableLayoutPanel Grid(int columns, int rows)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent };
        if (rows == 1) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return grid;
    }
    private static Label Heading(string text, int size = 12)
    {
        var label = LabelText(text, size); label.ForeColor = StudioTheme.Ink; label.Font = new Font("Segoe UI", size, FontStyle.Bold); return label;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        updateTextLayout?.Invoke();
    }

    private void AddWindowChrome()
    {
        var chrome = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(22, 0, 8, 0), BackColor = Bg };
        var caption = LabelText("Vocrec2  /  Ses kayıt stüdyosu", 8); caption.ForeColor = StudioTheme.Muted;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 114, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        foreach (var text in new[] { "−", "□", "×" })
        {
            var b = MakeButton(text); b.Dock = DockStyle.None; b.Width = 34; b.Height = 25; b.Margin = new Padding(2, 3, 2, 0); b.Font = new Font("Segoe UI", 12);
            b.AccessibleName = text == "×" ? "Kapat" : text == "−" ? "Küçült" : "Büyüt veya geri yükle";
            b.Click += (_, _) => { if (text == "×") Close(); else if (text == "−") WindowState = FormWindowState.Minimized; else ToggleMaximize(); };
            buttons.Controls.Add(b);
        }
        chrome.Controls.Add(caption); chrome.Controls.Add(buttons); caption.BringToFront();
        chrome.MouseDown += DragWindow; caption.MouseDown += DragWindow;
        chrome.DoubleClick += (_, _) => ToggleMaximize(); caption.DoubleClick += (_, _) => ToggleMaximize();
        Controls.Add(chrome);
    }
    private void ToggleMaximize()
    {
        MaximizedBounds = Screen.FromControl(this).WorkingArea;
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    }
    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
    }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != 0x84 || WindowState != FormWindowState.Normal || m.Result != (IntPtr)1) return;
        long packed = m.LParam.ToInt64();
        var p = PointToClient(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
        bool left = p.X < 6, right = p.X >= Width - 6, top = p.Y < 6, bottom = p.Y >= Height - 6;
        int hit = top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 1;
        m.Result = (IntPtr)hit;
    }
}

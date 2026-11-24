using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using CheckBoxState = System.Windows.Forms.VisualStyles.CheckBoxState;
using SesKayit.Core;

namespace SesKayit;

internal sealed class ProcessingHubForm : Form
{
    private readonly ProcessingQueue queue;
    private readonly Func<LibraryRequest, int> enqueue;
    private readonly Func<ProcessingJob, bool> retry, regenerate;
    private readonly Func<string> profileCaption;
    private readonly string preferencesPath;
    private sealed record LibraryPreferences(string TextFolder, string AudioFolder);
    private LibraryPreferences preferences;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 700 };
    private readonly Label overview = Label(""), profile = Label(""), note = Label(""), selection = Label("Dosyaları işaretleyin; tek özet için bölüm sırasını aşağıdaki oklarla değiştirebilirsiniz.");
    private readonly StudioButton historyTab = Button("Geçmiş / Kuyruk"), textTab = Button("Metinler (.md)"), audioTab = Button("Ses kayıtları");
    private readonly TableLayoutPanel historyPage = Grid(1, 3), libraryPage = Grid(1, 6);
    private readonly ListView history = List("İşlem geçmişi", false, ("İşlem", 245), ("Durum", 100), ("Aşama", 135), ("Profil", 125), ("Kuyruğa giriş", 130), ("Süre", 65));
    private readonly ListView files = List("Arşiv dosyaları", true, ("Dosya / bölüm sırası", 470), ("Değiştirilme", 145), ("Boyut", 85));
    private readonly TextBox details = new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = int.MaxValue, BorderStyle = BorderStyle.None, BackColor = StudioTheme.Card, ForeColor = StudioTheme.Ink, Dock = DockStyle.Fill, AccessibleName = "İşlem ayrıntıları" };
    private readonly TextBox folder = new() { AccessibleName = "Arşiv klasörü" }, title = new() { AccessibleName = "Yeni özet adı", MaxLength = 120, PlaceholderText = "İsteğe bağlı; boşsa LLM adlandırır" };
    private readonly StudioButton retryButton = Button("Hatayı yeniden dene"), newButton = Button("Güncel promptla yeni özet"), openButton = Button("Çıktıyı aç"), openFolder = Button("Klasörü aç");
    private readonly StudioButton openText = Button("Metni aç");
    private readonly StudioButton single = Button("Ayrı ayrı özetle"), combined = Button("Seçilenlerden tek özet");
    private CancellationTokenSource? scanCancellation;
    private long lastRevision = -1;
    private bool audioMode, loading;
    private int page;

    internal ProcessingHubForm(ProcessingQueue queue, string textFolder, string audioFolder, string preferencesPath,
        Func<LibraryRequest, int> enqueue, Func<ProcessingJob, bool> retry, Func<ProcessingJob, bool> regenerate, Func<string> profileCaption)
    {
        this.queue = queue; this.enqueue = enqueue; this.retry = retry; this.regenerate = regenerate; this.profileCaption = profileCaption; this.preferencesPath = preferencesPath;
        preferences = new(textFolder, audioFolder);
        try { if (File.Exists(preferencesPath)) preferences = JsonSerializer.Deserialize<LibraryPreferences>(File.ReadAllText(preferencesPath)) ?? preferences; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { note.Text = "Arşiv klasörleri okunamadı: " + ex.Message; }
        Text = "Vocrec2 · İşlemler ve arşiv"; AccessibleName = "İşlemler ve arşiv penceresi";
        BackColor = StudioTheme.Background; ForeColor = StudioTheme.Ink; Font = new Font("Segoe UI", 9);
        Size = new Size(980, 680); MinimumSize = new Size(900, 620); StartPosition = FormStartPosition.CenterParent;
        var icon = (Icon)AppLogo.Icon.Clone(); Icon = icon; Disposed += (_, _) => icon.Dispose();
        var root = Grid(1, 4); root.Padding = new Padding(18);
        root.RowStyles.Add(new(SizeType.Absolute, 72)); root.RowStyles.Add(new(SizeType.Absolute, 48)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 28)); Controls.Add(root);
        var heading = Grid(1, 2); heading.RowStyles.Add(new(SizeType.Absolute, 34)); heading.RowStyles.Add(new(SizeType.Percent, 100));
        var name = Label("İşlemler ve arşiv"); name.Font = new Font("Segoe UI", 19, FontStyle.Bold); heading.Controls.Add(name, 0, 0); heading.Controls.Add(overview, 0, 1); root.Controls.Add(heading, 0, 0);
        var navigation = Flow(historyTab, textTab, audioTab); foreach (var button in new[] { historyTab, textTab, audioTab }) button.Width = 165;
        root.Controls.Add(navigation, 0, 1);
        var body = new Panel { Dock = DockStyle.Fill }; body.Controls.Add(historyPage); body.Controls.Add(libraryPage); root.Controls.Add(body, 0, 2);
        root.Controls.Add(note, 0, 3); note.ForeColor = StudioTheme.Purple; note.AccessibleName = "İşlem bildirimi";
        BuildHistory(); BuildLibrary();
        historyTab.Click += (_, _) => SelectPage(0);
        textTab.Click += async (_, _) => { SelectPage(1); await LoadLibraryAsync(false); };
        audioTab.Click += async (_, _) => { SelectPage(2); await LoadLibraryAsync(true); };
        history.SelectedIndexChanged += (_, _) => UpdateDetails();
        files.ItemChecked += (_, _) => UpdateSelection();
        retryButton.Click += (_, _) => Act(() => SelectedJob() is { } job && retry(job) ? "İş yeniden kuyruğa eklendi; tamamlanan adımlar korunur." : "Yeniden denenecek hatalı bir iş seçin.");
        newButton.Click += (_, _) => Act(() => SelectedJob() is { } job && regenerate(job) ? "Güncel promptla yeni özet kuyruğa eklendi." : "Bir işlem seçin.");
        openButton.Click += (_, _) => Act(() => { OpenSelected(false); return "Çıktı açıldı."; });
        openText.Click += (_, _) => Act(() =>
        {
            string path = SelectedJob()?.Outputs.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Metin henüz oluşmadı.");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); return "Metin açıldı.";
        });
        openFolder.Click += (_, _) => Act(() => { OpenSelected(true); return "Klasör açıldı."; });
        single.Click += (_, _) => Submit(false); combined.Click += (_, _) => Submit(true);
        folder.Leave += (_, _) => SavePreferences();
        timer.Tick += (_, _) => RefreshHistory();
        Shown += (_, _) => { RefreshHistory(); timer.Start(); };
        SelectPage(0); RefreshHistory();
    }

    private void BuildHistory()
    {
        historyPage.RowStyles.Add(new(SizeType.Percent, 54)); historyPage.RowStyles.Add(new(SizeType.Percent, 46)); historyPage.RowStyles.Add(new(SizeType.Absolute, 48));
        historyPage.Controls.Add(history, 0, 0);
        var card = new StudioCard { Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 6), Padding = new Padding(12) };
        details.Font = new Font("Segoe UI", 9); card.Controls.Add(details); historyPage.Controls.Add(card, 0, 1);
        retryButton.Width = 158; newButton.Width = 222; openButton.Width = 120; openFolder.Width = 120;
        openText.Width = 112;
        historyPage.Controls.Add(Flow(retryButton, newButton, openText, openButton, openFolder), 0, 2);
    }

    private void BuildLibrary()
    {
        foreach (int height in new[] { 44, 26 }) libraryPage.RowStyles.Add(new(SizeType.Absolute, height));
        libraryPage.RowStyles.Add(new(SizeType.Percent, 100));
        foreach (int height in new[] { 30, 44, 48 }) libraryPage.RowStyles.Add(new(SizeType.Absolute, height));
        var path = Grid(3, 1); path.ColumnStyles.Add(new(SizeType.Percent, 100)); path.ColumnStyles.Add(new(SizeType.Absolute, 92)); path.ColumnStyles.Add(new(SizeType.Absolute, 86));
        path.Controls.Add(new StudioTextField(folder) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 4) }, 0, 0);
        var browse = Button("Klasör seç"); var refresh = Button("Yenile"); path.Controls.Add(browse, 1, 0); path.Controls.Add(refresh, 2, 0); libraryPage.Controls.Add(path, 0, 0);
        browse.Click += async (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { InitialDirectory = folder.Text, Description = "Listelenecek kaynak klasörü", UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) { folder.Text = dialog.SelectedPath; await LoadLibraryAsync(audioMode, folder.Text); }
        };
        refresh.Click += async (_, _) => await LoadLibraryAsync(audioMode, folder.Text);
        libraryPage.Controls.Add(profile, 0, 1); profile.ForeColor = StudioTheme.Purple;
        libraryPage.Controls.Add(files, 0, 2); libraryPage.Controls.Add(selection, 0, 3);
        var naming = Grid(2, 1); naming.ColumnStyles.Add(new(SizeType.Absolute, 152)); naming.ColumnStyles.Add(new(SizeType.Percent, 100));
        naming.Controls.Add(Label("Özet adı (isteğe bağlı)"), 0, 0); naming.Controls.Add(new StudioTextField(title) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4) }, 1, 0); libraryPage.Controls.Add(naming, 0, 4);
        var up = Button("↑ Yukarı"); var down = Button("↓ Aşağı"); var all = Button("Tümünü seç");
        up.Width = down.Width = 86; all.Width = 108; single.Width = 160; combined.Width = 204; combined.Primary = true;
        libraryPage.Controls.Add(Flow(up, down, all, single, combined), 0, 5);
        up.Click += (_, _) => MoveSelected(-1); down.Click += (_, _) => MoveSelected(1);
        all.Click += (_, _) => { bool select = files.CheckedItems.Count != files.Items.Count; foreach (ListViewItem item in files.Items) item.Checked = select; };
    }

    private void SelectPage(int selected)
    {
        page = selected; historyPage.Visible = selected == 0; libraryPage.Visible = selected != 0;
        (selected == 0 ? historyPage : libraryPage).BringToFront();
        historyTab.Selected = selected == 0; textTab.Selected = selected == 1; audioTab.Selected = selected == 2;
        historyTab.Invalidate(); textTab.Invalidate(); audioTab.Invalidate();
        if (selected == 0) RefreshHistory();
    }

    internal async Task LoadLibraryAsync(bool audio, string? chosenFolder = null)
    {
        if (IsDisposed) return;
        if (page != (audio ? 2 : 1)) SelectPage(audio ? 2 : 1);
        audioMode = audio; folder.Text = chosenFolder ?? (audio ? preferences.AudioFolder : preferences.TextFolder); SavePreferences();
        scanCancellation?.Cancel(); var cancellation = new CancellationTokenSource(); scanCancellation = cancellation;
        var checkedPaths = files.CheckedItems.Cast<ListViewItem>().Select(i => ((LibraryFile)i.Tag!).Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        files.Items.Clear();
        loading = true; UpdateSelection(); note.Text = "Klasör taranıyor…";
        try
        {
            var entries = await RecordingLibrary.ScanAsync(folder.Text, audio, cancellation.Token);
            if (IsDisposed || cancellation.IsCancellationRequested) return;
            files.BeginUpdate(); files.Items.Clear();
            foreach (var entry in entries)
            {
                var item = new ListViewItem(new[] { entry.RelativeName, entry.Modified.ToString("dd.MM.yyyy HH:mm"), $"{entry.Bytes / 1024d:0.#} KB" }) { Tag = entry, Checked = checkedPaths.Contains(entry.Path) };
                files.Items.Add(item);
            }
            files.EndUpdate(); note.Text = $"{entries.Length} dosya listelendi. Sıralama yukarıdan aşağıya; ses dosyaları değiştirilmez.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { if (!IsDisposed) note.Text = ex.Message; }
        finally
        {
            if (ReferenceEquals(scanCancellation, cancellation)) { scanCancellation = null; loading = false; if (!IsDisposed) UpdateSelection(); }
            cancellation.Dispose();
        }
    }

    private void SavePreferences()
    {
        preferences = audioMode ? preferences with { AudioFolder = folder.Text.Trim() } : preferences with { TextFolder = folder.Text.Trim() };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
            File.WriteAllText(preferencesPath + ".tmp", JsonSerializer.Serialize(preferences)); File.Move(preferencesPath + ".tmp", preferencesPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { note.Text = "Klasör tercihi kaydedilemedi: " + ex.Message; }
    }

    private void Submit(bool combine)
    {
        Act(() =>
        {
            var selected = files.Items.Cast<ListViewItem>().Where(i => i.Checked).Select(i => ((LibraryFile)i.Tag!).Path).ToArray();
            int count = enqueue(new(selected, audioMode, combine, title.Text));
            SelectPage(0); lastRevision = -1; RefreshHistory();
            return $"{count} iş kuyruğa eklendi. Seçilen sırayla, güncel profil ve prompt kullanılır.";
        });
    }

    private void MoveSelected(int delta)
    {
        if (files.SelectedIndices.Count == 0) return;
        int from = files.SelectedIndices[0], to = from + delta;
        if (to < 0 || to >= files.Items.Count) return;
        var item = files.Items[from]; files.Items.RemoveAt(from); files.Items.Insert(to, item); item.Selected = item.Focused = true; item.EnsureVisible(); UpdateSelection();
    }

    private void UpdateSelection()
    {
        if (IsDisposed) return;
        int count = files.CheckedItems.Count; single.Enabled = combined.Enabled = !loading && count > 0;
        selection.Text = count == 0 ? "Dosyaları işaretleyin. Tek özet için bölüm sırası listedeki sıradır." : $"{count} dosya seçildi · Tek özet, ilk bölümün kayıt gününe kaydedilir.";
    }

    private void RefreshHistory()
    {
        if (IsDisposed) return;
        long revision = queue.Revision;
        var jobs = queue.Snapshot().OrderBy(j => j.Status == ProcessingStatus.Running ? 0 : j.Status == ProcessingStatus.Queued ? 1 : 2)
            .ThenBy(j => j.Status == ProcessingStatus.Queued ? j.Sequence : -j.Sequence).ToArray();
        var positions = jobs.Where(j => j.Status == ProcessingStatus.Queued).OrderBy(j => j.Sequence)
            .Select((job, i) => (job.Id, Position: i + 1)).ToDictionary(j => j.Id, j => j.Position);
        overview.Text = $"{jobs.Count(j => j.Status == ProcessingStatus.Running)} çalışıyor   ·   {jobs.Count(j => j.Status == ProcessingStatus.Queued)} sırada   ·   {jobs.Count(j => j.Status == ProcessingStatus.Completed)} tamamlandı   ·   {jobs.Count(j => j.Status == ProcessingStatus.Failed)} hata";
        profile.Text = profileCaption();
        if (queue.StorageWarning.Length > 0) note.Text = queue.StorageWarning;
        if (page != 0) return;
        if (lastRevision != revision)
        {
            string? selected = SelectedJob()?.Id;
            int top = history.TopItem?.Index ?? 0;
            history.BeginUpdate(); history.Items.Clear();
            foreach (var job in jobs)
            {
                var row = new ListViewItem(new[] { job.DisplayName, job.Status == ProcessingStatus.Queued ? $"Sırada {positions[job.Id]}" : StatusText(job.Status), job.Phase, job.Profile, job.EnqueuedAt.LocalDateTime.ToString("dd.MM HH:mm:ss"), Duration(job) })
                { Tag = job, ToolTipText = string.Join("\n", job.Sources), ForeColor = job.Status == ProcessingStatus.Failed ? Color.FromArgb(255, 155, 155) : job.Status == ProcessingStatus.Completed ? StudioTheme.Mint : StudioTheme.Ink };
                history.Items.Add(row); if (job.Id == selected) row.Selected = true;
            }
            if (history.SelectedItems.Count == 0 && history.Items.Count > 0) history.Items[0].Selected = true;
            if (history.Items.Count > 0) history.TopItem = history.Items[Math.Min(top, history.Items.Count - 1)];
            history.EndUpdate(); lastRevision = revision; UpdateDetails();
        }
        foreach (ListViewItem item in history.Items) item.SubItems[5].Text = Duration((ProcessingJob)item.Tag!);
    }

    private ProcessingJob? SelectedJob() => history.SelectedItems.Count > 0 ? (ProcessingJob)history.SelectedItems[0].Tag! : null;
    private void UpdateDetails()
    {
        var job = SelectedJob();
        retryButton.Enabled = job?.Status == ProcessingStatus.Failed; newButton.Enabled = job != null;
        openText.Enabled = openButton.Enabled = job?.Outputs.Any(File.Exists) == true; openFolder.Enabled = job != null;
        if (job == null) { details.Text = "Henüz işlem yok. Kayıt bitince STT/LLM adımları burada görünür. Metinler veya Ses kayıtları bölümünden elle özet de başlatabilirsiniz."; return; }
        var text = new StringBuilder(); text.AppendLine($"{job.DisplayName}\r\nProfil: {job.Profile} · Model: {job.Settings.LlmModel} · Deneme: {job.Attempts}");
        text.AppendLine("Kaynak sırası:"); for (int i = 0; i < job.Sources.Length; i++) text.AppendLine($"  {i + 1}. {job.Sources[i]}");
        text.AppendLine("\r\nAşamalar:"); foreach (var step in job.Events) text.AppendLine($"{step.At.LocalDateTime:dd.MM HH:mm:ss}  {step.Message}");
        if (job.Error.Length > 0) text.AppendLine("\r\nHata: " + job.Error);
        if (job.Outputs.Length > 0) text.AppendLine("\r\nÇıktılar:\r\n" + string.Join("\r\n", job.Outputs));
        text.AppendLine("\r\nYeni prompt için 'Güncel promptla yeni özet' kullanın. 'Hatayı yeniden dene' tamamlanan adımları korur.");
        details.Text = text.ToString();
    }

    private void OpenSelected(bool directory)
    {
        var job = SelectedJob() ?? throw new InvalidOperationException("Bir işlem seçin.");
        string? path = job.Outputs.LastOrDefault(File.Exists);
        if (directory) path = Path.GetDirectoryName(path ?? job.Sources.FirstOrDefault() ?? "");
        if (string.IsNullOrEmpty(path)) throw new FileNotFoundException("Çıktı henüz oluşmadı.");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void Act(Func<string> action)
    {
        try { note.Text = action(); lastRevision = -1; RefreshHistory(); }
        catch (Exception ex) { note.Text = ex.Message; }
    }

    private static string StatusText(ProcessingStatus status) => status switch { ProcessingStatus.Queued => "Kuyrukta", ProcessingStatus.Running => "Çalışıyor", ProcessingStatus.Completed => "Tamamlandı", _ => "Hata" };
    private static string Duration(ProcessingJob job) => job.StartedAt == null ? "—" : ((job.FinishedAt ?? DateTimeOffset.Now) - job.StartedAt.Value).ToString(@"hh\:mm\:ss");
    private static Label Label(string text) => new() { Text = text, Dock = DockStyle.Fill, ForeColor = StudioTheme.Muted, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private static StudioButton Button(string text) => new() { Text = text, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(0, 3, 8, 3), Height = 38, Width = 150, AccessibleName = text };
    private static TableLayoutPanel Grid(int columns, int rows)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = Padding.Empty, BackColor = Color.Transparent };
        if (columns == 1) grid.ColumnStyles.Add(new(SizeType.Percent, 100));
        if (rows == 1) grid.RowStyles.Add(new(SizeType.Percent, 100));
        return grid;
    }
    private static FlowLayoutPanel Flow(params StudioButton[] buttons)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        foreach (var button in buttons) { button.Dock = DockStyle.None; flow.Controls.Add(button); }
        return flow;
    }

    private static ListView List(string accessible, bool checks, params (string Title, int Width)[] columns)
    {
        var list = new ListView { View = View.Details, Dock = DockStyle.Fill, CheckBoxes = checks, FullRowSelect = true, MultiSelect = false,
            HideSelection = false, BorderStyle = BorderStyle.None, BackColor = StudioTheme.Field, ForeColor = StudioTheme.Ink, Font = new Font("Segoe UI", 9),
            AccessibleName = accessible, ShowItemToolTips = true, OwnerDraw = true };
        foreach (var column in columns) list.Columns.Add(column.Title, column.Width);
        list.DrawColumnHeader += (_, e) =>
        {
            using var background = new SolidBrush(StudioTheme.Card); e.Graphics.FillRectangle(background, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", list.Font, Rectangle.Inflate(e.Bounds, -7, 0), StudioTheme.Purple, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        list.DrawItem += (_, e) => { if (list.View != View.Details) e.DrawDefault = true; };
        list.DrawSubItem += (_, e) =>
        {
            if (e.Item == null) return;
            using var background = new SolidBrush(e.Item.Selected ? Color.FromArgb(47, 40, 72) : StudioTheme.Field); e.Graphics.FillRectangle(background, e.Bounds);
            var bounds = Rectangle.Inflate(e.Bounds, -7, 0);
            if (checks && e.ColumnIndex == 0)
            {
                CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(e.Bounds.X + 5, e.Bounds.Y + Math.Max(0, (e.Bounds.Height - 14) / 2)), e.Item.Checked ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal);
                bounds.X += 19; bounds.Width -= 19;
            }
            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", list.Font, bounds, e.Item.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        list.ClientSizeChanged += (_, _) =>
        {
            if (list.Columns.Count > 0) list.Columns[0].Width = Math.Max(180, list.ClientSize.Width - list.Columns.Cast<ColumnHeader>().Skip(1).Sum(c => c.Width) - 24);
        };
        return list;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); scanCancellation?.Cancel(); }
        base.Dispose(disposing);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (OperatingSystem.IsWindowsVersionAtLeast(10)) { int dark = 1; DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); }
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

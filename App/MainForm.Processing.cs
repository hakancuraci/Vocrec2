using SesKayit.Core;
using TrsLocal;

namespace SesKayit;

public sealed partial class MainForm
{
    private readonly TextBox transcriptFolder = new(), summaryFolder = new(), llmUrl = new(), llmModel = new(), llmPrompt = new(), llmBearerToken = new() { UseSystemPasswordChar = true };
    private readonly StudioSwitch enhanceMic = new() { Text = "Mikrofon konuşma iyileştirme", AccessibleName = "Mikrofon konuşma iyileştirme", OffText = "Kapalı" };
    private readonly Label processingSaveState = LabelText("URL boşsa yalnızca metin oluşturulur.", 8);
    private readonly StudioButton saveProcessing = MakeButton("Ayarları kaydet");
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> queued = new();
    private string PendingDirectory => Path.Combine(Path.GetDirectoryName(settingsPath)!, "pending");
    private sealed record PendingJob(string Audio, PostProcessingSettings Settings);
    private Task processingTask = Task.CompletedTask;

    private Control ProcessingCard()
    {
        var columns = Grid(2, 1);
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36)); columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        var left = Grid(1, 2);
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 66)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 34));

        var outputCard = new StudioCard { Dock = DockStyle.Fill, Padding = new Padding(18), Margin = new Padding(0, 0, 14, 12) };
        var outputGrid = Grid(1, 7);
        foreach (int height in new[] { 26, 30, 22, 42, 12, 22 }) outputGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        outputGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outputGrid.Controls.Add(Heading("Çıktı klasörleri", 12), 0, 0);
        outputGrid.Controls.Add(LabelText("Her kayıt için ayrı metin ve özet.", 9), 0, 1);
        outputGrid.Controls.Add(LabelText("STT metin dosyası", 9), 0, 2);
        outputGrid.Controls.Add(FolderField(transcriptFolder, "STT metin çıktı klasörü"), 0, 3);
        outputGrid.Controls.Add(LabelText("LLM özet dosyası", 9), 0, 5);
        var summaryHost = Grid(1, 2);
        summaryHost.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); summaryHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        summaryHost.Controls.Add(FolderField(summaryFolder, "LLM özet çıktı klasörü"), 0, 0);
        outputGrid.Controls.Add(summaryHost, 0, 6);
        outputCard.Controls.Add(outputGrid); left.Controls.Add(outputCard, 0, 0);

        var micCard = new StudioCard { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 12), Margin = new Padding(0, 0, 14, 0) };
        var micGrid = Grid(1, 3);
        micGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); micGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); micGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        micGrid.Controls.Add(Heading("Mikrofon iyileştirme", 11), 0, 0);
        micGrid.Controls.Add(LabelText("Gürültüyü azaltır, konuşma\nseviyesini dengeler.", 9), 0, 1);
        enhanceMic.BackColor = StudioTheme.Card; micGrid.Controls.Add(enhanceMic, 0, 2);
        micCard.Controls.Add(micGrid); left.Controls.Add(micCard, 0, 1);
        configuration.Add(enhanceMic); columns.Controls.Add(left, 0, 0);

        var llmCard = new StudioCard { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 14), Margin = Padding.Empty };
        var llmGrid = Grid(1, 7);
        foreach (int height in new[] { 26, 24, 64, 64 }) llmGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        llmGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        llmGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); llmGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var title = Grid(2, 1); title.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); title.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        title.Controls.Add(Heading("LLM ile özet", 12), 0, 0);
        var badge = LabelText("İSTEĞE BAĞLI", 8); badge.ForeColor = StudioTheme.Purple; badge.TextAlign = ContentAlignment.MiddleRight; title.Controls.Add(badge, 1, 0);
        llmGrid.Controls.Add(title, 0, 0);
        llmGrid.Controls.Add(LabelText("Bağlantınızı kurun, özetin içeriğini prompt ile belirleyin.", 9), 0, 1);
        var connection = Grid(2, 1); connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62)); connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        var urlField = InputField("Servis URL", llmUrl, "http://localhost:8000/v1"); urlField.Margin = new Padding(0, 0, 12, 6);
        connection.Controls.Add(urlField, 0, 0); connection.Controls.Add(InputField("Model", llmModel, "Örn. Qwen3-8B"), 1, 0);
        llmGrid.Controls.Add(connection, 0, 2);
        var tokenGrid = InputField("Bearer token · isteğe bağlı", llmBearerToken, "Tokenınızı girin", secret: true);
        llmGrid.Controls.Add(tokenGrid, 0, 3);
        hints.SetToolTip(llmBearerToken, "Windows hesabınıza bağlı şifreli kaydedilir. Boş bırakıp kaydetmek tokenı kaldırır.");
        llmPrompt.Multiline = true; llmPrompt.ScrollBars = ScrollBars.Vertical;
        llmGrid.Controls.Add(InputField("Özet prompt", llmPrompt, "Özetin dili, ayrıntı seviyesi ve içeriği…"), 0, 4);
        llmGrid.Controls.Add(processingSaveState, 0, 5);
        var actions = Grid(2, 1); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        var retry = MakeButton("Bekleyenleri yeniden dene"); retry.Font = new Font("Segoe UI", 9); retry.Margin = new Padding(0, 0, 10, 0);
        retry.Click += (_, _) => ResumePending(useCurrentSettings: true);
        saveProcessing.Primary = true; saveProcessing.Margin = Padding.Empty;
        actions.Controls.Add(retry, 0, 0); actions.Controls.Add(saveProcessing, 1, 0); llmGrid.Controls.Add(actions, 0, 6);
        configuration.Add(retry); configuration.Add(saveProcessing);
        llmCard.Controls.Add(llmGrid); columns.Controls.Add(llmCard, 1, 0);
        return columns;
    }

    private Control FolderField(TextBox input, string name)
    {
        var row = Grid(2, 1); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
        input.AccessibleName = name;
        var field = new StudioTextField(input) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
        var pick = MakeButton("Seç"); pick.Accent = true; pick.Margin = Padding.Empty; pick.AccessibleName = name + " seç";
        pick.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { InitialDirectory = input.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK) input.Text = dialog.SelectedPath;
        };
        hints.SetToolTip(input, name);
        input.TextChanged += (_, _) => hints.SetToolTip(input, input.Text); row.Controls.Add(field, 0, 0); row.Controls.Add(pick, 1, 0);
        configuration.Add(input); configuration.Add(pick); return row;
    }

    private Control InputField(string label, TextBox input, string placeholder, bool secret = false)
    {
        var grid = Grid(1, 2); grid.Margin = new Padding(0, 0, 0, 6);
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.Controls.Add(LabelText(label, 9), 0, 0);
        input.AccessibleName = label; input.PlaceholderText = placeholder;
        var field = new StudioTextField(input) { Dock = DockStyle.Fill, Margin = Padding.Empty };
        if (secret)
        {
            var reveal = MakeButton("Göster"); reveal.Dock = DockStyle.Right; reveal.Width = 68; reveal.Margin = Padding.Empty; reveal.Font = new Font("Segoe UI", 8); reveal.AccessibleName = "Bearer tokenı göster veya gizle";
            reveal.Click += (_, _) => { llmBearerToken.UseSystemPasswordChar = !llmBearerToken.UseSystemPasswordChar; reveal.Text = llmBearerToken.UseSystemPasswordChar ? "Göster" : "Gizle"; };
            field.Controls.Add(reveal); configuration.Add(reveal);
        }
        grid.Controls.Add(field, 0, 1); configuration.Add(input); return grid;
    }

    private void InitializeProcessingControls()
    {
        Shown += (_, _) => ResumePending(useCurrentSettings: false);
        foreach (var field in new[] { transcriptFolder, summaryFolder, llmUrl, llmModel, llmPrompt, llmBearerToken })
            field.TextChanged += (_, _) => { processingSaveState.Text = "Değişiklikleri uygulamak için kaydedin."; processingSaveState.ForeColor = StudioTheme.Muted; };
        enhanceMic.CheckedChanged += (_, _) => processingSaveState.Text = "Değişiklikleri uygulamak için kaydedin.";
        saveProcessing.Click += (_, _) =>
        {
            try
            {
                var value = new PostProcessingSettings
                {
                    TranscriptFolder = transcriptFolder.Text.Trim(), SummaryFolder = summaryFolder.Text.Trim(),
                    ProtectedLlmBearerToken = LlmTokenProtection.Protect(llmBearerToken.Text),
                    LlmUrl = llmUrl.Text.Trim(), LlmModel = llmModel.Text.Trim(), LlmPrompt = llmPrompt.Text,
                    EnhanceMicrophone = enhanceMic.Checked
                };
                value.Validate();
                Directory.CreateDirectory(value.TranscriptFolder); Directory.CreateDirectory(value.SummaryFolder);
                var previous = settings.Processing;
                settings.Processing = value;
                try { PersistSettings(); }
                catch { settings.Processing = previous; throw; }
                status.Text = "STT / LLM ayarları kaydedildi.";
                processingSaveState.Text = "✓ Ayarlar kaydedildi"; processingSaveState.ForeColor = StudioTheme.Mint;
            }
            catch (Exception ex) { ShowError(ex); }
        };
    }

    private void LoadProcessingControls(PostProcessingSettings value)
    {
        transcriptFolder.Text = value.TranscriptFolder; summaryFolder.Text = value.SummaryFolder;
        llmUrl.Text = value.LlmUrl; llmModel.Text = value.LlmModel; llmPrompt.Text = value.LlmPrompt;
        enhanceMic.Checked = value.EnhanceMicrophone;
        try { llmBearerToken.Text = LlmTokenProtection.Unprotect(value.ProtectedLlmBearerToken); }
        catch (InvalidOperationException ex) { llmBearerToken.Clear(); status.Text = ex.Message; hints.SetToolTip(llmBearerToken, ex.Message); }
        processingSaveState.Text = "URL boşsa yalnızca metin oluşturulur.";
    }

    private void PersistSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        string temp = settingsPath + ".tmp";
        File.WriteAllText(temp, System.Text.Json.JsonSerializer.Serialize(settings));
        File.Move(temp, settingsPath, true);
    }

    private void ResumePending(bool useCurrentSettings)
    {
        if (!Directory.Exists(PendingDirectory)) return;
        foreach (var path in Directory.EnumerateFiles(PendingDirectory, "*.json"))
        {
            try
            {
                var job = System.Text.Json.JsonSerializer.Deserialize<PendingJob>(File.ReadAllText(path));
                if (job != null && File.Exists(job.Audio)) QueueProcessing(job.Audio, useCurrentSettings ? settings.Processing : job.Settings);
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
            { status.Text = "Bekleyen işlem okunamadı: " + ex.Message; }
        }
    }

    private void QueueProcessing(string file, PostProcessingSettings value)
    {
        if (!queued.TryAdd(file, 0)) return;
        string id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(file))));
        string pending = Path.Combine(PendingDirectory, id + ".json");
        try
        {
            Directory.CreateDirectory(PendingDirectory);
            File.WriteAllText(pending + ".tmp", System.Text.Json.JsonSerializer.Serialize(new PendingJob(file, value)));
            File.Move(pending + ".tmp", pending, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { status.Text = "İşlem kuyruğu kaydedilemedi: " + ex.Message; }

        var previous = processingTask;
        processingTask = Task.Run(async () =>
        {
            await previous;
            void Report(string message)
            {
                if (IsDisposed || !IsHandleCreated) return;
                try { BeginInvoke((Action)(() => { if (!IsDisposed && session == null) status.Text = message; })); }
                catch (InvalidOperationException) { }
            }
            try
            {
                using var runtime = new PortableRuntime();
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
                var processor = new RecordingPostProcessor(http, (audio, token) => runtime.TranscribeAsync(audio, cancellationToken: token),
                    saved => LlmTokenProtection.Unprotect(saved.ProtectedLlmBearerToken));
                await processor.ProcessAsync(file, value, Report);
                if (File.Exists(pending)) File.Delete(pending);
            }
            catch (Exception ex)
            {
                Report("Metin/özet hatası (ses korunuyor): " + ex.Message);
                try
                {
                    var log = Path.Combine(Path.GetDirectoryName(settingsPath)!, "processing-errors.log");
                    File.AppendAllText(log, $"{DateTimeOffset.Now:O} {file}\n{ex}\n");
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            finally { queued.TryRemove(file, out _); }
        });
    }
}

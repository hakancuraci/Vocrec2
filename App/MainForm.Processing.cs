using SesKayit.Core;
using TrsLocal;

namespace SesKayit;

public sealed partial class MainForm
{
    private readonly TextBox transcriptFolder = new(), summaryFolder = new(), llmUrl = new(), llmModel = new(), llmPrompt = new(), llmBearerToken = new() { UseSystemPasswordChar = true };
    private readonly CheckBox enhanceMic = new() { Text = "Mikrofon konuşma iyileştirme", AutoSize = true };
    private readonly StudioButton saveProcessing = MakeButton("Ayarları kaydet");
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> queued = new();
    private string PendingDirectory => Path.Combine(Path.GetDirectoryName(settingsPath)!, "pending");
    private sealed record PendingJob(string Audio, PostProcessingSettings Settings);
    private Task processingTask = Task.CompletedTask;

    private Control ProcessingCard()
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(Control control, int height)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 2, 0, 4); grid.Controls.Add(control, 0, row);
        }
        void Field(string label, TextBox input, int height = 32)
        {
            Row(LabelText(label, 9), 25);
            input.BackColor = StudioTheme.Field; input.ForeColor = StudioTheme.Ink;
            input.AccessibleName = label; Row(input, height);
            configuration.Add(input);
        }
        void Folder(string label, TextBox input)
        {
            Field(label, input);
            var button = MakeButton("Klasör seç");
            button.Click += (_, _) =>
            {
                using var dialog = new FolderBrowserDialog { InitialDirectory = input.Text };
                if (dialog.ShowDialog(this) == DialogResult.OK) input.Text = dialog.SelectedPath;
            };
            Row(button, 32); configuration.Add(button);
        }
        Row(Heading("Otomatik metin ve özet", 11), 32);
        Row(LabelText("Kayıt bitince STT çalışır. Boş LLM URL: yalnız metin.", 8), 36);
        Folder("STT metin çıktı klasörü", transcriptFolder);
        Field("Qwen / OpenAI uyumlu servis URL", llmUrl);
        Field("Bearer token (isteğe bağlı)", llmBearerToken);
        hints.SetToolTip(llmBearerToken, "Token değerini girin. Windows hesabınıza bağlı şifreli kaydedilir. Boş bırakıp kaydetmek tokenı kaldırır.");
        Field("Model adı", llmModel);
        llmPrompt.Multiline = true; llmPrompt.ScrollBars = ScrollBars.Vertical;
        Field("LLM özet prompt", llmPrompt, 100);
        Folder("LLM özet çıktı klasörü", summaryFolder);
        Row(enhanceMic, 38); Row(saveProcessing, 38);
        var retry = MakeButton("Bekleyen işlemleri yeniden dene");
        retry.Click += (_, _) => ResumePending(useCurrentSettings: true);
        Row(retry, 38);
        configuration.Add(retry);
        configuration.Add(enhanceMic); configuration.Add(saveProcessing);
        scroll.Controls.Add(grid); return scroll;
    }

    private void InitializeProcessingControls()
    {
        Shown += (_, _) => ResumePending(useCurrentSettings: false);
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

using SesKayit.Core;
using TrsLocal;

namespace SesKayit;

public sealed partial class MainForm
{
    private readonly TextBox transcriptFolder = new(), summaryFolder = new(), llmUrl = new(), llmModel = new(), llmPrompt = new(), llmBearerToken = new() { UseSystemPasswordChar = true };
    private readonly StudioChoice profilePicker = new() { Dock = DockStyle.Fill, AccessibleName = "Kayıtlı LLM profili" };
    private readonly TextBox profileName = new();
    private bool loadingProfiles, processingControlsReady;
    private readonly StudioSwitch enhanceMic = new() { Text = "Mikrofon konuşma iyileştirme", AccessibleName = "Mikrofon konuşma iyileştirme", OffText = "Kapalı" };
    private readonly Label processingSaveState = LabelText("URL boşsa yalnızca metin oluşturulur.", 8);
    private readonly StudioButton saveProcessing = MakeButton("Ayarları kaydet");
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> queued = new();
    private string PendingDirectory => Path.Combine(Path.GetDirectoryName(settingsPath)!, "pending");
    private sealed record PendingJob(string Audio, PostProcessingSettings Settings, bool UseRecordingName = false);
    private Task processingTask = Task.CompletedTask;
    internal Task ProcessingCompletion => processingTask;

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
        outputGrid.Controls.Add(LabelText("Metin ve özetler Markdown (.md) olarak kaydedilir.", 9), 0, 1);
        outputGrid.Controls.Add(LabelText("STT metni (.md)", 9), 0, 2);
        outputGrid.Controls.Add(FolderField(transcriptFolder, "STT metin çıktı klasörü"), 0, 3);
        outputGrid.Controls.Add(LabelText("LLM özeti (.md)", 9), 0, 5);
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
        var llmGrid = Grid(1, 8);
        foreach (int height in new[] { 26, 24, 64, 64, 64 }) llmGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        llmGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        llmGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); llmGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var title = Grid(2, 1); title.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); title.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        title.Controls.Add(Heading("LLM ile özet", 12), 0, 0);
        var badge = LabelText("İSTEĞE BAĞLI", 8); badge.ForeColor = StudioTheme.Purple; badge.TextAlign = ContentAlignment.MiddleRight; title.Controls.Add(badge, 1, 0);
        llmGrid.Controls.Add(title, 0, 0);
        llmGrid.Controls.Add(LabelText("Bağlantınızı kurun, özetin içeriğini prompt ile belirleyin.", 9), 0, 1);
        var profiles = Grid(1, 2); profiles.RowStyles.Add(new RowStyle(SizeType.Absolute, 22)); profiles.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); profiles.Margin = new Padding(0, 0, 0, 6);
        profiles.Controls.Add(LabelText("Kayıtlı LLM profili", 9), 0, 0);
        var profileActions = Grid(3, 1); profileActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); profileActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68)); profileActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        profilePicker.Margin = new Padding(0, 0, 8, 0); profileActions.Controls.Add(profilePicker, 0, 0);
        var addProfile = MakeButton("Yeni"); addProfile.Name = "AddLlmProfile"; addProfile.Accent = true; addProfile.Margin = new Padding(0, 0, 6, 0);
        var deleteProfile = MakeButton("Sil"); deleteProfile.Name = "DeleteLlmProfile"; deleteProfile.Margin = Padding.Empty;
        addProfile.Click += (_, _) => AddLlmProfile(); deleteProfile.Click += (_, _) => DeleteLlmProfile();
        profileActions.Controls.Add(addProfile, 1, 0); profileActions.Controls.Add(deleteProfile, 2, 0); profiles.Controls.Add(profileActions, 0, 1);
        llmGrid.Controls.Add(profiles, 0, 2); configuration.Add(profilePicker); configuration.Add(addProfile); configuration.Add(deleteProfile);
        var connection = Grid(2, 1); connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62)); connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        var urlField = InputField("Servis URL", llmUrl, "http://localhost:8000/v1"); urlField.Margin = new Padding(0, 0, 12, 6);
        connection.Controls.Add(urlField, 0, 0); connection.Controls.Add(InputField("Model", llmModel, "Örn. Qwen3-8B"), 1, 0);
        llmGrid.Controls.Add(connection, 0, 3);
        var tokenGrid = InputField("Bearer token · isteğe bağlı", llmBearerToken, "Tokenınızı girin", secret: true);
        var identity = Grid(2, 1); identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36)); identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        var nameField = InputField("Profil adı", profileName, "Örn. Yerel Qwen"); nameField.Margin = new Padding(0, 0, 12, 6);
        identity.Controls.Add(nameField, 0, 0); identity.Controls.Add(tokenGrid, 1, 0); llmGrid.Controls.Add(identity, 0, 4);
        hints.SetToolTip(llmBearerToken, "Windows hesabınıza bağlı şifreli kaydedilir. Boş bırakıp kaydetmek tokenı kaldırır.");
        // Configure native edit styles before handle creation. Changing ScrollBars
        // inside TextChanged can destroy the HWND while Windows is processing WM_PASTE.
        llmPrompt.Multiline = true; llmPrompt.ScrollBars = ScrollBars.Vertical;
        llmPrompt.MaxLength = int.MaxValue; llmPrompt.AcceptsReturn = true;
        llmGrid.Controls.Add(InputField("Özet prompt", llmPrompt, "Özetin dili, ayrıntı seviyesi ve içeriği…"), 0, 5);
        llmGrid.Controls.Add(processingSaveState, 0, 6);
        var actions = Grid(2, 1); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        var retry = MakeButton("Bekleyenleri yeniden dene"); retry.Font = new Font("Segoe UI", 9); retry.Margin = new Padding(0, 0, 10, 0);
        retry.Click += (_, _) => ResumePending(useCurrentSettings: true);
        saveProcessing.Primary = true; saveProcessing.Margin = Padding.Empty;
        actions.Controls.Add(retry, 0, 0); actions.Controls.Add(saveProcessing, 1, 0); llmGrid.Controls.Add(actions, 0, 7);
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
            if (dialog.ShowDialog(this) == DialogResult.OK) { input.Text = dialog.SelectedPath; SaveOutputPreferences(); }
        };
        input.Leave += (_, _) => SaveOutputPreferences();
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
        profilePicker.SelectedIndexChanged += (_, _) => SwitchLlmProfile();
        foreach (var field in new[] { transcriptFolder, summaryFolder, llmUrl, llmModel, llmPrompt, llmBearerToken, profileName })
            field.TextChanged += (_, _) => { processingSaveState.Text = "Değişiklikleri uygulamak için kaydedin."; processingSaveState.ForeColor = StudioTheme.Muted; };
        enhanceMic.CheckedChanged += (_, _) => SaveOutputPreferences();
        saveProcessing.Click += (_, _) =>
        {
            try
            {
                var value = ReadProcessingControls();
                value.Validate();
                Directory.CreateDirectory(value.TranscriptFolder); Directory.CreateDirectory(value.SummaryFolder);
                SaveCurrentProfileDraft();
                PersistSettings();
                RefreshProfilePicker();
                status.Text = "STT / LLM ayarları kaydedildi.";
                processingSaveState.Text = "✓ Ayarlar kaydedildi"; processingSaveState.ForeColor = StudioTheme.Mint;
            }
            catch (Exception ex) { ShowError(ex); }
        };
    }

    private PostProcessingSettings ReadProcessingControls() => new()
    {
        TranscriptFolder = transcriptFolder.Text.Trim(), SummaryFolder = summaryFolder.Text.Trim(),
        ProtectedLlmBearerToken = LlmTokenProtection.Protect(llmBearerToken.Text),
        LlmUrl = llmUrl.Text.Trim(), LlmModel = llmModel.Text.Trim(), LlmPrompt = llmPrompt.Text,
        EnhanceMicrophone = enhanceMic.Checked
    };

    private void CaptureOutputPreferences()
    {
        settings.Processing = settings.Processing with
        {
            TranscriptFolder = transcriptFolder.Text.Trim(), SummaryFolder = summaryFolder.Text.Trim(),
            EnhanceMicrophone = enhanceMic.Checked
        };
    }

    private void SaveOutputPreferences()
    {
        if (!processingControlsReady || loadingProfiles) return;
        try { CaptureOutputPreferences(); PersistSettings(); processingSaveState.Text = "Çıktı klasörleri otomatik kaydedildi."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = "Ayarlar kaydedilemedi: " + ex.Message; }
    }

    private void LoadProcessingControls(PostProcessingSettings value)
    {
        bool previous = loadingProfiles; loadingProfiles = true;
        try
        {
            transcriptFolder.Text = value.TranscriptFolder; summaryFolder.Text = value.SummaryFolder;
            llmUrl.Text = value.LlmUrl; llmModel.Text = value.LlmModel; llmPrompt.Text = value.LlmPrompt;
            llmBearerToken.UseSystemPasswordChar = true;
            var reveal = llmBearerToken.Parent?.Controls.OfType<StudioButton>().FirstOrDefault();
            if (reveal != null) reveal.Text = "Göster";
            enhanceMic.Checked = value.EnhanceMicrophone;
            try { llmBearerToken.Text = LlmTokenProtection.Unprotect(value.ProtectedLlmBearerToken); }
            catch (InvalidOperationException ex) { llmBearerToken.Clear(); status.Text = ex.Message; hints.SetToolTip(llmBearerToken, ex.Message); }
            processingSaveState.Text = "Seçili profil kayıtlar için kullanılır.";
        }
        finally { loadingProfiles = previous; }
    }

    private void InitializeLlmProfiles()
    {
        settings.LlmProfiles ??= [];
        if (settings.LlmProfiles.Count == 0) settings.LlmProfiles.Add(LlmProfile.FromSettings(settings.Processing, "Varsayılan"));
        var selected = settings.LlmProfiles.FirstOrDefault(p => p.Id == settings.SelectedLlmProfileId) ?? settings.LlmProfiles[0];
        settings.SelectedLlmProfileId = selected.Id;
        settings.Processing = selected.ApplyTo(settings.Processing);
        LoadProcessingControls(settings.Processing);
        profileName.Text = selected.Name;
        RefreshProfilePicker(); processingControlsReady = true;
    }

    private void RefreshProfilePicker()
    {
        bool previous = loadingProfiles; loadingProfiles = true;
        try
        {
            profilePicker.SelectedIndex = -1; profilePicker.Items.Clear();
            profilePicker.Items.AddRange(settings.LlmProfiles.Cast<object>());
            profilePicker.SelectedIndex = settings.LlmProfiles.FindIndex(p => p.Id == settings.SelectedLlmProfileId);
            profilePicker.Invalidate();
        }
        finally { loadingProfiles = previous; }
    }

    private void SaveCurrentProfileDraft()
    {
        if (!processingControlsReady || loadingProfiles) return;
        CaptureOutputPreferences();
        var value = ReadProcessingControls();
        string name = profileName.Text.Trim();
        if (name.Length == 0) name = string.IsNullOrWhiteSpace(value.LlmModel) ? "LLM " + (settings.LlmProfiles.Count + 1) : value.LlmModel;
        string baseName = name;
        for (int suffix = 2; settings.LlmProfiles.Any(p => p.Id != settings.SelectedLlmProfileId && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); suffix++)
            name = baseName + " (" + suffix + ")";
        profileName.Text = name;
        var profile = LlmProfile.FromSettings(value, name, settings.SelectedLlmProfileId);
        int index = settings.LlmProfiles.FindIndex(p => p.Id == profile.Id);
        if (index < 0) settings.LlmProfiles.Add(profile); else settings.LlmProfiles[index] = profile;
        settings.SelectedLlmProfileId = profile.Id; settings.Processing = value;
    }

    private void SwitchLlmProfile()
    {
        if (loadingProfiles || !processingControlsReady || profilePicker.SelectedIndex < 0) return;
        var next = (LlmProfile)profilePicker.Items[profilePicker.SelectedIndex];
        try
        {
            SaveCurrentProfileDraft();
            var selected = settings.LlmProfiles.First(p => p.Id == next.Id);
            settings.SelectedLlmProfileId = selected.Id; settings.Processing = selected.ApplyTo(settings.Processing);
            LoadProcessingControls(settings.Processing); profileName.Text = selected.Name;
            PersistSettings(); RefreshProfilePicker();
        }
        catch (Exception ex) { RefreshProfilePicker(); ShowError(ex); }
    }

    private void AddLlmProfile()
    {
        try
        {
            SaveCurrentProfileDraft();
            var profile = new LlmProfile { Name = "LLM " + (settings.LlmProfiles.Count + 1) };
            settings.LlmProfiles.Add(profile); settings.SelectedLlmProfileId = profile.Id;
            settings.Processing = profile.ApplyTo(settings.Processing);
            LoadProcessingControls(settings.Processing); profileName.Text = profile.Name;
            RefreshProfilePicker(); PersistSettings(); profileName.Focus(); profileName.SelectAll();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void DeleteLlmProfile()
    {
        try
        {
            CaptureOutputPreferences();
            settings.LlmProfiles.RemoveAll(p => p.Id == settings.SelectedLlmProfileId);
            if (settings.LlmProfiles.Count == 0) settings.LlmProfiles.Add(new LlmProfile());
            var selected = settings.LlmProfiles[0]; settings.SelectedLlmProfileId = selected.Id;
            settings.Processing = selected.ApplyTo(settings.Processing);
            LoadProcessingControls(settings.Processing); profileName.Text = selected.Name;
            RefreshProfilePicker(); PersistSettings();
        }
        catch (Exception ex) { ShowError(ex); }
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
                if (job != null && File.Exists(job.Audio)) QueueProcessing(job.Audio, useCurrentSettings ? settings.Processing : job.Settings, job.UseRecordingName);
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
            { status.Text = "Bekleyen işlem okunamadı: " + ex.Message; }
        }
    }

    private void QueueProcessing(string file, PostProcessingSettings value, bool useRecordingName = false)
    {
        if (!queued.TryAdd(file, 0)) return;
        string id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(file))));
        string pending = Path.Combine(PendingDirectory, id + ".json");
        try
        {
            Directory.CreateDirectory(PendingDirectory);
            File.WriteAllText(pending + ".tmp", System.Text.Json.JsonSerializer.Serialize(new PendingJob(file, value, useRecordingName)));
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
                await processRecording(file, value, Report, useRecordingName);
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

    private static async Task ProcessRecordingAsync(string file, PostProcessingSettings value, Action<string> report, bool useRecordingName)
    {
        using var runtime = new PortableRuntime();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        var processor = new RecordingPostProcessor(http, (audio, token) => runtime.TranscribeAsync(audio, cancellationToken: token),
            saved => LlmTokenProtection.Unprotect(saved.ProtectedLlmBearerToken));
        await processor.ProcessAsync(file, value, report, useRecordingName: useRecordingName);
    }
}

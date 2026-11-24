using SesKayit.Core;

namespace SesKayit;

internal sealed record LibraryRequest(string[] Files, bool Audio, bool Combine, string Title);

public sealed partial class MainForm
{
    private readonly ProcessingQueue processingQueue;
    private readonly Func<ProcessingJob, Action<string>, Task> processManual;
    private readonly StudioButton historyButton = MakeButton("İşlem geçmişi / Arşiv");
    private readonly System.Windows.Forms.Timer historyTimer = new() { Interval = 700 };
    private ProcessingHubForm? processingHub;
    internal ProcessingJob[] ProcessingHistory => processingQueue.Snapshot();

    private void InitializeHistory()
    {
        historyButton.Name = "ProcessingHistory"; historyButton.AccessibleName = "İşlem geçmişi ve arşiv";
        historyButton.Accent = true; historyButton.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        historyButton.Click += (_, _) => OpenProcessingHub();
        historyTimer.Tick += (_, _) => RefreshProcessingIndicator();
    }

    private void RefreshProcessingIndicator()
    {
        if (IsDisposed) return;
        var jobs = processingQueue.Snapshot();
        int waiting = jobs.Count(j => j.Status == ProcessingStatus.Queued);
        var active = jobs.FirstOrDefault(j => j.Status == ProcessingStatus.Running);
        historyButton.Text = active != null ? $"{active.Phase} · {waiting} sırada" : waiting > 0 ? $"İşlemler · {waiting} sırada" : "İşlem geçmişi / Arşiv";
        hints.SetToolTip(historyButton, $"{jobs.Count(j => j.Status == ProcessingStatus.Completed)} tamamlandı · {jobs.Count(j => j.Status == ProcessingStatus.Failed)} hata · {waiting} sırada");
    }

    internal ProcessingHubForm OpenProcessingHub()
    {
        if (processingHub is { IsDisposed: false }) { processingHub.Show(); processingHub.Activate(); return processingHub; }
        processingHub = new ProcessingHubForm(processingQueue, transcriptFolder.Text, folder.Text,
            Path.Combine(Path.GetDirectoryName(settingsPath)!, "library-settings.json"), EnqueueLibraryWork,
            job =>
            {
                var snapshot = CurrentSummarySettings();
                bool result = processingQueue.Retry(job.Id, snapshot, profileName.Text);
                RefreshProcessingIndicator(); return result;
            }, job => EnqueueLibraryWork(new(job.Sources, job.Kind != ProcessingKind.TextSummary, true,
                job.Kind == ProcessingKind.Recording && job.UseRecordingName
                    ? RecordingDates.WithoutPrefix(Path.GetFileNameWithoutExtension(job.Sources[0]), job.RecordedOn) : job.Title)) > 0,
            () => $"Etkin profil: {profileName.Text}  ·  Model: {llmModel.Text}");
        processingHub.Show(this); return processingHub;
    }

    private PostProcessingSettings CurrentSummarySettings()
    {
        if (closingRequested) throw new InvalidOperationException("Uygulama kapanırken yeni işlem eklenemez.");
        var value = ReadProcessingControls(); value.Validate();
        if (value.Endpoint() == null) throw new ArgumentException("Özet oluşturmak için STT / LLM ayarlarında servis URL'si, model ve prompt girin.");
        SaveSettings(); return value;
    }

    internal int EnqueueLibraryWork(LibraryRequest request)
    {
        if (request.Files.Length == 0) throw new ArgumentException("Önce en az bir dosya işaretleyin.");
        var snapshot = CurrentSummarySettings();
        foreach (string source in request.Files)
            if (!File.Exists(source)) throw new FileNotFoundException("Kaynak dosya bulunamadı.", source);
        var groups = request.Combine ? new[] { request.Files } : request.Files.Select(f => new[] { f }).ToArray();
        foreach (string[] group in groups)
            processingQueue.Enqueue(new ProcessingJob { Sources = group.ToArray(), Kind = request.Audio ? ProcessingKind.AudioSummary : ProcessingKind.TextSummary,
                Settings = snapshot, Profile = profileName.Text, Title = request.Title.Trim(), RecordedOn = RecordingDates.FromAudio(group[0]) });
        RefreshProcessingIndicator(); return groups.Length;
    }
}

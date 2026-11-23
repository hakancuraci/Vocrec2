using System.Drawing.Imaging;
using SesKayit;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: SesKayit.Screenshot <output.png>");
            return 1;
        }
        string path = Path.GetFullPath(args[0]);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // Instantiate the actual production form, including its real device enumeration.
        // No example devices, recording data, or replacement controls are injected.
        using var form = new MainForm();
        using var captureTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        int result = 1;
        captureTimer.Tick += (_, _) =>
        {
            captureTimer.Stop();
            try
            {
                form.PerformLayout();
                form.Refresh();
                CheckImportantControls(form);
                using var bitmap = new Bitmap(form.Width, form.Height);
                // Native WinForms rendering also works on a runner without a visible desktop.
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                bitmap.Save(path, ImageFormat.Png);
                Console.WriteLine($"Rendered production MainForm: {bitmap.Width}x{bitmap.Height} -> {path}");
                form.Size = form.MinimumSize;
                form.PerformLayout(); form.Refresh();
                CheckImportantControls(form);
                using var compact = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(compact, new Rectangle(Point.Empty, compact.Size));
                compact.Save(Path.Combine(Path.GetDirectoryName(path)!, "SesKayit-compact.png"), ImageFormat.Png);
                form.Size = new Size(1020, 760);
                var processingTab = Descendants(form).OfType<Button>().Single(c => c.Name == "ProcessingTab");
                processingTab.PerformClick();
                form.PerformLayout(); form.Refresh(); Application.DoEvents();
                CheckProcessingControls(form);
                Capture(form, "Vocrec2-STT-LLM.png", path);
                form.Size = form.MinimumSize;
                form.PerformLayout(); form.Refresh(); Application.DoEvents();
                CheckProcessingControls(form);
                Capture(form, "Vocrec2-STT-LLM-compact.png", path);
                result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { form.Close(); }
        };
        form.Shown += (_, _) => captureTimer.Start();
        Application.Run(form);
        return result;
    }

    private static void Capture(Form form, string name, string originalPath)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(Path.GetDirectoryName(originalPath)!, name), ImageFormat.Png);
    }

    private static void CheckProcessingControls(Form form)
    {
        var controls = Descendants(form).ToArray();
        foreach (var control in controls.Where(c => c.Visible && c is Button)) CheckAncestorClipping(control);
        if (controls.OfType<TabControl>().Any()) throw new InvalidOperationException("Native white tabs must not return.");
        foreach (string name in new[] { "Kayıtlı LLM profili", "Profil adı", "STT metin çıktı klasörü", "LLM özet çıktı klasörü", "Servis URL", "Model", "Bearer token · isteğe bağlı", "Özet prompt", "Mikrofon konuşma iyileştirme" })
        {
            var control = controls.Single(c => c.AccessibleName == name);
            var bounds = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            if (!control.Visible || !form.ClientRectangle.Contains(bounds) || control.Width < 40 || control.Height < 16)
                throw new InvalidOperationException($"Setting must be visible without scrolling: {name}; bounds: {bounds}; form: {form.ClientRectangle}");
        }
        var save = controls.Single(c => c is Button && c.Text == "Ayarları kaydet");
        if (!save.Visible || !form.ClientRectangle.Contains(form.RectangleToClient(save.RectangleToScreen(save.ClientRectangle))))
            throw new InvalidOperationException("Save action must remain visible without scrolling.");
        Console.WriteLine($"Modern settings layout verified at {form.Size}.");
    }

    private static void CheckAncestorClipping(Control control)
    {
        var bounds = control.RectangleToScreen(control.ClientRectangle);
        for (var parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if (!parent.RectangleToScreen(parent.ClientRectangle).Contains(bounds))
                throw new InvalidOperationException($"Control is clipped by parent: {control.Text}; bounds: {bounds}; parent: {parent.GetType().Name} {parent.RectangleToScreen(parent.ClientRectangle)}");
        }
    }

    private static void CheckImportantControls(Form form)
    {
        if (form.Icon == null || form.Icon.Width < 16) throw new InvalidOperationException("Product icon must be embedded in the form.");
        Control[] controls = Descendants(form).ToArray();
        foreach (string text in new[] { "Kayıt klasörünü değiştir", "Klasörü görüntüle", "●  Kaydı başlat", "■  Kaydı bitir" })
        {
            var control = controls.Single(c => c is Button && c.Text == text);
            var bounds = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            if (!control.Visible || !form.ClientRectangle.Contains(bounds))
                throw new InvalidOperationException($"Important action is outside the visible window: {text}");
        }
        var folder = controls.Single(c => c.AccessibleName == "Seçili kayıt klasörü");
        if (!folder.Visible || !form.ClientRectangle.Contains(form.RectangleToClient(folder.RectangleToScreen(folder.ClientRectangle))))
            throw new InvalidOperationException("Recording folder must stay visible without scrolling.");
        var recordingName = controls.Single(c => c.AccessibleName == "Kayıt adı");
        if (!recordingName.Visible || recordingName.Height < recordingName.PreferredSize.Height)
            throw new InvalidOperationException("Optional recording name must remain visible and readable.");
        CheckAncestorClipping(recordingName);
        foreach (var name in new[] { "Kayıt süresi", "Uygulama başlığı", "Uygulama açıklaması" })
        {
            var label = (Label)controls.Single(c => c.AccessibleName == name);
            if (label.AutoEllipsis || label.ClientSize.Width < label.PreferredSize.Width || label.ClientSize.Height < label.PreferredSize.Height)
                throw new InvalidOperationException($"Text must be shown in full: {name}");
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}

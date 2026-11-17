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
                using var bitmap = new Bitmap(form.Width, form.Height);
                // Native WinForms rendering also works on a runner without a visible desktop.
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                bitmap.Save(path, ImageFormat.Png);
                Console.WriteLine($"Rendered production MainForm: {bitmap.Width}x{bitmap.Height} -> {path}");
                result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { form.Close(); }
        };
        form.Shown += (_, _) => captureTimer.Start();
        Application.Run(form);
        return result;
    }
}

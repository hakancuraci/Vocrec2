using SesKayit.Core;
using Xunit;

namespace SesKayit.WindowsTests;

public class ActivityMonitorTests
{
    [Fact]
    public async Task CanScanWindowsSessionsOnBackgroundThreadWithoutOpeningCapture()
    {
        var snapshot = await Task.Run(AudioActivityMonitor.Scan).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(snapshot.Devices.Length, snapshot.Devices.Select(d => d.Id).Distinct().Count());
        var selected = AutomaticSources.Select(snapshot.Devices);
        Assert.All(selected, id => Assert.Contains(snapshot.Devices, d => d.Id == id));
        Assert.All(snapshot.Outputs.Concat(snapshot.Microphones), line => Assert.False(string.IsNullOrWhiteSpace(line)));
    }
}

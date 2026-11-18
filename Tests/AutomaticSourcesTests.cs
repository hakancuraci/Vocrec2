using SesKayit.Core;
using Xunit;

public class AutomaticSourcesTests
{
    [Fact]
    public void IncludesEveryOutputAndEveryMicWithoutWaitingForApplicationActivity()
    {
        var selected = AutomaticSources.Select([
            new("speakers", "Hoparlör", false, false, false),
            new("headphones", "Kulaklık", false, true, false),
            new("communication-mic", "Mikrofon", true, true, false),
            new("unused-mic", "Webcam", true, false, false)
        ]);
        Assert.True(selected.SetEquals(["speakers", "headphones", "communication-mic", "unused-mic"]));
    }

    [Fact]
    public void NonDefaultMicIsSelectedEvenWhenNoOtherAppHasOpenedIt()
    {
        Assert.Contains("teams-mic", AutomaticSources.Select([new("teams-mic", "USB", true, false, true)]));
        Assert.Contains("teams-mic", AutomaticSources.Select([new("teams-mic", "USB", true, false, false)]));
    }

    [Fact]
    public void FollowsAvailableDeviceSnapshotAndDeduplicatesIds()
    {
        var selected = AutomaticSources.Select([
            new("new-mic", "USB", true, false, false),
            new("new-mic", "USB", true, true, false)
        ]);
        Assert.Single(selected); Assert.Contains("new-mic", selected);
    }
}

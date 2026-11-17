using SesKayit.Core;
using Xunit;

public class AutomaticSourcesTests
{
    [Fact]
    public void IncludesEveryOutputAndDefaultMicBeforeAnyAppMakesSound()
    {
        var selected = AutomaticSources.Select([
            new("speakers", "Hoparlör", false, false, false),
            new("headphones", "Kulaklık", false, true, false),
            new("communication-mic", "Mikrofon", true, true, false),
            new("unused-mic", "Webcam", true, false, false)
        ]);
        Assert.True(selected.SetEquals(["speakers", "headphones", "communication-mic"]));
    }

    [Fact]
    public void IncludesNonDefaultMicWhenAnotherAppOpensIt()
    {
        Assert.Contains("teams-mic", AutomaticSources.Select([new("teams-mic", "USB", true, false, true)]));
        Assert.Empty(AutomaticSources.Select([new("teams-mic", "USB", true, false, false)]));
    }

    [Fact]
    public void RemovesDisconnectedEndpointsAndUsesNewCommunicationDefault()
    {
        var selected = AutomaticSources.Select([
            new("old-mic", "Dahili", true, false, false),
            new("new-mic", "USB", true, true, false)
        ]);
        Assert.Single(selected); Assert.Contains("new-mic", selected);
    }
}

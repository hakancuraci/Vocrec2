namespace SesKayit.Core;

public sealed record ObservedDevice(string Id, string Name, bool Microphone, bool CommunicationDefault, bool UsedByOtherApp);

public static class AutomaticSources
{
    // Device availability alone selects sources. Application/session scans never gate recording.
    public static HashSet<string> Select(IEnumerable<ObservedDevice> devices) => devices
        .Select(d => d.Id).ToHashSet();
}

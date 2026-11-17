namespace SesKayit.Core;

public sealed record ObservedDevice(string Id, string Name, bool Microphone, bool CommunicationDefault, bool UsedByOtherApp);

public static class AutomaticSources
{
    // Activity peaks deliberately don't gate capture: quiet audio and the first word matter.
    public static HashSet<string> Select(IEnumerable<ObservedDevice> devices) => devices
        .Where(d => !d.Microphone || d.CommunicationDefault || d.UsedByOtherApp)
        .Select(d => d.Id).ToHashSet();
}

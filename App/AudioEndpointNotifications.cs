using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using SesKayit.Core;

namespace SesKayit;

internal sealed class AudioEndpointNotifications(EndpointChanges changes) : IMMNotificationClient
{
    // MMDevice callbacks must remain non-blocking: no UI, capture disposal, or COM calls here.
    public void OnDeviceStateChanged(string id, DeviceState state) => changes.DeviceChanged(id);
    public void OnDeviceAdded(string id) => changes.DeviceChanged(id);
    public void OnDeviceRemoved(string id) => changes.DeviceChanged(id);
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id)
    {
        if (flow is DataFlow.Render or DataFlow.All) changes.DefaultChanged(false);
        if (flow is DataFlow.Capture or DataFlow.All) changes.DefaultChanged(true);
    }
    public void OnPropertyValueChanged(string id, PropertyKey key)
    {
        if (new[] { PropertyKeys.PKEY_AudioEngine_DeviceFormat, PropertyKeys.PKEY_AudioEngine_OEMFormat,
            PropertyKeys.PKEY_AudioEndpoint_JackSubType, PropertyKeys.PKEY_AudioEndpoint_FormFactor,
            PropertyKeys.PKEY_AudioEndpoint_PhysicalSpeakers, PropertyKeys.PKEY_AudioEndpoint_Association,
            PropertyKeys.PKEY_Device_FriendlyName, PropertyKeys.PKEY_Device_DeviceDesc }.Contains(key)) changes.DeviceChanged(id);
    }
}

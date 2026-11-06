using System.Collections.Concurrent;
using Acs.Core.Hik;
using Acs.Core.Model;

namespace Acs.Core.Tests.Fakes;

/// <summary>Maps device id to a FakeDevice, creating one on first use.</summary>
public sealed class FakeDeviceFactory : IHikDeviceFactory
{
    private readonly ConcurrentDictionary<int, FakeDevice> _devices = new();

    public FakeDevice For(int deviceId) => _devices.GetOrAdd(deviceId, _ => new FakeDevice());
    public void Add(int deviceId, FakeDevice device) => _devices[deviceId] = device;
    public IHikDevice Create(DeviceRecord device) => For(device.Id);
}

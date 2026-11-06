using Acs.Core.Model;

namespace Acs.Core.Hik;

public sealed class HikDeviceFactory : IHikDeviceFactory
{
    public IHikDevice Create(DeviceRecord device) => new HikClient(device.Host, device.Username, device.Password);
}

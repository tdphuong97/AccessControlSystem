using Acs.Core.Model;

namespace Acs.Core.Hik;

/// <summary>Caches one HikClient per device id; rebuilds (and disposes the old one) when host or credentials change.</summary>
public sealed class HikDeviceFactory : IHikDeviceFactory
{
    private readonly object _lock = new();
    private readonly Dictionary<int, (string Host, string User, string Password, HikClient Client)> _cache = new();

    public IHikDevice Create(DeviceRecord device)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(device.Id, out var e) && e.Host == device.Host && e.User == device.Username && e.Password == device.Password)
                return e.Client;
            var c = new HikClient(device.Host, device.Username, device.Password);
            _cache[device.Id] = (device.Host, device.Username, device.Password, c);
            e.Client?.Dispose();
            return c;
        }
    }
}

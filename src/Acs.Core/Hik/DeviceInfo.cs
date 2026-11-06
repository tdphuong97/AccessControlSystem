namespace Acs.Core.Hik;

// Contract C2. Created in Task 3 only because DeviceStore.SetSeen needs it; the Hik track owns this file afterwards.
public sealed record DeviceInfo(string Model, string Serial, string Firmware);

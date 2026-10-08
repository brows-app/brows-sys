namespace Brows.Sys;

internal interface IDeviceNotificationRegistrar {
    IDisposable Register(nint windowHandle, Guid interfaceClassGuid);
}

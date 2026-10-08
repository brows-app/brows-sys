using System;

namespace Brows.Sys.Messages.DeviceMessages.Devices;

/// <summary>
/// Specifies flags describing a volume and a volume-change notification.
/// </summary>
/// <remarks>
/// These values may be combined to describe independent media and network characteristics.
/// </remarks>
[Flags]
public enum VolumeFlag {
    /// <summary>
    /// No media or network flags are set.
    /// </summary>
    None = 0,
    /// <summary>
    /// The change affects media in the drive.
    /// </summary>
    Media = 1,
    /// <summary>
    /// The logical volume is a network volume.
    /// </summary>
    Network = 2,
}

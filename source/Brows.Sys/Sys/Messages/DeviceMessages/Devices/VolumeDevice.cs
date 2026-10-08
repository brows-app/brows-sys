namespace Brows.Sys.Messages.DeviceMessages.Devices;

/// <summary>
/// Describes a logical volume associated with a device notification.
/// </summary>
public sealed record VolumeDevice : Device {
    /// <summary>
    /// Gets the device kind for a logical volume.
    /// </summary>
    /// <value>
    /// <see cref="DeviceKind.Volume"/>.
    /// </value>
    public sealed override DeviceKind DeviceKind => DeviceKind.Volume;

    /// <summary>
    /// Gets or initializes the name identifying this volume.
    /// </summary>
    /// <value>
    /// The volume name, or <see langword="null"/> when no name is supplied.
    /// </value>
    /// <remarks>
    /// The supplied Windows backend uses a single drive letter without a colon or directory separator.
    /// </remarks>
    public string VolumeName { get; init; }

    /// <summary>
    /// Gets or initializes the flags describing the volume and its change notification.
    /// </summary>
    /// <value>
    /// A bitwise combination of <see cref="VolumeFlag"/> values. The default is <see cref="VolumeFlag.None"/>.
    /// </value>
    public VolumeFlag VolumeFlag { get; init; }
}

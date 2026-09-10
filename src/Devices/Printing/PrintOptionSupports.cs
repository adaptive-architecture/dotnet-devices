namespace AdaptArch.Devices.Printing;

/// <summary>
/// The <see cref="PrintOptions"/> properties that one channel applies to a job. An option
/// that is not named here is dropped or ignored by that channel, whatever the caller sets.
/// </summary>
/// <remarks>
/// The value starts from what the transport itself can carry, which the library already
/// knows without asking the device: a raw channel applies none, because it sends the bytes
/// unchanged, and the Windows spooler applies what a device mode can hold. When the
/// discovery reads the capabilities of a printer, the value is narrowed further by what the
/// printer reported.
/// </remarks>
[Flags]
public enum PrintOptionSupports
{
    /// <summary>No option is applied. The payload reaches the device unchanged.</summary>
    None = 0,

    /// <summary><see cref="PrintOptions.Copies"/> is applied.</summary>
    Copies = 1,

    /// <summary><see cref="PrintOptions.Duplex"/> is applied.</summary>
    Duplex = 1 << 1,

    /// <summary><see cref="PrintOptions.ColorMode"/> is applied.</summary>
    ColorMode = 1 << 2,

    /// <summary><see cref="PrintOptions.Orientation"/> is applied.</summary>
    Orientation = 1 << 3,

    /// <summary><see cref="PrintOptions.MediaSource"/> is applied.</summary>
    MediaSource = 1 << 4,

    /// <summary><see cref="PrintOptions.MediaSize"/> is applied.</summary>
    MediaSize = 1 << 5,

    /// <summary><see cref="PrintOptions.ResolutionDpi"/> is applied.</summary>
    ResolutionDpi = 1 << 6,

    /// <summary><see cref="PrintOptions.JobName"/> is applied.</summary>
    JobName = 1 << 7,

    /// <summary><see cref="PrintOptions.RequestingUserName"/> is applied.</summary>
    RequestingUserName = 1 << 8,

    /// <summary><see cref="PrintOptions.PageRanges"/> is applied.</summary>
    PageRanges = 1 << 9,

    /// <summary><see cref="PrintOptions.NumberUp"/> is applied.</summary>
    NumberUp = 1 << 10,

    /// <summary><see cref="PrintOptions.Quality"/> is applied.</summary>
    Quality = 1 << 11,

    /// <summary><see cref="PrintOptions.MediaType"/> is applied.</summary>
    MediaType = 1 << 12,

    /// <summary><see cref="PrintOptions.OutputBin"/> is applied.</summary>
    OutputBin = 1 << 13,

    /// <summary><see cref="PrintOptions.Scaling"/> is applied.</summary>
    Scaling = 1 << 14,

    /// <summary>Every option this library models is applied.</summary>
    All = Copies | Duplex | ColorMode | Orientation | MediaSource | MediaSize | ResolutionDpi | JobName | RequestingUserName
        | PageRanges | NumberUp | Quality | MediaType | OutputBin | Scaling,
}

using IppScaling = SharpIpp.Protocol.Models.PrintScaling;

namespace AdaptArch.Devices.Printing.Ipp;

// "print-scaling" from PWG 5100.16 is a keyword, so SharpIppNext carries it as a value
// type around a string rather than as an enum. The two directions live together so the
// keyword text is written once.
internal static class IppScalingMapper
{
    public static IppScaling? Map(PrintScaling? scaling)
    {
        if (scaling == PrintScaling.Auto)
        {
            return IppScaling.Auto;
        }

        if (scaling == PrintScaling.AutoFit)
        {
            return IppScaling.AutoFit;
        }

        if (scaling == PrintScaling.Fill)
        {
            return IppScaling.Fill;
        }

        if (scaling == PrintScaling.Fit)
        {
            return IppScaling.Fit;
        }

        if (scaling == PrintScaling.None)
        {
            return IppScaling.None;
        }

        return null;
    }

    // A keyword this library does not model gives null, so the caller leaves it out.
    public static PrintScaling? Map(IppScaling scaling)
    {
        if (String.Equals(scaling.Value, IppScaling.Auto.Value, StringComparison.OrdinalIgnoreCase))
        {
            return PrintScaling.Auto;
        }

        if (String.Equals(scaling.Value, IppScaling.AutoFit.Value, StringComparison.OrdinalIgnoreCase))
        {
            return PrintScaling.AutoFit;
        }

        if (String.Equals(scaling.Value, IppScaling.Fill.Value, StringComparison.OrdinalIgnoreCase))
        {
            return PrintScaling.Fill;
        }

        if (String.Equals(scaling.Value, IppScaling.Fit.Value, StringComparison.OrdinalIgnoreCase))
        {
            return PrintScaling.Fit;
        }

        if (String.Equals(scaling.Value, IppScaling.None.Value, StringComparison.OrdinalIgnoreCase))
        {
            return PrintScaling.None;
        }

        return null;
    }
}

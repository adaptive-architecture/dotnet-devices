using AdaptArch.Devices.Printing.Raster;
using Microsoft.Extensions.Logging;

namespace AdaptArch.Devices.Printing.Ipp;

// The channel a document is converted for: an IPP printer, or a CUPS queue.
internal sealed record IppConversionChannel(
    PrintFormatPolicy Formats,
    PrinterId Id,
    ILogger Logger,
    Func<CancellationToken, Task<PrinterConfiguration>> GetConfigurationAsync,
    Func<CancellationToken, Task<Uri>> GetEndpointAsync,
    string[] Targets)
{
    // A CUPS queue reads every document itself, through its filters, so a job that asks
    // for no rendering needs no format list to know it passes through.
    public bool ReadsEveryDocument { get; init; }
}

// What a converted, or deliberately unconverted, job is sent as.
internal sealed record IppConversion(
    PrinterPayload Payload,
    string Format,
    PrintOptions? Options,
    IReadOnlyList<DroppedOption> Dropped,
    string? ConverterUsed)
{
    public bool IsConverted => ConverterUsed is not null;
}

// The one conversion step IPP and CUPS share, so the two paths cannot drift.
internal static class IppDocumentConversion
{
    // A document the printer cannot read is rendered to a format it can, when a converter
    // is registered for it. Everything else passes through: a printer that lists the format
    // reads the document itself, which is always better than a raster of it -- unless the job
    // named the converter, which is the one way of saying otherwise.
    public static async Task<IppConversion> ConvertIfNeededAsync(
        IppConversionChannel channel,
        PrinterPayload payload,
        string format,
        PrintOptions? options,
        CancellationToken cancellationToken)
    {
        var formats = channel.Formats;
        var name = PrintConverters.NameFor(formats, payload.ContentType, options);
        var kind = formats.KindOf(payload.ContentType);
        if (kind != PrinterFormatKind.Document)
        {
            // An image is never converted, so a name on one is reported rather than failed;
            // a printer language or opaque bytes cannot be rendered at all.
            if (name is not null && kind != PrinterFormatKind.Image)
            {
                throw PrintConverters.Unhonoured(name, payload.ContentType, channel.Id, $"'{payload.ContentType}' is sent as it is and nothing renders it");
            }

            return Unconverted(payload, format, options, Unrendered(
                options,
                $"the library renders only documents, so {payload.ContentType} goes to the printer as it is",
                nameof(PrintOptions.ConverterName)));
        }

        // The converter is looked for before the format list is read, because an application
        // that registered none converts nothing whatever the printer answers, and this path
        // must not cost it a request it never needed.
        var converter = formats.ConverterFor(payload.ContentType, name);
        if (converter is null)
        {
            // A job that named a converter asked for that one, so rendering with another or
            // sending the document unchanged would both be the wrong answer to a question
            // the caller did ask.
            PrintConverters.ThrowIfNamed(formats, payload.ContentType, name);
            return Unconverted(payload, format, options, Unrendered(options, $"no converter is registered for {payload.ContentType}"));
        }

        // The document itself is the better thing to send when nobody said otherwise: the
        // printer's own interpreter beats any raster of ours and the job is a fraction of the
        // size. Naming or requiring a converter is saying otherwise, and it is the only way:
        // a placement or a document media size alone does not pick an engine on the caller's
        // behalf, because the one registered first may be there for another channel, and on
        // CUPS it would replace the queue's own driver. Those options are reported instead.
        if (name is null)
        {
            var reads = channel.ReadsEveryDocument
                || (await channel.GetConfigurationAsync(cancellationToken).ConfigureAwait(false)).SupportedDocumentFormats
                    .Contains(payload.ContentType, StringComparer.OrdinalIgnoreCase);
            if (reads)
            {
                return Unconverted(payload, format, options, Unrendered(
                    options,
                    "the printer reads the document itself and the job names no converter, so the library renders nothing"));
            }
        }

        var configuration = await channel.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var target = IppDocumentFormat.NegotiateConversionTarget(configuration.SupportedDocumentFormats, converter, channel.Targets);
        if (target is null)
        {
            if (name is not null)
            {
                throw PrintConverters.Unhonoured(name, payload.ContentType, channel.Id, $"the printer reads no format converter '{name}' writes");
            }

            // Passing through is still the right answer where the printer reads the document.
            const string reason = "the printer reads no format the converter writes";
            var unreachable = await channel.GetEndpointAsync(cancellationToken).ConfigureAwait(false);
            IppLog.DocumentNotConverted(channel.Logger, payload.ContentType, unreachable, reason);
            return Unconverted(payload, format, options, Unrendered(options, reason));
        }

        var endpoint = await channel.GetEndpointAsync(cancellationToken).ConfigureAwait(false);

        // A URF printer states in one attribute what an IPP Everywhere printer states in three.
        var isUrf = String.Equals(target, PrinterContentTypes.Urf, StringComparison.OrdinalIgnoreCase);
        var dpi = ResolveDpi(
            options?.ResolutionDpi,
            isUrf ? UrfKeywords.Resolutions(configuration.UrfSupported) : configuration.PwgRasterResolutionsDpi);
        var mediaName = options?.MediaSize ?? configuration.DefaultMediaSize;
        var media = ResolveMedia(options, mediaName);

        PrintConversionContext context = new(
            payload.ContentType,
            target,
            dpi,
            options?.PageRanges,
            channel.Id.ToString())
        {
            RasterType = ResolveRasterType(
                isUrf ? UrfKeywords.RasterTypes(configuration.UrfSupported) : configuration.PwgRasterTypes,
                options?.ColorMode),
            SheetBack = isUrf ? UrfKeywords.SheetBack(configuration.UrfSupported) : configuration.PwgRasterSheetBack,
            Duplex = options?.Duplex,
            MediaName = mediaName,
            MediaSizeNames = configuration.MediaSizes,
            MediaWidthPixels = media?.Width.ToWholePixels(dpi),
            MediaHeightPixels = media?.Height.ToWholePixels(dpi),
            FitArea = ResolveFitArea(options, media, configuration.DefaultMediaMargins, dpi),
            Scaling = options?.Scaling,
            Orientation = options?.Orientation,
            Placement = options?.Placement,
            Smoothing = options?.Smoothing,
            MediaSizeSource = options?.MediaSizeSource ?? MediaSizeSource.Printer,
            DocumentPassword = options?.DocumentPassword,
        };

        var documents = await converter.ConvertAsync(payload.Data.ToArray(), context, cancellationToken).ConfigureAwait(false);

        // Every target this negotiates carries each page in one stream, so one document is
        // the only valid answer. A converter that returned one page each would otherwise
        // have all but the first silently dropped.
        if (documents is not { Count: 1 })
        {
            throw new InvalidOperationException(
                $"The converter of '{payload.ContentType}' returned {documents?.Count ?? 0} documents for '{target}', " +
                $"which carries every page in one. Printer '{channel.Id}' was sent nothing.");
        }

        IppLog.DocumentConverted(channel.Logger, payload.ContentType, endpoint, target);
        IppLog.DocumentConversionSize(channel.Logger, payload.ContentType, endpoint, documents[0].Length, target);

        // The converter selected the pages, so the printer must not select them again -- and
        // where it also placed the page on its media, the printer must not fit it again.
        // The job then asks for the resolution the raster carries, not the one it named.
        // A raster header states its sides, so the job states the same: a queue that
        // defaults to two-sided, as a macOS one does, must not contradict a simplex page.
        var converted = options is null
            ? new PrintOptions()
            : converter.PlacesOnMedia(context)
                ? PrintOptionValidator.WithoutPlacedGeometry(options)
                : PrintOptionValidator.WithoutPageRanges(options);
        converted.Duplex ??= DuplexMode.Simplex;
        DroppedOption[] moved = [];
        if (options?.ResolutionDpi is int requested && requested != dpi)
        {
            converted.ResolutionDpi = dpi;
            moved = [new DroppedOption(
                nameof(PrintOptions.ResolutionDpi),
                PrintOptionStage.Conversion,
                $"the page was rasterized at {dpi} dpi, the resolution chosen from those the printer rasters at")];
        }

        return new IppConversion(PrinterPayload.FromBytes(documents[0], target), target, converted, moved, converter.Name);
    }

    // A raster is only readable at a resolution the printer rasters at, so the request is
    // moved to the nearest one it named rather than sent as asked and refused. One the engine
    // renders well wins over a nearer one it does not; with none such, the converter renders
    // at its limit and scales the page up to the printer's. A printer that named none takes
    // what the job asked for.
    internal static int ResolveDpi(int? requested, IReadOnlyList<int> supported)
    {
        var dpi = requested ?? PrintConversionContext.DefaultDpi;
        if (supported.Count == 0)
        {
            return dpi;
        }

        var renderable = supported.Where(candidate => PdfRenderLimits.ClampDpi(candidate) == candidate).ToList();
        return (renderable.Count > 0 ? renderable : supported).MinBy(candidate => Math.Abs(candidate - dpi));
    }

    // The part of the sheet the page is fitted into. A job that asked for the physical page,
    // a printer that reported no margins, and a margin set that would leave nothing to print
    // on all answer null, which is the whole sheet.
    internal static ImageRectangle? ResolveFitArea(PrintOptions? options, MediaDimensions? media, MediaMargins? margins, int dpi)
    {
        if (media is null || margins is null or { IsEmpty: true })
        {
            return null;
        }

        if (options?.FitArea == PrintFitArea.Physical)
        {
            return null;
        }

        var left = margins.Left.ToPixels(dpi);
        var top = margins.Top.ToPixels(dpi);
        var width = media.Width.ToWholePixels(dpi) - left - margins.Right.ToPixels(dpi);
        var height = media.Height.ToWholePixels(dpi) - top - margins.Bottom.ToPixels(dpi);
        return width > 0 && height > 0 ? new ImageRectangle(left, top, width, height) : null;
    }

    // How large the sheet is, for a converter that composes the page onto it. The dimensions
    // a job carries win over the name, because a name is only as good as the size it encodes;
    // a legacy keyword such as "letter" encodes none, and the converter is then told nothing
    // rather than told a guess.
    private static MediaDimensions? ResolveMedia(PrintOptions? options, string? mediaName)
    {
        if (options?.MediaDimensions is MediaDimensions dimensions)
        {
            return dimensions;
        }

        return PwgMediaNames.TryParse(mediaName, out var parsed) ? parsed : null;
    }

    // Grayscale for a job that asked for it and a printer that offers it, and colour
    // otherwise. A printer that named no type leaves the choice to the converter.
    private static string? ResolveRasterType(IReadOnlyList<string> types, PrintColorMode? colorMode)
    {
        if (types.Count == 0)
        {
            return null;
        }

        if (colorMode == PrintColorMode.Monochrome)
        {
            var gray = types.FirstOrDefault(static type => type.StartsWith("sgray", StringComparison.OrdinalIgnoreCase));
            if (gray is not null)
            {
                return gray;
            }
        }

        return types.FirstOrDefault(static type => type.StartsWith("srgb", StringComparison.OrdinalIgnoreCase)) ?? types[0];
    }

    private static IppConversion Unconverted(PrinterPayload payload, string format, PrintOptions? options, IReadOnlyList<DroppedOption> dropped) =>
        new(payload, format, options, dropped, null);

    // What only a renderer applies, lost on a job that is sent as it is.
    private static DroppedOption[] Unrendered(PrintOptions? options, string reason, params string[] more) =>
        PrintOptionValidator.Unapplied(
            options,
            PrintOptionStage.Conversion,
            reason,
            [nameof(PrintOptions.FitArea), nameof(PrintOptions.Placement), nameof(PrintOptions.Smoothing), nameof(PrintOptions.MediaSizeSource), .. more]);
}

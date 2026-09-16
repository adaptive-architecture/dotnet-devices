namespace AdaptArch.Devices.Printing;

/// <summary>
/// Names the transport that answered, and the endpoint that served a printer operation.
/// </summary>
/// <remarks>
/// A network printer is tried over IPPS (TLS) first and over plain IPP next, so a caller
/// cannot see which one answered from the printer identifier alone. This record makes a
/// downgrade to plain IPP visible.
/// <para>
/// A <c>spooler://</c> queue on Linux and macOS always reports <see cref="PrinterScheme.Ipp"/>:
/// the local CUPS server listens on the IPP socket of the machine, and no TLS attempt is
/// made or needed there. Read this to find a downgrade on a network printer, not on a local
/// queue.
/// </para>
/// See <see href="https://github.com/adaptive-architecture/dotnet-devices/blob/main/docs/troubleshooting.md">Troubleshooting</see>.
/// </remarks>
/// <param name="Scheme">The transport that answered.</param>
/// <param name="Endpoint">The endpoint that answered.</param>
public sealed record PrinterConnection(PrinterScheme Scheme, Uri Endpoint);

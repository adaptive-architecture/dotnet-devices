using System.Diagnostics.CodeAnalysis;

namespace AdaptArch.Devices.Printing.Ipp;

/// <summary>
/// Builds the <see cref="HttpClient"/> that every IPP (Internet Printing Protocol) type in
/// this library uses: the connect timeout, no redirects, and the certificate policy of the
/// <see cref="IppTransportOptions"/>. Build one client here and pass it, with the same
/// options, to every type that must share one connection pool and one trust policy.
/// </summary>
public static class IppHttpClientFactory
{
    /// <summary>
    /// Builds an <see cref="HttpClient"/> from <paramref name="options"/>. The caller owns
    /// the client.
    /// </summary>
    /// <param name="options">The certificate trust and the connect timeout.</param>
    /// <returns>A client that follows no redirect and times out on connect after <see cref="IppTransportOptions.ConnectTimeout"/>.</returns>
    [SuppressMessage(
        "Critical Vulnerability",
        "S4830:Server certificates should be verified during SSL/TLS connections",
        Justification = "Network printers use self-signed certificates in nearly every case, so a validating client reaches almost none of them over IPPS. The accept-all callback is the documented default; a caller that needs trust sets IppTransportOptions.ServerCertificateValidation, and the resolver then refuses the plain IPP fallback. See docs/printers.md.")]
    public static HttpClient Create(IppTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.ConnectTimeout, TimeSpan.Zero);

        SocketsHttpHandler handler = new()
        {
            ConnectTimeout = options.ConnectTimeout,
            // Every IPP operation is a POST that carries the document. A redirect from a
            // printer would re-post that document to another host.
            AllowAutoRedirect = false,
        };
        handler.SslOptions.RemoteCertificateValidationCallback =
            options.ServerCertificateValidation ?? (static (_, _, _, _) => true);
        return new HttpClient(handler, true);
    }
}

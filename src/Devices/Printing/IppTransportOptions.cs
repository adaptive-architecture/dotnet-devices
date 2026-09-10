using System.Net.Security;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Transport policy for every IPP (Internet Printing Protocol) connection this library
/// opens: the certificate trust, the plain IPP fallback, and the connect timeout.
/// </summary>
/// <remarks>
/// Network printers use self-signed certificates in nearly every case, so the default
/// policy accepts every server certificate. That default protects the print data against
/// a passive observer only. Set <see cref="ServerCertificateValidation"/> when the
/// application must know that it talks to the right printer. A thumbprint pin is the
/// usual way, because a printer has no certificate chain to a public root.
/// </remarks>
public sealed class IppTransportOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether a printer that does not answer IPPS (TLS)
    /// is tried over plain IPP next. Defaults to <c>true</c>, because many label printers
    /// speak plain IPP only. Set this to <c>false</c> to talk to IPPS printers only.
    /// </summary>
    public bool AllowPlainIpp { get; set; } = true;

    /// <summary>
    /// Gets or sets the callback that decides whether a printer certificate is trusted.
    /// Defaults to <c>null</c>, which accepts every certificate. When this is set, a
    /// failed TLS handshake ends the connection attempt with an
    /// <see cref="System.Security.Authentication.AuthenticationException"/>, and the
    /// library does not fall back to plain IPP, because clear text would defeat the trust
    /// this callback asks for.
    /// </summary>
    public RemoteCertificateValidationCallback? ServerCertificateValidation { get; set; }

    /// <summary>
    /// Gets or sets the time allowed to open the TCP connection to a printer. Defaults to
    /// five seconds. A host that drops packets fails after this time instead of the
    /// default of the operating system.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets a value indicating whether a TLS failure must end the connection attempt.
    /// True when the caller validates certificates, through <see cref="ServerCertificateValidation"/>
    /// or through an <see cref="HttpClient"/> of its own.
    /// </summary>
    internal bool StrictTls => TrustSupplied || ServerCertificateValidation is not null;

    /// <summary>
    /// Gets a value indicating whether the caller supplied the <see cref="HttpClient"/>.
    /// The library then does not know the certificate policy of that client, so it
    /// treats it as validating.
    /// </summary>
    internal bool TrustSupplied { get; init; }

    /// <summary>
    /// The policy for a caller-supplied <see cref="HttpClient"/>: plain IPP is allowed,
    /// and a TLS failure never falls back to it.
    /// </summary>
    internal static IppTransportOptions ForSuppliedClient() => new() { TrustSupplied = true };
}

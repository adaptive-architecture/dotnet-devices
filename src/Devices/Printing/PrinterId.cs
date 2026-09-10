namespace AdaptArch.Devices.Printing;

/// <summary>
/// Identifies one channel to a printer, written as a URI: <c>{scheme}://{authority}</c>.
/// </summary>
/// <remarks>
/// <para>
/// The scheme names the transport channel, so the identifier itself says how the bytes
/// travel: <c>raw</c>, <c>ipp</c>, <c>ipps</c> or <c>spooler</c>. Nothing has to guess a
/// channel from a port number.
/// </para>
/// <para>
/// The authority is the identity the device reported about itself when there is one, and
/// otherwise the address that opens the channel. An <b>address form</b> can be opened
/// with no discovery, because the scheme gives the endpoint type and the default port. An
/// <b>identity form</b> names no address, so it is resolved through
/// <see cref="IPrinterManager"/>, which knows where the device was found.
/// </para>
/// <para>
/// Only a UUID is written as an identity authority. A serial number reads exactly like a
/// host name or a queue name, so it would make the text ambiguous; it still groups the
/// channels of one device, through <see cref="PrinterDeviceKey"/>.
/// </para>
/// <example>
/// <code>
/// raw://192.168.1.5                              the raw channel, port 9100 implied
/// raw://192.168.1.5:9101                         a port that is not the default
/// ipp://192.168.1.5                              the IPP channel, port 631 implied
/// ipps://[2001:db8::5]:8631                      an IPv6 literal is bracketed
/// ipp://e3b0c442-98fc-1c14-9afb-4c8996fb9242     the identity form
/// spooler://EPSON_L6270                          a print queue of the operating system
/// spooler://%5C%5Cserver%5Cqueue                 a Windows connection name, escaped
/// </code>
/// </example>
/// </remarks>
public readonly struct PrinterId : IEquatable<PrinterId>
{
    private const string SchemeSeparator = "://";

    /// <summary>
    /// The largest identifier this type reads. It bounds the work a malformed value can
    /// cost, and no real address or identity comes close to it.
    /// </summary>
    public const int MaxLength = 512;

    private readonly string? _value;
    private readonly string? _authority;

    private PrinterId(PrinterScheme scheme, string authority, string value, int port, bool isDeviceIdentity)
    {
        Scheme = scheme;
        _authority = authority;
        _value = value;
        Port = port;
        IsDeviceIdentity = isDeviceIdentity;
    }

    /// <summary>
    /// Gets the transport channel this identifier addresses.
    /// </summary>
    public PrinterScheme Scheme { get; }

    /// <summary>
    /// Gets the authority in its canonical written form: escaped, with an IPv6 literal in
    /// brackets, and with a port only when it is not the default of the scheme.
    /// </summary>
    public string Authority => _authority ?? String.Empty;

    /// <summary>
    /// Gets the port the channel uses, or zero for a scheme that has no network port.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Gets a value indicating whether the authority is an identity the device reported,
    /// rather than an address. An identity survives a change of address; an address does
    /// not, but it can be opened with no discovery.
    /// </summary>
    /// <remarks>
    /// This is informational. It takes no part in equality, so an identifier read from
    /// text equals the one a discovery produced.
    /// </remarks>
    public bool IsDeviceIdentity { get; }

    /// <summary>
    /// Gets the key that groups every channel of one device.
    /// </summary>
    /// <remarks>
    /// The port is a detail of the channel, not of the device, so it takes no part in the
    /// key. That is what puts <c>raw://192.168.1.5</c> and <c>ipp://192.168.1.5</c> on one
    /// device when neither reported an identity.
    /// </remarks>
    public PrinterDeviceKey DeviceKey
    {
        get
        {
            var value = _value ?? String.Empty;
            if (IsDeviceIdentity)
            {
                return PrinterDeviceKey.ForDeviceIdentity(value);
            }

            if (Scheme == PrinterScheme.Spooler)
            {
                return PrinterDeviceKey.ForQueue(value);
            }

            return PrinterDeviceKey.ForHost(value);
        }
    }

    /// <summary>
    /// Creates an identifier for the raw TCP channel of a host.
    /// </summary>
    /// <param name="host">The host name or IP address.</param>
    /// <param name="port">The TCP port. Defaults to 9100.</param>
    public static PrinterId ForRaw(string host, int port = NetworkPrinterEndpoint.DefaultPort) =>
        ForNetwork(PrinterScheme.Raw, host, port);

    /// <summary>
    /// Creates an identifier for the IPP channel of a host.
    /// </summary>
    /// <param name="host">The host name or IP address.</param>
    /// <param name="port">The TCP port. Defaults to 631.</param>
    public static PrinterId ForIpp(string host, int port = IppPrinterStatusClient.DefaultPort) =>
        ForNetwork(PrinterScheme.Ipp, host, port);

    /// <summary>
    /// Creates an identifier for the IPP over TLS channel of a host.
    /// </summary>
    /// <param name="host">The host name or IP address.</param>
    /// <param name="port">The TCP port. Defaults to 631.</param>
    public static PrinterId ForIpps(string host, int port = IppPrinterStatusClient.DefaultPort) =>
        ForNetwork(PrinterScheme.Ipps, host, port);

    /// <summary>
    /// Creates an identifier for a network channel whose scheme is known at run time.
    /// </summary>
    /// <param name="scheme">The channel scheme. One of <see cref="PrinterScheme.Raw"/>, <see cref="PrinterScheme.Ipp"/> and <see cref="PrinterScheme.Ipps"/>.</param>
    /// <param name="host">The host name or IP address.</param>
    /// <param name="port">The TCP port.</param>
    /// <exception cref="ArgumentException">Thrown when the scheme is not a network scheme, or the host is not a host.</exception>
    public static PrinterId ForNetwork(PrinterScheme scheme, string host, int port)
    {
        if (!PrinterSchemes.IsNetwork(scheme))
        {
            throw new ArgumentException($"'{PrinterSchemes.Format(scheme)}' does not address a host.", nameof(scheme));
        }

        NetworkPrinterEndpoint.ThrowIfNotAHost(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        return new PrinterId(scheme, FormatNetworkAuthority(scheme, host, port), host, port, false);
    }

    /// <summary>
    /// Creates an identifier for a print queue of the operating system spooler.
    /// </summary>
    /// <param name="queueName">The queue name.</param>
    /// <exception cref="ArgumentException">Thrown when the name is not a valid queue name.</exception>
    public static PrinterId ForSpooler(string queueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        SpoolerPrinterEndpoint.ThrowIfNotAQueueName(queueName, nameof(queueName));
        return new PrinterId(PrinterScheme.Spooler, PrinterIdSyntax.Encode(queueName), queueName, PrinterSchemes.NoPort, false);
    }

    /// <summary>
    /// Creates an identifier from an identity the device reported about itself.
    /// </summary>
    /// <param name="scheme">The channel scheme.</param>
    /// <param name="uuid">The device UUID.</param>
    /// <exception cref="ArgumentException">Thrown when the UUID is empty.</exception>
    public static PrinterId ForDeviceUuid(PrinterScheme scheme, Guid uuid)
    {
        if (uuid == Guid.Empty)
        {
            throw new ArgumentException("An empty UUID identifies no device.", nameof(uuid));
        }

        var value = uuid.ToString("D");
        return new PrinterId(scheme, value, value, PrinterSchemes.DefaultPort(scheme), true);
    }

    /// <summary>
    /// Creates the identifier of the channel an endpoint describes.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    public static PrinterId FromEndpoint(PrinterEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint is NetworkPrinterEndpoint network)
        {
            return ForNetwork(network.Scheme, network.Host, network.Port);
        }

        if (endpoint is SpoolerPrinterEndpoint spooler)
        {
            return ForSpooler(spooler.Name);
        }

        throw new ArgumentException($"Endpoint type '{endpoint.GetType().Name}' has no identifier.", nameof(endpoint));
    }

    /// <summary>
    /// Reads an identifier.
    /// </summary>
    /// <param name="value">The identifier text.</param>
    /// <exception cref="ArgumentException">Thrown when the text is not an identifier.</exception>
    public static PrinterId Parse(string value)
    {
        if (!TryParse(value, out var id))
        {
            throw new ArgumentException($"'{value}' is not a printer identifier. Expected a URI such as 'raw://192.168.1.5' or 'spooler://EPSON_L6270'.", nameof(value));
        }

        return id;
    }

    /// <summary>
    /// Reads an identifier, and reads a text that is not a URI as the raw channel of
    /// that host.
    /// </summary>
    /// <param name="value">The identifier text, or a bare host name or address.</param>
    /// <returns>The identifier that was read.</returns>
    /// <remarks>
    /// This is for a command line or another place where a person types the value, and
    /// where typing <c>192.168.1.5</c> instead of <c>raw://192.168.1.5</c> is convenient.
    /// Use <see cref="Parse"/> or <see cref="TryParse"/> everywhere else, because they
    /// name the channel and never guess it.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when the text is empty or white space.</exception>
    public static PrinterId ParseOrRaw(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return TryParse(value, out var id) ? id : ForRaw(value);
    }

    /// <summary>
    /// Reads an identifier, reporting failure instead of throwing.
    /// </summary>
    /// <param name="value">The identifier text.</param>
    /// <param name="id">The identifier that was read.</param>
    /// <returns><c>true</c> when the text is an identifier.</returns>
    public static bool TryParse(string? value, out PrinterId id)
    {
        id = default;
        if (String.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return false;
        }

        var separator = value.IndexOf(SchemeSeparator, StringComparison.Ordinal);
        if (separator <= 0 || !PrinterSchemes.TryParse(value.AsSpan()[..separator], out var scheme))
        {
            return false;
        }

        var authority = value.AsSpan()[(separator + SchemeSeparator.Length)..];

        // A delimiter here would mean the value carries a path, a query or a fragment,
        // none of which an identifier has.
        if (authority.IsEmpty || PrinterIdSyntax.ContainsReservedDelimiter(authority))
        {
            return false;
        }

        // The identity form is tested first on purpose: a UUID also passes as a host name,
        // so the order is what tells the two apart. An empty UUID is refused rather than
        // read on as a host, because it identifies nothing and would name every device
        // that reports no UUID at all.
        if (Guid.TryParseExact(authority, "D", out var uuid))
        {
            if (uuid == Guid.Empty)
            {
                return false;
            }

            var text = uuid.ToString("D");
            id = new PrinterId(scheme, text, text, PrinterSchemes.DefaultPort(scheme), true);
            return true;
        }

        if (PrinterSchemes.IsNetwork(scheme))
        {
            return TryParseNetwork(scheme, authority, out id);
        }

        return TryParseSpooler(authority, out id);
    }

    private static bool TryParseNetwork(PrinterScheme scheme, ReadOnlySpan<char> authority, out PrinterId id)
    {
        id = default;
        if (!PrinterIdSyntax.TrySplitHostPort(authority, out var host, out var port))
        {
            return false;
        }

        var effective = port == 0 ? PrinterSchemes.DefaultPort(scheme) : port;
        id = new PrinterId(scheme, FormatNetworkAuthority(scheme, host, effective), host, effective, false);
        return true;
    }

    private static bool TryParseSpooler(ReadOnlySpan<char> authority, out PrinterId id)
    {
        id = default;
        if (!PrinterIdSyntax.TryDecode(authority, out var name) || !SpoolerPrinterEndpoint.IsValidName(name))
        {
            return false;
        }

        id = new PrinterId(PrinterScheme.Spooler, PrinterIdSyntax.Encode(name), name, PrinterSchemes.NoPort, false);
        return true;
    }

    private static string FormatNetworkAuthority(PrinterScheme scheme, string host, int port)
    {
        var text = PrinterIdSyntax.FormatHost(host);
        return port == PrinterSchemes.DefaultPort(scheme) ? text : $"{text}:{port}";
    }

    /// <summary>
    /// Reads the host of a network channel.
    /// </summary>
    /// <param name="host">The host name or IP address.</param>
    /// <returns><c>false</c> for an identity form, and for a scheme that addresses no host.</returns>
    public bool TryGetHost(out string host)
    {
        if (!IsDeviceIdentity && PrinterSchemes.IsNetwork(Scheme) && _value is not null)
        {
            host = _value;
            return true;
        }

        host = String.Empty;
        return false;
    }

    /// <summary>
    /// Reads the queue name of a spooler channel.
    /// </summary>
    /// <param name="name">The queue name.</param>
    /// <returns><c>false</c> for an identity form, and for any other scheme.</returns>
    public bool TryGetQueueName(out string name)
    {
        if (!IsDeviceIdentity && Scheme == PrinterScheme.Spooler && _value is not null)
        {
            name = _value;
            return true;
        }

        name = String.Empty;
        return false;
    }

    /// <summary>
    /// Reads the identity the device reported about itself.
    /// </summary>
    /// <param name="identity">The reported identity.</param>
    /// <returns><c>false</c> for an address form.</returns>
    public bool TryGetDeviceIdentity(out string identity)
    {
        if (IsDeviceIdentity && _value is not null)
        {
            identity = _value;
            return true;
        }

        identity = String.Empty;
        return false;
    }

    /// <summary>
    /// Builds the endpoint this identifier opens.
    /// </summary>
    /// <remarks>
    /// This is the whole of the rule that an address form needs no discovery: the scheme
    /// gives the endpoint type and the default port, and the authority gives the address.
    /// An identity form names no address and has to be resolved through
    /// <see cref="IPrinterManager"/> instead.
    /// </remarks>
    /// <param name="endpoint">The endpoint, or <c>null</c> when the identifier names no address.</param>
    /// <returns><c>false</c> for an identity form.</returns>
    public bool TryCreateEndpoint(out PrinterEndpoint? endpoint)
    {
        endpoint = null;
        if (IsDeviceIdentity || _value is null)
        {
            return false;
        }

        if (PrinterSchemes.IsNetwork(Scheme))
        {
            endpoint = new NetworkPrinterEndpoint(_value, Scheme, Port);
            return true;
        }

        if (Scheme == PrinterScheme.Spooler)
        {
            endpoint = new SpoolerPrinterEndpoint(_value);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads a UUID that a printer reported, accepting the <c>urn:uuid:</c> prefix of IPP
    /// and the braces some printers add.
    /// </summary>
    /// <param name="value">The reported text.</param>
    /// <param name="uuid">The UUID that was read.</param>
    /// <returns><c>true</c> when the text holds a UUID that identifies a device.</returns>
    public static bool TryParseDeviceUuid(string? value, out Guid uuid)
    {
        uuid = Guid.Empty;
        if (String.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.StartsWith("urn:uuid:", StringComparison.OrdinalIgnoreCase))
        {
            text = text["urn:uuid:".Length..];
        }

        return Guid.TryParse(text.Trim('{', '}'), out uuid) && uuid != Guid.Empty;
    }

    /// <inheritdoc />
    /// <remarks><see cref="IsDeviceIdentity"/> takes no part, so a parsed identifier equals a discovered one.</remarks>
    public bool Equals(PrinterId other) =>
        Scheme == other.Scheme && String.Equals(Authority, other.Authority, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PrinterId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine((int)Scheme, StringComparer.Ordinal.GetHashCode(Authority));

    /// <inheritdoc />
    public override string ToString() => $"{PrinterSchemes.Format(Scheme)}{SchemeSeparator}{Authority}";

    /// <summary>
    /// Compares two identifiers for equality.
    /// </summary>
    public static bool operator ==(PrinterId left, PrinterId right) => left.Equals(right);

    /// <summary>
    /// Compares two identifiers for inequality.
    /// </summary>
    public static bool operator !=(PrinterId left, PrinterId right) => !left.Equals(right);
}

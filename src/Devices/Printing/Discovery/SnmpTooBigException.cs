namespace AdaptArch.Devices.Printing;

/// <summary>
/// Reports that an SNMP agent answered <c>tooBig</c>: the response would not fit in one
/// datagram. The status client answers it with a smaller request one time. Every other
/// error status stays a plain <see cref="InvalidOperationException"/>.
/// </summary>
internal sealed class SnmpTooBigException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SnmpTooBigException"/> class.
    /// </summary>
    public SnmpTooBigException()
        : base("The SNMP agent reported that the response is too big for one datagram.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SnmpTooBigException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public SnmpTooBigException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SnmpTooBigException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The cause.</param>
    public SnmpTooBigException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

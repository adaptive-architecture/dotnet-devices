using DotNetSnmp.Asn1.SyntaxObjects;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// A decoded SNMP response.
/// </summary>
internal sealed class SnmpReply
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SnmpReply"/> class.
    /// </summary>
    /// <param name="requestId">The request identifier that the agent echoed.</param>
    /// <param name="variables">The variable bindings.</param>
    public SnmpReply(int requestId, IReadOnlyList<Variable> variables)
    {
        RequestId = requestId;
        Variables = variables;
    }

    /// <summary>
    /// Gets the request identifier that the agent echoed.
    /// </summary>
    public int RequestId { get; }

    /// <summary>
    /// Gets the variable bindings.
    /// </summary>
    public IReadOnlyList<Variable> Variables { get; }
}

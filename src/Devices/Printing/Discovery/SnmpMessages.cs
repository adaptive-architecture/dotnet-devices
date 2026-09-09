using DotNetSnmp.Asn1.Serialization;
using DotNetSnmp.Asn1.SyntaxObjects;
using DotNetSnmp.Common.Definitions;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;

namespace AdaptArch.Devices.Printing;

/// <summary>
/// Writes and reads SNMP version 2c messages with <c>Lextm.SharpSnmpLib</c>.
/// </summary>
/// <remarks>
/// Only the message classes of that library are used, not its <c>Messenger</c> helpers.
/// The datagrams travel over <see cref="IUdpChannel"/> instead, which keeps the timeout
/// and the retry behaviour in our own hands.
/// <para>
/// The response is read through <c>Scope.Pdu</c> and never through the
/// <c>SnmpMessageCompatibilityExtensions</c> shim. That shim reads the protocol data unit
/// by reflection, which raises trim warning IL2075 in an application that trims or uses
/// native AOT. This package sets <c>IsAotCompatible</c>, so the shim must not be used.
/// </para>
/// </remarks>
internal static class SnmpMessages
{
    /// <summary>
    /// Writes a GetRequest that reads a set of object identifiers.
    /// </summary>
    /// <param name="community">The community string.</param>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="oids">The object identifiers to read.</param>
    /// <returns>The bytes of the request.</returns>
    public static byte[] CreateGetRequest(string community, int requestId, IReadOnlyList<string> oids) =>
        new GetRequestMessage(requestId, VersionCode.V2, new OctetString(community), ToVariables(oids)).ToBytes();

    /// <summary>
    /// Writes a GetBulkRequest that reads the rows after a set of object identifiers.
    /// </summary>
    /// <param name="community">The community string.</param>
    /// <param name="requestId">The request identifier.</param>
    /// <param name="oids">The object identifiers to continue from.</param>
    /// <param name="maxRepetitions">How many rows to read for each identifier.</param>
    /// <returns>The bytes of the request.</returns>
    public static byte[] CreateGetBulkRequest(string community, int requestId, IReadOnlyList<string> oids, int maxRepetitions) =>
        new GetBulkRequestMessage(requestId, VersionCode.V2, new OctetString(community), 0, maxRepetitions, ToVariables(oids)).ToBytes();

    /// <summary>
    /// Reads an SNMP response.
    /// </summary>
    /// <param name="payload">The bytes of the response.</param>
    /// <returns>The decoded response.</returns>
    /// <exception cref="InvalidDataException">Thrown when the message is malformed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the agent reported an error status.</exception>
    public static SnmpReply Parse(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        IList<ISnmpMessage> messages;
        try
        {
            messages = MessageFactory.ParseMessages(payload, new UserRegistry());
        }
        catch (Exception exception) when (exception is SnmpDecodeException or SnmpException or ArgumentException or FormatException or IndexOutOfRangeException)
        {
            throw new InvalidDataException("The SNMP response is malformed.", exception);
        }

        if (messages.Count == 0)
        {
            throw new InvalidDataException("The SNMP response holds no message.");
        }

        var message = messages[0];
        var pdu = message.Scope?.Pdu;
        if (pdu is null)
        {
            throw new InvalidDataException("The SNMP response holds no protocol data unit.");
        }

        if (pdu.ErrorStatus != ErrorCode.NoError)
        {
            throw new InvalidOperationException(
                $"The SNMP agent reported error status {pdu.ErrorStatus} at index {pdu.ErrorIndex}.");
        }

        return new SnmpReply(pdu.RequestId, [.. pdu.Variables]);
    }

    private static List<Variable> ToVariables(IReadOnlyList<string> oids)
    {
        List<Variable> variables = new(oids.Count);
        for (var i = 0; i < oids.Count; i++)
        {
            variables.Add(new Variable(oids[i], Null.Instance));
        }

        return variables;
    }
}

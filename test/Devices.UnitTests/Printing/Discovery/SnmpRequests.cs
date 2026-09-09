using DotNetSnmp.Asn1.SyntaxObjects;
using DotNetSnmp.Common.Definitions;
using DotNetSnmp.Protocol.V2;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

// Reads a request that the client produced, so a test can assert the wire fields and so
// the fake channel can echo the request identifier the way a real agent does.
internal static class SnmpRequests
{
    public static ISnmpMessage Parse(byte[] payload) =>
        MessageFactory.ParseMessages(payload, new UserRegistry())[0];

    public static int ReadRequestId(byte[] payload) => Parse(payload).Scope.Pdu.RequestId;

    public static SnmpType ReadType(byte[] payload) => Parse(payload).Scope.Pdu.TypeCode;

    // Version 2c carries the community string in the user name field.
    public static string ReadCommunity(byte[] payload) => Parse(payload).Parameters.UserName.ToString();

    public static IReadOnlyList<string> ReadOids(byte[] payload)
    {
        List<string> oids = [];
        foreach (var variable in Parse(payload).Scope.Pdu.Variables)
        {
            oids.Add(variable.Id.ToString());
        }

        return oids;
    }

    // In version 13 a GetBulkRequest keeps these two as named properties of its own
    // protocol data unit, rather than in the error fields that older versions reused.
    public static int ReadMaxRepetitions(byte[] payload) => BulkPdu(payload).MaxRepetitions;

    public static int ReadNonRepeaters(byte[] payload) => BulkPdu(payload).NonRepeaters;

    private static GetBulkRequestPdu BulkPdu(byte[] payload) =>
        (GetBulkRequestPdu)Parse(payload).Scope.Pdu;
}

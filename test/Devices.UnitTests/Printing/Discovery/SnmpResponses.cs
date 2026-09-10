using System.Formats.Asn1;
using DotNetSnmp.Asn1.Serialization;
using DotNetSnmp.Asn1.SyntaxObjects;
using DotNetSnmp.Common.Definitions;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

// Builds SNMP version 2c responses with the library the client reads them with, so the
// tests exercise the real wire format.
internal static class SnmpResponses
{
    public static byte[] Response(int requestId, params Variable[] variables) =>
        Response(requestId, 0, 0, variables);

    public static byte[] Response(int requestId, int errorStatus, int errorIndex, params Variable[] variables)
    {
        // ResponseMessage is marked internal-use-only, but a test needs an agent's bytes.
#pragma warning disable CS0618
        return new ResponseMessage(
            requestId,
            VersionCode.V2,
            new OctetString("public"),
            (ErrorCode)errorStatus,
            errorIndex,
            variables).ToBytes();
#pragma warning restore CS0618
    }

    // Answers a GetBulkRequest as RFC 3416 §4.2.3 describes: one repetition at a time,
    // the successor of each identifier, and endOfMibView once a cursor runs out.
    public static byte[] BulkResponse(byte[] request, IReadOnlyList<Variable> mib)
    {
        var cursors = SnmpRequests.ReadOids(request).Select(static oid => new ObjectIdentifier(oid)).ToList();
        var repetitions = SnmpRequests.ReadMaxRepetitions(request);
        var sorted = mib.OrderBy(static variable => variable.Id).ToList();
        List<Variable> variables = [];
        for (var repetition = 0; repetition < repetitions; repetition++)
        {
            for (var cursor = 0; cursor < cursors.Count; cursor++)
            {
                var next = sorted.FindIndex(variable => variable.Id.CompareTo(cursors[cursor]) > 0);
                if (next < 0)
                {
                    variables.Add(EndOfMibView(cursors[cursor].ToString()));
                    continue;
                }

                variables.Add(sorted[next]);
                cursors[cursor] = sorted[next].Id;
            }
        }

        return Response(SnmpRequests.ReadRequestId(request), [.. variables]);
    }

    public static Variable Text(string oid, string value) => new(oid, new OctetString(value));

    public static Variable Bytes(string oid, byte[] value) => new(oid, new OctetString(value));

    public static Variable Integer(string oid, int value) => new(oid, new Integer32(value));

    public static Variable Counter(string oid, long value) => new(oid, new Counter32(value));

    public static Variable Gauge(string oid, long value) => new(oid, new Gauge32(value));

    public static Variable Ticks(string oid, uint value) => new(oid, new TimeTicks(value));

    // These three have no public constructor, so they are built from the parser singleton.
    public static Variable NoSuchObject(string oid) => new(oid, Absent(SnmpType.NoSuchObject));

    public static Variable NoSuchInstance(string oid) => new(oid, Absent(SnmpType.NoSuchInstance));

    public static Variable EndOfMibView(string oid) => new(oid, Absent(SnmpType.EndOfMibView));

    public static Variable Null(string oid) => new(oid, DotNetSnmp.Asn1.SyntaxObjects.Null.Instance);

    private static AbsentValue Absent(SnmpType typeCode)
    {
        if (typeCode == SnmpType.NoSuchObject)
        {
            return new AbsentValue(typeCode, 0);
        }

        if (typeCode == SnmpType.NoSuchInstance)
        {
            return new AbsentValue(typeCode, 1);
        }

        return new AbsentValue(typeCode, 2);
    }

    // Each marker is a context-specific tag with no content on the wire.
    private sealed class AbsentValue : IAsnSerializable
    {
        private readonly int _tagNumber;

        public AbsentValue(SnmpType typeCode, int tagNumber)
        {
            TypeCode = typeCode;
            _tagNumber = tagNumber;
        }

        public SnmpType TypeCode { get; }

        public void WriteTo(AsnWriter writer) =>
            writer.WriteNull(new Asn1Tag(TagClass.ContextSpecific, _tagNumber));
    }
}

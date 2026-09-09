using System.Formats.Asn1;
using DotNetSnmp.Asn1.Serialization;
using DotNetSnmp.Asn1.SyntaxObjects;
using DotNetSnmp.Common.Definitions;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;

namespace AdaptArch.Devices.UnitTests.Printing.Discovery;

// Builds SNMP version 2c responses with the same library the client reads them with. The
// tests therefore exercise the real wire format without needing an agent on the network.
internal static class SnmpResponses
{
    public static byte[] Response(int requestId, params Variable[] variables) =>
        Response(requestId, 0, 0, variables);

    public static byte[] Response(int requestId, int errorStatus, int errorIndex, params Variable[] variables)
    {
        // 13.0.0-beta.3 marks ResponseMessage as internal-use-only, but a test needs to
        // produce the bytes an agent would send. Nothing here ships in a package.
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

    public static Variable Text(string oid, string value) => new(oid, new OctetString(value));

    public static Variable Bytes(string oid, byte[] value) => new(oid, new OctetString(value));

    public static Variable Integer(string oid, int value) => new(oid, new Integer32(value));

    public static Variable Counter(string oid, long value) => new(oid, new Counter32(value));

    public static Variable Gauge(string oid, long value) => new(oid, new Gauge32(value));

    public static Variable Ticks(string oid, uint value) => new(oid, new TimeTicks(value));

    // 13.0.0-beta.3 gives NoSuchObject, NoSuchInstance and EndOfMibView no public
    // constructor, so these three are built from the singleton that the parser produces.
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

    // The three markers say "there is no value here". On the wire each is a
    // context-specific tag with no content, which is what the client must decode.
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

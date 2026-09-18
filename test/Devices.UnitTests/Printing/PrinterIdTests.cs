using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrinterIdTests
{
    [Fact]
    public void ForRaw_NamesTheRawChannelAndItsDefaultPort()
    {
        var id = PrinterId.ForRaw("192.168.1.50");
        Assert.Equal(PrinterScheme.Raw, id.Scheme);
        Assert.Equal("192.168.1.50", id.Authority);
        Assert.Equal(9100, id.Port);
        Assert.Equal("raw://192.168.1.50", id.ToString());
    }

    [Fact]
    public void ForIpp_NamesTheIppChannelAndItsDefaultPort()
    {
        var id = PrinterId.ForIpp("printer.local");
        Assert.Equal(PrinterScheme.Ipp, id.Scheme);
        Assert.Equal(631, id.Port);
        Assert.Equal("ipp://printer.local", id.ToString());
    }

    [Fact]
    public void ForSpooler_EscapesAQueueNameThatNeedsIt()
    {
        var id = PrinterId.ForSpooler("Front Desk");
        Assert.Equal(PrinterScheme.Spooler, id.Scheme);
        Assert.Equal("spooler://Front%20Desk", id.ToString());
        Assert.True(id.TryGetQueueName(out var name));
        Assert.Equal("Front Desk", name);
    }

    [Fact]
    public void ForDeviceUuid_IsAnIdentityFormThatKeepsTheSchemeDefaultPort()
    {
        var uuid = Guid.Parse("e3b0c442-98fc-1c14-9afb-4c8996fb9242");
        var id = PrinterId.ForDeviceUuid(PrinterScheme.Ipp, uuid);
        Assert.True(id.IsDeviceIdentity);
        Assert.Equal("ipp://e3b0c442-98fc-1c14-9afb-4c8996fb9242", id.ToString());
        Assert.Equal(631, id.Port);
    }

    [Fact]
    public void ForDeviceUuid_WritesAPortThatIsNotTheDefaultOfTheScheme()
    {
        var uuid = Guid.Parse("e3b0c442-98fc-1c14-9afb-4c8996fb9242");
        var id = PrinterId.ForDeviceUuid(PrinterScheme.Ipps, uuid, 443);

        Assert.True(id.IsDeviceIdentity);
        Assert.Equal("ipps://e3b0c442-98fc-1c14-9afb-4c8996fb9242:443", id.ToString());
        Assert.Equal(443, id.Port);
    }

    [Fact]
    public void ForDeviceUuid_TellsTwoChannelsOfOneDeviceApartByTheirPort()
    {
        var uuid = Guid.Parse("e3b0c442-98fc-1c14-9afb-4c8996fb9242");
        var secure = PrinterId.ForDeviceUuid(PrinterScheme.Ipp, uuid, 443);
        var plain = PrinterId.ForDeviceUuid(PrinterScheme.Ipp, uuid, 631);

        Assert.NotEqual(secure, plain);

        // The port names the channel and not the device, so the two are still one device.
        Assert.Equal(secure.DeviceKey, plain.DeviceKey);
    }

    [Fact]
    public void TryParse_ReadsAnIdentityFormThatCarriesAPort()
    {
        Assert.True(PrinterId.TryParse("ipps://e3b0c442-98fc-1c14-9afb-4c8996fb9242:443", out var id));

        Assert.True(id.IsDeviceIdentity);
        Assert.Equal(443, id.Port);
        Assert.True(id.TryGetDeviceIdentity(out var identity));
        Assert.Equal("e3b0c442-98fc-1c14-9afb-4c8996fb9242", identity);
        Assert.False(id.TryGetHost(out _));
    }

    [Fact]
    public void TryParse_ReadsBackWhatForDeviceUuidWrote()
    {
        var uuid = Guid.Parse("e3b0c442-98fc-1c14-9afb-4c8996fb9242");
        foreach (var port in new[] { 443, 631, 8631 })
        {
            var written = PrinterId.ForDeviceUuid(PrinterScheme.Ipps, uuid, port);

            Assert.True(PrinterId.TryParse(written.ToString(), out var read));
            Assert.Equal(written, read);
            Assert.Equal(port, read.Port);
            Assert.True(read.IsDeviceIdentity);
        }
    }

    [Fact]
    public void ForDeviceUuid_RefusesAPortNoChannelUses()
    {
        var uuid = Guid.Parse("e3b0c442-98fc-1c14-9afb-4c8996fb9242");

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => PrinterId.ForDeviceUuid(PrinterScheme.Ipp, uuid, 0));
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => PrinterId.ForDeviceUuid(PrinterScheme.Ipp, uuid, 70000));
    }

    [Fact]
    public void ForDeviceUuid_RefusesAnEmptyUuid() =>
        Assert.Throws<ArgumentException>(() => PrinterId.ForDeviceUuid(PrinterScheme.Ipp, Guid.Empty));

    [Theory]
    [InlineData("raw://192.168.1.5", PrinterScheme.Raw, "192.168.1.5", 9100)]
    [InlineData("raw://192.168.1.5:9101", PrinterScheme.Raw, "192.168.1.5:9101", 9101)]
    [InlineData("ipp://192.168.1.5", PrinterScheme.Ipp, "192.168.1.5", 631)]
    [InlineData("ipps://printer.local", PrinterScheme.Ipps, "printer.local", 631)]
    [InlineData("ipps://[2001:db8::5]:8631", PrinterScheme.Ipps, "[2001:db8::5]:8631", 8631)]
    [InlineData("RAW://192.168.1.5", PrinterScheme.Raw, "192.168.1.5", 9100)]
    public void Parse_ReadsANetworkAddress(string text, PrinterScheme scheme, string authority, int port)
    {
        var id = PrinterId.Parse(text);
        Assert.Equal(scheme, id.Scheme);
        Assert.Equal(authority, id.Authority);
        Assert.Equal(port, id.Port);
        Assert.False(id.IsDeviceIdentity);
    }

    [Fact]
    public void Parse_DropsAPortThatIsTheDefaultOfTheScheme() =>
        Assert.Equal("raw://192.168.1.5", PrinterId.Parse("raw://192.168.1.5:9100").ToString());

    [Fact]
    public void Parse_ReadsAnIdentityBeforeItReadsAHost()
    {
        // A UUID also passes as a host name, so the order is what tells the two apart.
        var id = PrinterId.Parse("ipp://e3b0c442-98fc-1c14-9afb-4c8996fb9242");
        Assert.True(id.IsDeviceIdentity);
        Assert.False(id.TryGetHost(out _));
    }

    [Fact]
    public void Parse_ReadsAnIPv6LiteralWithoutItsBrackets()
    {
        var id = PrinterId.Parse("ipps://[2001:db8::5]:8631");
        Assert.True(id.TryGetHost(out var host));
        Assert.Equal("2001:db8::5", host);
    }

    [Theory]
    [InlineData("spooler://EPSON_L6270", "EPSON_L6270")]
    [InlineData("spooler://Front%20Desk", "Front Desk")]
    [InlineData("spooler://%5C%5Csrv%5CHP", "\\\\srv\\HP")]
    [InlineData("spooler://a%3Ab", "a:b")]
    public void Parse_ReadsASpoolerQueueName(string text, string expected)
    {
        var id = PrinterId.Parse(text);
        Assert.True(id.TryGetQueueName(out var name));
        Assert.Equal(expected, name);
        Assert.Equal(0, id.Port);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("192.168.1.5")]
    [InlineData("://192.168.1.5")]
    [InlineData("ftp://192.168.1.5")]
    [InlineData("raw://")]
    [InlineData("raw://printer.example:80/ipp/print#")]
    [InlineData("raw://host/path")]
    [InlineData("raw://host?query")]
    [InlineData("raw://user@host")]
    [InlineData("raw://host:0")]
    [InlineData("raw://host:65536")]
    [InlineData("raw://2001:db8::5")]
    [InlineData("ipp://urn:uuid:e3b0c442-98fc-1c14-9afb-4c8996fb9242")]
    [InlineData("ipp://00000000-0000-0000-0000-000000000000")]
    [InlineData("usb://04b8-0e15")]
    [InlineData("usb://X1J123456")]
    [InlineData("spooler://a%2Fb")]
    [InlineData("spooler://%zz")]
    public void TryParse_RefusesSomethingThatIsNotAnIdentifier(string text) =>
        Assert.False(PrinterId.TryParse(text, out _));

    [Fact]
    public void TryParse_RefusesAValueLongerThanTheCap() =>
        Assert.False(PrinterId.TryParse("spooler://" + new string('a', PrinterId.MaxLength), out _));

    [Fact]
    public void Parse_ThrowsAndNamesTheExpectedShape()
    {
        var exception = Assert.Throws<ArgumentException>(() => PrinterId.Parse("not-an-identifier"));
        Assert.Contains("raw://192.168.1.5", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("raw://192.168.1.5")]
    [InlineData("raw://192.168.1.5:9101")]
    [InlineData("ipp://printer.local")]
    [InlineData("ipps://[2001:db8::5]:8631")]
    [InlineData("spooler://EPSON_L6270")]
    [InlineData("spooler://%5C%5Csrv%5CHP")]
    [InlineData("ipp://e3b0c442-98fc-1c14-9afb-4c8996fb9242")]
    public void ToStringThenParse_RoundTrips(string text) =>
        Assert.Equal(text, PrinterId.Parse(text).ToString());

    [Fact]
    public void TwoIdentifiersOfDifferentSchemesAreNotEqual()
    {
        Assert.NotEqual(PrinterId.ForSpooler("A"), PrinterId.ForRaw("A"));
        Assert.True(PrinterId.ForSpooler("A") != PrinterId.ForRaw("A"));
        Assert.False(PrinterId.ForSpooler("A") == PrinterId.ForRaw("A"));
    }

    [Fact]
    public void AParsedIdentifierEqualsTheOneItWasBuiltFrom()
    {
        var built = PrinterId.ForRaw("192.168.1.5");
        var parsed = PrinterId.Parse("raw://192.168.1.5");
        Assert.Equal(built, parsed);
        Assert.Equal(built.GetHashCode(), parsed.GetHashCode());
    }

    [Fact]
    public void TheTwoNetworkChannelsOfOneHostShareADeviceKey()
    {
        // The port belongs to the channel, not to the device.
        Assert.Equal(PrinterId.ForRaw("192.168.1.5").DeviceKey, PrinterId.ForIpp("192.168.1.5").DeviceKey);
        Assert.Equal(PrinterId.ForRaw("192.168.1.5", 9101).DeviceKey, PrinterId.ForRaw("192.168.1.5").DeviceKey);
        Assert.NotEqual(PrinterId.ForRaw("192.168.1.5"), PrinterId.ForIpp("192.168.1.5"));
    }

    [Fact]
    public void TheDeviceKeyOfAnIdentityFormIsTheIdentity()
    {
        var uuid = Guid.Parse("e3b0c442-98fc-1c14-9afb-4c8996fb9242");
        var raw = PrinterId.ForDeviceUuid(PrinterScheme.Raw, uuid);
        var ipp = PrinterId.ForDeviceUuid(PrinterScheme.Ipp, uuid);
        Assert.Equal(raw.DeviceKey, ipp.DeviceKey);
        Assert.True(raw.DeviceKey.IsDeviceIdentity);
    }

    [Theory]
    [InlineData("raw://192.168.1.5", typeof(NetworkPrinterEndpoint))]
    [InlineData("ipp://printer.local", typeof(NetworkPrinterEndpoint))]
    [InlineData("spooler://EPSON_L6270", typeof(SpoolerPrinterEndpoint))]
    public void TryCreateEndpoint_BuildsTheEndpointAnAddressFormNames(string text, Type expected)
    {
        Assert.True(PrinterId.Parse(text).TryCreateEndpoint(out var endpoint));
        Assert.IsType(expected, endpoint);
    }

    [Fact]
    public void TryCreateEndpoint_KeepsTheSchemeAndThePort()
    {
        Assert.True(PrinterId.Parse("ipps://printer.local:8631").TryCreateEndpoint(out var endpoint));
        var network = Assert.IsType<NetworkPrinterEndpoint>(endpoint);
        Assert.Equal(PrinterScheme.Ipps, network.Scheme);
        Assert.Equal(8631, network.Port);
        Assert.Equal("printer.local", network.Host);
    }

    [Theory]
    [InlineData("ipp://e3b0c442-98fc-1c14-9afb-4c8996fb9242")]
    public void TryCreateEndpoint_RefusesAnIdentityFormBecauseItNamesNoAddress(string text) =>
        Assert.False(PrinterId.Parse(text).TryCreateEndpoint(out _));

    [Fact]
    public void FromEndpoint_ReadsTheChannelBack()
    {
        Assert.Equal(PrinterId.ForIpp("printer.local"), PrinterId.FromEndpoint(NetworkPrinterEndpoint.Ipp("printer.local")));
        Assert.Equal(PrinterId.ForRaw("printer.local"), PrinterId.FromEndpoint(NetworkPrinterEndpoint.Raw("printer.local")));
        Assert.Equal(PrinterId.ForSpooler("Lobby"), PrinterId.FromEndpoint(new SpoolerPrinterEndpoint("Lobby")));
    }

    [Theory]
    [InlineData("urn:uuid:E3B0C442-98FC-1C14-9AFB-4C8996FB9242")]
    [InlineData("{e3b0c442-98fc-1c14-9afb-4c8996fb9242}")]
    [InlineData("e3b0c442-98fc-1c14-9afb-4c8996fb9242")]
    public void TryParseDeviceUuid_AcceptsWhatAPrinterActuallyReports(string value)
    {
        Assert.True(PrinterId.TryParseDeviceUuid(value, out var uuid));
        Assert.Equal(Guid.Parse("e3b0c442-98fc-1c14-9afb-4c8996fb9242"), uuid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("urn:uuid:00000000-0000-0000-0000-000000000000")]
    public void TryParseDeviceUuid_RefusesWhatIdentifiesNoDevice(string value) =>
        Assert.False(PrinterId.TryParseDeviceUuid(value, out _));

    [Fact]
    public void ParseOrRaw_ReadsAnIdentifier()
    {
        var id = PrinterId.ParseOrRaw("spooler://EPSON_L6270");

        Assert.Equal(PrinterScheme.Spooler, id.Scheme);
        Assert.Equal("EPSON_L6270", id.Authority);
    }

    // Typing a bare address is convenient, so it is read as the raw channel of that host.
    [Theory]
    [InlineData("192.168.1.5", "raw://192.168.1.5")]
    [InlineData("printer.local", "raw://printer.local")]
    public void ParseOrRaw_ReadsABareAddressAsTheRawChannel(string value, string expected) =>
        Assert.Equal(expected, PrinterId.ParseOrRaw(value).ToString());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ParseOrRaw_RefusesAnEmptyText(string value) =>
        Assert.ThrowsAny<ArgumentException>(() => PrinterId.ParseOrRaw(value));

    [Theory]
    [InlineData("cups://printsrv/lobby", "printsrv/lobby", 631, "lobby")]
    [InlineData("cups://printsrv:8631/lobby", "printsrv:8631/lobby", 8631, "lobby")]
    [InlineData("cups://printsrv:631/lobby", "printsrv/lobby", 631, "lobby")]
    [InlineData("cups://[2001:db8::5]/lobby", "[2001:db8::5]/lobby", 631, "lobby")]
    [InlineData("cups://printsrv/Front%20Desk", "printsrv/Front%20Desk", 631, "Front Desk")]
    public void TryParse_ReadsACupsQueue(string value, string authority, int port, string queue)
    {
        Assert.True(PrinterId.TryParse(value, out var id));

        Assert.Equal(PrinterScheme.Cups, id.Scheme);
        Assert.Equal(authority, id.Authority);
        Assert.Equal(port, id.Port);
        Assert.True(id.TryGetQueueName(out var name));
        Assert.Equal(queue, name);
    }

    // The queue is the whole of the path, so a name that escaped to more than one segment
    // is refused rather than truncated to the part before the second slash.
    [Theory]
    [InlineData("cups://printsrv")]
    [InlineData("cups://printsrv/")]
    [InlineData("cups:///lobby")]
    [InlineData("cups://printsrv/a/b")]
    [InlineData("cups://printsrv/lobby?x")]
    [InlineData("cups://printsrv:0/lobby")]
    [InlineData("cups://printsrv:70000/lobby")]
    public void TryParse_RefusesAMalformedCupsIdentifier(string value) =>
        Assert.False(PrinterId.TryParse(value, out _));

    [Fact]
    public void ForCups_WritesTheHostAndTheEscapedQueue()
    {
        var id = PrinterId.ForCups("printsrv", "Front Desk");

        Assert.Equal("cups://printsrv/Front%20Desk", id.ToString());
        Assert.True(id.TryGetHost(out var host));
        Assert.Equal("printsrv", host);
    }

    [Fact]
    public void ForCups_RoundTripsThroughItsEndpoint()
    {
        var id = PrinterId.ForCups("printsrv", "lobby", 8631);

        Assert.True(id.TryCreateEndpoint(out var endpoint));
        var cups = Assert.IsType<CupsPrinterEndpoint>(endpoint);
        Assert.Equal("printsrv", cups.Host);
        Assert.Equal("lobby", cups.Name);
        Assert.Equal(8631, cups.Port);
        Assert.Equal(id, PrinterId.FromEndpoint(cups));
    }

    // One server holds many queues, so its host alone would fuse every printer behind it
    // into one device.
    [Fact]
    public void DeviceKey_OfACupsQueueNamesTheServerAndTheQueue()
    {
        var lobby = PrinterId.ForCups("printsrv", "lobby").DeviceKey;
        var desk = PrinterId.ForCups("printsrv", "desk").DeviceKey;
        var elsewhere = PrinterId.ForCups("other", "lobby").DeviceKey;

        Assert.Equal(PrinterDeviceKeyKind.Queue, lobby.Kind);
        Assert.NotEqual(lobby, desk);
        Assert.NotEqual(lobby, elsewhere);

        // The port names the channel and not the device, as for every other scheme.
        Assert.Equal(lobby, PrinterId.ForCups("printsrv", "lobby", 8631).DeviceKey);
    }

    // A queue of a server is not a device, so there is nothing a reported UUID could
    // stand in for.
    [Fact]
    public void ForDeviceUuid_RefusesTheCupsScheme() =>
        Assert.Throws<ArgumentException>(() => PrinterId.ForDeviceUuid(PrinterScheme.Cups, Guid.NewGuid()));
}

#nullable enable
using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Ipp;
using SharpIpp.Protocol;
using SharpIpp.Protocol.Models;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

/// <summary>
/// The corners no other test walks through: a decorator's pass-through members, the
/// constructors of an exception that only one path throws, and a default interface method
/// every implementer in this library overrides.
/// </summary>
public class SmallGapsTests
{
    [Fact]
    public void SnmpTooBigException_CarriesTheMessageAndTheCause()
    {
        SnmpTooBigException fromNothing = new();
        Assert.Contains("too big", fromNothing.Message, StringComparison.OrdinalIgnoreCase);

        SnmpTooBigException fromMessage = new("the walk asked for too much at once");
        Assert.Equal("the walk asked for too much at once", fromMessage.Message);

        InvalidOperationException cause = new("the agent said so");
        SnmpTooBigException fromCause = new("too big", cause);
        Assert.Same(cause, fromCause.InnerException);

        // It is caught as one, because the retry path handles it and every other SNMP
        // error status stays a plain InvalidOperationException.
        _ = Assert.IsType<InvalidOperationException>(fromNothing, exactMatch: false);
    }

    [Fact]
    public async Task IPrinter_APrinterThatNamesNoIdentity_AnswersNullRatherThanFailing()
    {
        // The default of the interface. Every printer in this library overrides it, so a
        // channel added outside must not have to: an unknown identity is not an error.
        IPrinter printer = new IdentitylessPrinter();

        Assert.Null(await printer.GetIdentityAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CapturingIppProtocol_KeepsTheResponseAndPassesEverythingElseOn()
    {
        FakeIppProtocol inner = new();
        CapturingIppProtocol capturing = new(inner);

        // Nothing has been read, so there is nothing to report.
        Assert.Null(capturing.Response);

        await using MemoryStream stream = new();
        var response = await capturing.ReadIppResponseAsync(stream, TestContext.Current.CancellationToken);

        // The raw answer is kept, which is the whole reason this decorator exists: the
        // typed model drops the marker attributes that report ink and toner.
        Assert.Same(inner.Response, capturing.Response);
        Assert.Same(inner.Response, response);
    }

    [Fact]
    public async Task CapturingIppProtocol_TheServerSideGoesStraightThrough()
    {
        FakeIppProtocol inner = new();
        CapturingIppProtocol capturing = new(inner);
        await using MemoryStream stream = new();
        var cancellationToken = TestContext.Current.CancellationToken;

        _ = await capturing.ReadIppRequestAsync(stream, cancellationToken);
        await capturing.WriteIppRequestAsync(inner.Request, stream, cancellationToken);
        await capturing.WriteIppResponseAsync(inner.Response, stream, cancellationToken);

        Assert.Equal(["ReadIppRequest", "WriteIppRequest", "WriteIppResponse"], inner.Calls);

        // Only a response read is captured. A request this library never sends as a server
        // must not be mistaken for one.
        Assert.Null(capturing.Response);
    }

    [Fact]
    public void CapturingIppProtocol_TheLimitsBelongToTheReaderItWraps()
    {
        FakeIppProtocol inner = new();
        CapturingIppProtocol capturing = new(inner)
        {
            MaxDocumentStreamBytes = 1024,
            MaxMessageAttributesBytes = 2048,
            MaxMessageAttributesCount = 64,
            ReadDocumentStream = true,
        };

        // Set through the decorator, read off the reader: a limit stored on the wrapper
        // would be one the parser never applies.
        Assert.Equal(1024, inner.MaxDocumentStreamBytes);
        Assert.Equal(2048, inner.MaxMessageAttributesBytes);
        Assert.Equal(64, inner.MaxMessageAttributesCount);
        Assert.True(inner.ReadDocumentStream);

        Assert.Equal(1024, capturing.MaxDocumentStreamBytes);
        Assert.Equal(2048, capturing.MaxMessageAttributesBytes);
        Assert.Equal(64, capturing.MaxMessageAttributesCount);
        Assert.True(capturing.ReadDocumentStream);
    }

    private sealed class IdentitylessPrinter : IPrinter
    {
        public PrinterId Id => PrinterId.ForRaw("127.0.0.1");

        public PrinterEndpoint Endpoint => NetworkPrinterEndpoint.Raw("127.0.0.1");

        public PrinterInfo Info => new(Id, "nameless");

        public Task<PrintJobInfo> PrintAsync(PrinterPayload payload, PrintOptions? options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PrinterStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PrinterConfiguration> GetConfigurationAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeIppProtocol : IIppProtocol
    {
        public List<string> Calls { get; } = [];

        public IIppRequestMessage Request { get; } = new IppRequestMessage();

        public IIppResponseMessage Response { get; } = new IppResponseMessage();

        public long? MaxDocumentStreamBytes { get; set; }

        public long? MaxMessageAttributesBytes { get; set; }

        public int? MaxMessageAttributesCount { get; set; }

        public bool ReadDocumentStream { get; set; }

        public Task<IIppRequestMessage> ReadIppRequestAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            Calls.Add("ReadIppRequest");
            return Task.FromResult(Request);
        }

        public Task<IIppResponseMessage> ReadIppResponseAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            Calls.Add("ReadIppResponse");
            return Task.FromResult(Response);
        }

        public Task WriteIppRequestAsync(IIppRequestMessage ippRequestMessage, Stream stream, CancellationToken cancellationToken = default)
        {
            Calls.Add("WriteIppRequest");
            return Task.CompletedTask;
        }

        public Task WriteIppResponseAsync(IIppResponseMessage message, Stream stream, CancellationToken cancellationToken = default)
        {
            Calls.Add("WriteIppResponse");
            return Task.CompletedTask;
        }
    }
}

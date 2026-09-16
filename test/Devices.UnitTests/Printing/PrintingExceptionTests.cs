using AdaptArch.Devices.Printing;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing;

public class PrintingExceptionTests
{
    private static readonly Uri Ipps = new("ipps://printer.local:631/ipp/print");
    private static readonly Uri Ipp = new("ipp://printer.local:631/ipp/print");

    [Fact]
    public void PrinterConnectionException_NamesEachEndpointAndItsCause()
    {
        Dictionary<Uri, Exception> failures = new()
        {
            [Ipps] = new HttpRequestException("The remote certificate is expired."),
            [Ipp] = new HttpRequestException("Connection refused"),
        };

        PrinterConnectionException error = new("printer.local", 631, "IPPS or IPP", failures, failures[Ipps]);

        Assert.Equal(2, error.Failures.Count);
        Assert.Contains("printer.local:631", error.Message, StringComparison.Ordinal);
        Assert.Contains("The remote certificate is expired.", error.Message, StringComparison.Ordinal);
        Assert.Contains("Connection refused", error.Message, StringComparison.Ordinal);

        // The first probe is the cause, not the last one.
        Assert.Same(failures[Ipps], error.InnerException);
    }

    [Fact]
    public void PrinterConnectionException_RejectsAnEmptyFailureSet() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PrinterConnectionException("printer.local", 631, "IPPS", new Dictionary<Uri, Exception>(), new HttpRequestException("none")));

    [Fact]
    public void PrinterConnectionException_StandardConstructors_ReportNoFailures()
    {
        Assert.Empty(new PrinterConnectionException().Failures);
        Assert.Equal("a message", new PrinterConnectionException("a message").Message);

        PrinterConnectionException withCause = new("a message", new InvalidOperationException("the cause"));
        Assert.Equal("the cause", withCause.InnerException.Message);
        Assert.Empty(withCause.Failures);
    }

    [Fact]
    public void PrinterConnectionException_IsCaughtAsAnInvalidOperationException() =>
        Assert.IsType<InvalidOperationException>(new PrinterConnectionException(), exactMatch: false);

    [Fact]
    public void PrinterOperationException_CarriesTheCauseAsData()
    {
        PrinterOperationException error = new("a message", new InvalidOperationException("the cause"))
        {
            PrinterId = PrinterId.ForIpp("printer.local"),
            Endpoint = Ipp,
            Operation = "Print-Job",
            IppStatusCode = 0x040A,
        };

        Assert.Equal("printer.local", error.PrinterId?.Authority);
        Assert.Equal(Ipp, error.Endpoint);
        Assert.Equal("Print-Job", error.Operation);
        Assert.Equal(0x040A, error.IppStatusCode);
        Assert.Empty(error.RawAttributes);
    }

    [Fact]
    public void PrinterOperationException_StandardConstructors_CarryNothing()
    {
        Assert.Null(new PrinterOperationException().Endpoint);
        Assert.Equal("a message", new PrinterOperationException("a message").Message);
        Assert.Null(new PrinterOperationException("a message").IppStatusCode);
    }

    [Fact]
    public void PrinterOperationException_IsCaughtAsAnInvalidOperationException() =>
        Assert.IsType<InvalidOperationException>(new PrinterOperationException(), exactMatch: false);
}

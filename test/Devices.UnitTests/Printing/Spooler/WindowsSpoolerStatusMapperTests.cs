using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Printing.Spooler;
using Xunit;

namespace AdaptArch.Devices.UnitTests.Printing.Spooler;

public class WindowsSpoolerStatusMapperTests
{
    [Theory]
    [InlineData(0x00000000u, PrinterStatusState.Idle)]
    [InlineData(0x00000001u, PrinterStatusState.Paused)]
    [InlineData(0x00000002u, PrinterStatusState.Error)]
    [InlineData(0x00000004u, PrinterStatusState.Error)] // PRINTER_STATUS_PENDING_DELETION
    [InlineData(0x00000008u, PrinterStatusState.Error)] // PRINTER_STATUS_PAPER_JAM
    [InlineData(0x00000010u, PrinterStatusState.Error)] // PRINTER_STATUS_PAPER_OUT
    [InlineData(0x00000040u, PrinterStatusState.Error)] // PRINTER_STATUS_PAPER_PROBLEM
    [InlineData(0x00400000u, PrinterStatusState.Error)] // PRINTER_STATUS_DOOR_OPEN
    [InlineData(0x00000080u, PrinterStatusState.Offline)] // PRINTER_STATUS_OFFLINE
    [InlineData(0x00001000u, PrinterStatusState.Offline)] // PRINTER_STATUS_NOT_AVAILABLE
    [InlineData(0x00000400u, PrinterStatusState.Processing)] // PRINTER_STATUS_PRINTING
    [InlineData(0x00004000u, PrinterStatusState.Processing)] // PRINTER_STATUS_PROCESSING
    [InlineData(0x00000200u, PrinterStatusState.Idle)] // PRINTER_STATUS_BUSY: not one of the mapped bits
    public void MapPrinterStatus_MapsEachDocumentedBit(uint status, PrinterStatusState expected) =>
        Assert.Equal(expected, WindowsSpoolerStatusMapper.MapPrinterStatus(status));

    [Fact]
    public void MapPrinterStatus_ErrorTakesPrecedenceOverPaused()
    {
        // PAUSED (0x1) and ERROR (0x2) both set: error needs the most attention.
        var status = WindowsSpoolerStatusMapper.MapPrinterStatus(0x00000003u);

        Assert.Equal(PrinterStatusState.Error, status);
    }

    [Fact]
    public void MapPrinterStatus_OfflineTakesPrecedenceOverProcessing()
    {
        // OFFLINE (0x80) and PROCESSING (0x4000) both set: offline is the more actionable state.
        var status = WindowsSpoolerStatusMapper.MapPrinterStatus(0x00004080u);

        Assert.Equal(PrinterStatusState.Offline, status);
    }

    [Fact]
    public void MapPrinterStatus_PendingDeletionTakesPrecedenceOverOffline()
    {
        // PENDING_DELETION (0x4) and OFFLINE (0x80) both set: a queue being torn down
        // is not the same "actionable, might come back" state offline alone reports.
        var status = WindowsSpoolerStatusMapper.MapPrinterStatus(0x00000084u);

        Assert.Equal(PrinterStatusState.Error, status);
    }

    [Theory]
    [InlineData(0x00000000u, true)] // no bits: idle, accepts jobs
    [InlineData(0x00000001u, false)] // PRINTER_STATUS_PAUSED
    [InlineData(0x00000002u, false)] // PRINTER_STATUS_ERROR
    [InlineData(0x00000080u, false)] // PRINTER_STATUS_OFFLINE
    [InlineData(0x00001000u, false)] // PRINTER_STATUS_NOT_AVAILABLE
    [InlineData(0x00000400u, true)] // PRINTER_STATUS_PRINTING: busy, but still accepting
    [InlineData(0x00004000u, true)] // PRINTER_STATUS_PROCESSING: busy, but still accepting
    public void IsAcceptingJobs_IsFalseForPausedOfflineOrErrorBits(uint status, bool expected) =>
        Assert.Equal(expected, WindowsSpoolerStatusMapper.IsAcceptingJobs(status));

    [Theory]
    [InlineData(0x00000000u, PrintJobState.Queued)]
    [InlineData(0x00000001u, PrintJobState.Paused)]
    [InlineData(0x00000002u, PrintJobState.Failed)]
    [InlineData(0x00000004u, PrintJobState.Canceled)] // JOB_STATUS_DELETING
    [InlineData(0x00000100u, PrintJobState.Canceled)] // JOB_STATUS_DELETED
    [InlineData(0x00000010u, PrintJobState.Printing)] // JOB_STATUS_PRINTING
    [InlineData(0x00000080u, PrintJobState.Completed)] // JOB_STATUS_PRINTED
    [InlineData(0x00001000u, PrintJobState.Completed)] // JOB_STATUS_COMPLETE
    [InlineData(0x00000008u, PrintJobState.Queued)] // JOB_STATUS_SPOOLING: not one of the mapped bits
    public void MapJobStatus_MapsEachDocumentedBit(uint status, PrintJobState expected) =>
        Assert.Equal(expected, WindowsSpoolerStatusMapper.MapJobStatus(status));

    [Fact]
    public void MapJobStatus_DeletedTakesPrecedenceOverPrinting()
    {
        // DELETED (0x100) and PRINTING (0x10) both set: deletion is the more terminal outcome.
        var status = WindowsSpoolerStatusMapper.MapJobStatus(0x00000110u);

        Assert.Equal(PrintJobState.Canceled, status);
    }

    [Fact]
    public void DescribeJobStatus_ReturnsNullWhenNoBitIsSet() =>
        Assert.Null(WindowsSpoolerStatusMapper.DescribeJobStatus(0x00000000u));

    [Fact]
    public void DescribeJobStatus_NamesEachSetBit()
    {
        // PAUSED (0x1) and PRINTING (0x10) both set.
        var detail = WindowsSpoolerStatusMapper.DescribeJobStatus(0x00000011u);

        Assert.Equal("Paused; Printing", detail);
    }
}

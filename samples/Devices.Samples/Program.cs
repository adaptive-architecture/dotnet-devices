using AdaptArch.Devices.DependencyInjection;
using AdaptArch.Devices.Samples;
using Microsoft.Extensions.DependencyInjection;

Console.WriteLine("AdaptArch.Devices samples");
Console.WriteLine($"Current OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");

ServiceCollection services = new();
services.AddPrinters();
using var provider = services.BuildServiceProvider();

var printFilesDirectory = Path.Combine(AppContext.BaseDirectory, "PrintFiles");
Console.WriteLine("Printable files:");
foreach (var file in SampleHelpers.GetPrintFiles(printFilesDirectory))
{
    Console.WriteLine($"- {file}");
}

if (args.Length == 0)
{
    await PrinterManagerScenario.DiscoverAsync(provider).ConfigureAwait(false);
    PrintHelp();
    return;
}

switch (args[0])
{
    case "print-manager":
        await RunPrinterManagerAsync(args).ConfigureAwait(false);
        return;
    case "manual-management":
        await RunManualManagementAsync(args).ConfigureAwait(false);
        return;
    case "win-printer-test":
        await RunWinPrinterTestAsync(args).ConfigureAwait(false);
        return;
    default:
        PrintHelp();
        return;
}

async Task RunPrinterManagerAsync(string[] commandArgs)
{
    if (commandArgs.Length >= 2 && commandArgs[1] == "discover")
    {
        await PrinterManagerScenario.DiscoverAsync(provider).ConfigureAwait(false);
        return;
    }

    if (commandArgs.Length == 4 && commandArgs[1] == "send")
    {
        if (!SampleHelpers.Confirm($"Send '{commandArgs[3]}' to printer {commandArgs[2]}?"))
        {
            Console.WriteLine("Cancelled; nothing was sent.");
            return;
        }

        await PrinterManagerScenario.SendAsync(provider, printFilesDirectory, commandArgs[2], commandArgs[3]).ConfigureAwait(false);
        return;
    }

    if (commandArgs.Length == 4 && commandArgs[1] == "watch")
    {
        if (!SampleHelpers.Confirm($"Send '{commandArgs[3]}' to printer {commandArgs[2]} and watch the job?"))
        {
            Console.WriteLine("Cancelled; nothing was sent.");
            return;
        }

        await PrinterManagerScenario.WatchAsync(provider, printFilesDirectory, commandArgs[2], commandArgs[3]).ConfigureAwait(false);
        return;
    }

    if (commandArgs.Length == 3 && commandArgs[1] == "zpl")
    {
        if (!SampleHelpers.Confirm($"Send a ZPL test label to printer {commandArgs[2]} over its passthrough channel?"))
        {
            Console.WriteLine("Cancelled; nothing was sent.");
            return;
        }

        await PrinterManagerScenario.SendZplAsync(provider, commandArgs[2]).ConfigureAwait(false);
        return;
    }

    PrintHelp();
}

async Task RunManualManagementAsync(string[] commandArgs)
{
    if (commandArgs.Length >= 2 && commandArgs[1] == "discover")
    {
        await ManualManagementScenario.DiscoverAsync(provider).ConfigureAwait(false);
        return;
    }

    if (commandArgs.Length == 3 && commandArgs[1] == "status")
    {
        await ManualManagementScenario.StatusAsync(provider, commandArgs[2]).ConfigureAwait(false);
        return;
    }

    if (commandArgs.Length == 4 && commandArgs[1] == "send")
    {
        if (!SampleHelpers.Confirm($"Send '{commandArgs[3]}' to printer {commandArgs[2]} on TCP port 9100?"))
        {
            Console.WriteLine("Cancelled; nothing was sent.");
            return;
        }

        await ManualManagementScenario.SendAsync(provider, printFilesDirectory, commandArgs[2], commandArgs[3]).ConfigureAwait(false);
        return;
    }

    PrintHelp();
}

async Task RunWinPrinterTestAsync(string[] commandArgs)
{
    if (commandArgs.Length < 2)
    {
        PrintHelp();
        return;
    }

    var wantsPrint = commandArgs.Length >= 3 && commandArgs[2] == "--print";
    await WindowsPrinterTestScenario.RunAsync(commandArgs[1], wantsPrint).ConfigureAwait(false);
}

static void PrintHelp()
{
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run -- print-manager discover");
    Console.WriteLine("  dotnet run -- print-manager send  <printer-id> <file>");
    Console.WriteLine("  dotnet run -- print-manager watch <printer-id> <file>");
    Console.WriteLine("  dotnet run -- print-manager zpl   <printer-id>");
    Console.WriteLine();
    Console.WriteLine("  dotnet run -- manual-management discover");
    Console.WriteLine("  dotnet run -- manual-management status <printer-id>");
    Console.WriteLine("  dotnet run -- manual-management send   <printer-id> <file>");
    Console.WriteLine();
    Console.WriteLine("  dotnet run -- win-printer-test <queue-name> [--print]");
    Console.WriteLine();
    Console.WriteLine("<file> is a name from PrintFiles, above.");
    Console.WriteLine("<printer-id> is a printer identifier, such as Network:192.168.0.152 or Spooler:EPSON_L6270_Series.");
    Console.WriteLine("A bare value with no 'Kind:' prefix, such as 192.168.0.152, is treated as a network printer.");
    Console.WriteLine("'discover' prints the exact identifier to use for each printer it finds.");
}

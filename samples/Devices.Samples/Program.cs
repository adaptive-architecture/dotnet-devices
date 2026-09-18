using System.Text.Json.Serialization;
using AdaptArch.Devices.DependencyInjection;
using AdaptArch.Devices.Samples;
using AdaptArch.Devices.Samples.Api;
using AdaptArch.Devices.Samples.Contracts;

// The Windows package is referenced on Windows only (see the sample project file), so the
// call needs the same compile-time guard as the reference.
#if WINDOWS10_0_19041_0_OR_GREATER
if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240, 0))
{
    AdaptArch.Devices.Windows.WindowsPrinting.EnableSpoolerPdfPrinting();
}
#endif

// The slim builder leaves out what a printer manager never uses, and it is the shape the
// native AOT publish supports. That publish is why this sample exists: it is the only
// application that consumes src/, so a trim or an AOT problem in the library shows up here.
var builder = WebApplication.CreateSlimBuilder(args);

// This application prints to real hardware on the local network, so it listens on the
// loopback address only. Set ASPNETCORE_URLS to move it, and know what that means.
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5080");

// Native AOT has no reflection to fall back on, so every contract is source-generated.
// The options of the source generator govern what the context reads from disk; these
// govern what the endpoints write, so the two must agree about a null.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});

_ = builder.Services.AddPrinters();
builder.Services.AddSingleton<SamplePaths>();
builder.Services.AddSingleton<PrinterCatalog>();
builder.Services.AddSingleton<PrintJobRunner>();

var app = builder.Build();

app.UseDefaultFiles();

// The page is edited while the sample runs, and a browser that keeps a heuristic copy of
// app.js shows a button that does nothing. "no-cache" still revalidates, so an unchanged
// file costs a 304.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
});

PrintersApi.Map(app);
JobsApi.Map(app);
JobSetsApi.Map(app);
DiagnosticsApi.Map(app);

Console.WriteLine($"Current OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
Console.WriteLine("Open the printer manager in a browser. Press Ctrl+C to stop it.");

app.Run();

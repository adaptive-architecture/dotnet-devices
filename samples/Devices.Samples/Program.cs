using System.Text.Json.Serialization;
using AdaptArch.Devices.DependencyInjection;
using AdaptArch.Devices.Samples;
using AdaptArch.Devices.Samples.Api;
using AdaptArch.Devices.Samples.Contracts;

// The Windows package is referenced on Windows only (see the sample project file), so the
// call needs the same compile-time guard as the reference. It is enabled first because the
// first converter registered for a content type is the one that runs, and the in-box engine
// is the one to prefer where it exists: it needs no native library beside the executable.
#if WINDOWS10_0_19041_0_OR_GREATER
if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240, 0))
{
    AdaptArch.Devices.Windows.WindowsPrinting.EnablePdfPrinting();
}
#endif

// The PDFium package is referenced on every platform, and enabled on every platform even
// where the line above already answered for PDF. That is deliberate: pipeline/publish-samples.sh
// is the only trim and native AOT gate in this repository, and a reference nothing calls is
// a reference the trimmer removes whole, which would leave the publish green having proven
// nothing about it.
AdaptArch.Devices.Pdfium.PdfiumPrinting.EnablePdfPrinting();

// The slim builder leaves out what a printer manager never uses, and it is the shape the
// native AOT publish supports. That publish is why this sample exists: it is the only
// application that consumes src/, so a trim or an AOT problem in the library shows up here.
// A published exe is started from any working directory, while dotnet run starts in the
// project folder. The folder beside the exe holds wwwroot after publish, so prefer it when
// it does and fall back to the working directory for development. A build leaves an empty
// wwwroot beside the exe, so the page itself is what the probe looks for.
var contentRoot = File.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"))
    ? AppContext.BaseDirectory
    : Directory.GetCurrentDirectory();
var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args, ContentRootPath = contentRoot });

// This application prints to real hardware on the local network, so it listens on the
// loopback address only. Set ASPNETCORE_URLS to move it, and know what that means.
var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5080";
builder.WebHost.UseUrls(urls);

// ASP.NET Core narrates every request and every static file it serves, which buries the
// lines the printing stack writes. Its warnings still arrive, and the address it would
// have reported is printed below instead.
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);

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
EnginesApi.Map(app);
DiagnosticsApi.Map(app);

Console.WriteLine($"Current OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
Console.WriteLine($"Open the printer manager at {urls}. Press Ctrl+C to stop it.");

app.Run();

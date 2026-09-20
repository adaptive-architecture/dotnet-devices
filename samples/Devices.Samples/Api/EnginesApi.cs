using AdaptArch.Devices.Printing;
using AdaptArch.Devices.Samples.Contracts;

namespace AdaptArch.Devices.Samples.Api;

// Which engines can render a PDF in this process. There is no fixed list to hard-code: it
// depends on which packages the build referenced and which of them Program.cs enabled, and
// on Windows that is two where everywhere else it is one.
internal static class EnginesApi
{
    public static void Map(IEndpointRouteBuilder app) =>
        _ = app.MapGet("/api/pdf-engines", () =>
        {
            // Ordering carries the preference, so the head of the list is what a job that
            // names nothing gets. The page shows that as the default and names the rest.
            var converters = PrintFormatPolicy.Default.ConvertersFor(PrinterContentTypes.Pdf);

            List<EngineDto> engines = new(converters.Count);
            for (var index = 0; index < converters.Count; index++)
            {
                engines.Add(new EngineDto(converters[index].Name, index == 0));
            }

            return (IReadOnlyList<EngineDto>)engines;
        });
}

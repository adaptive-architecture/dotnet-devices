#nullable enable
namespace AdaptArch.Devices.Pdfium.UnitTests;

// The renderer yields its pages one at a time; a test reads them all.
internal static class AsyncEnumerableExtensions
{
    public static async Task<IReadOnlyList<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        List<T> items = [];
        await foreach (var item in source)
        {
            items.Add(item);
        }

        return items;
    }
}

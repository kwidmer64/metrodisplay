using CsvHelper.Configuration;
using MetroDisplay.Contracts;

namespace MetroDisplay.Server.Feeds;

/// <summary>
/// Downloads a city's static GTFS zip once and keeps it on disk, so a restart does not fetch
/// 25 MB again. There is no refresh: delete the cached file to pick up a newer feed.
/// Conditional GET and a daily re-check arrive in slice 14.
/// </summary>
/// <param name="httpClient">Client used for the download.</param>
/// <param name="gtfsStoreDirectory">Where zips are kept, one per city as <c>&lt;cityId&gt;.zip</c>. Created on first download.</param>
public sealed class StaticFeedCache(HttpClient httpClient, string gtfsStoreDirectory)
{
    /// <summary>
    /// Where a city's feed is kept, so an error about a bad cached feed can name the file to delete.
    /// </summary>
    public string CachePathFor(string cityId) => Path.Combine(gtfsStoreDirectory, $"{cityId}.zip");

    /// <exception cref="HttpRequestException">The download failed. The message names the city and URL, and nothing is cached.</exception>
    public async Task<byte[]> GetAsync(string cityId, FeedSource source, CancellationToken cancellationToken = default)
    {
        // Generate the file path and check if it already exists
        string filePath = CachePathFor(cityId);
        if (File.Exists(filePath))
        {
            return await File.ReadAllBytesAsync(filePath, cancellationToken);
        }

        // create a new request message, setting the method and the URL
        using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, source.Url))
        {
            // Make sure there are Headers in the FeedSource object,
            // then loop through each header and add it to the request headers
            // TryAddWithoutValidation skips validating the value of header
            if (source.Headers is not null)
            {
                foreach ((string name, string value) in source.Headers)
                {
                    request.Headers.TryAddWithoutValidation(name, value);
                }
            }

            // Send the HTTP request and await the response
            using (HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken))
            {
                // Check the request was successful
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Static feed for '{cityId}' returned HTTP {(int)response.StatusCode} from {source.Url}.",
                        inner: null,
                        response.StatusCode);
                }

                // Read the response content (should be a ZIP file at this point) into a byte array
                byte[] zipBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

                // Create the directory to store the file. CreateDirectory doesnt run if the directory exists
                Directory.CreateDirectory(gtfsStoreDirectory);

                // Store the file and return the bytes
                await File.WriteAllBytesAsync(filePath, zipBytes, cancellationToken);
                return zipBytes;
            }
        }
    }
}

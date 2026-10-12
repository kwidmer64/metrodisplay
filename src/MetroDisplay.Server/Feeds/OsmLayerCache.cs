using System.Security.Cryptography;
using System.Text;
using MetroDisplay.Osm;

namespace MetroDisplay.Server.Feeds;

/// <summary>
/// Fetches an OpenStreetMap layer from Overpass once per city and keeps it for good.
/// Coastlines and lakes change on a scale of years, so unlike the GTFS feed a layer is never
/// refreshed: delete its file to fetch it again.
/// </summary>
/// <remarks>
/// A layer is kept per query, not only per city. A city whose core or radius changes asks for
/// a different box, and the answer kept for the old box does not cover the new one: coastline
/// would stop short of the map's edge. Such a city fetches again and leaves the old file behind.
/// </remarks>
/// <param name="httpClient">Client used for the query. Overpass asks callers to identify themselves with a User-Agent.</param>
/// <param name="cacheDirectory">Where layers are kept, one file per city, layer and query. Created on first download.</param>
/// <param name="overpassUrl">The Overpass API interpreter endpoint.</param>
public sealed class OsmLayerCache(HttpClient httpClient, string cacheDirectory, string overpassUrl)
{
    /// <summary>
    /// Where a layer is kept, so a warning about it can name the file to delete.
    /// Returns the full path of the layer
    /// </summary>
    public string CachePathFor(string cityId, string layer, string query)
    {
        // Eight hex digits of the query's hash tell one box from another without a long name.
        string queryHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(query)))[..8];
        return Path.Combine(cacheDirectory, $"{cityId}-{layer}-{queryHash}.json");
    }

    /// <returns>The raw Overpass response. Raw, so later changes to how a layer is processed need no refetch.</returns>
    /// <exception cref="HttpRequestException">Overpass could not be reached or answered with an error status. Nothing is cached.</exception>
    /// <exception cref="InvalidDataException">Overpass answered with something unusable, such as a timed-out query. Nothing is cached.</exception>
    public async Task<string> GetAsync(string cityId, string layer, string query, CancellationToken cancellationToken = default)
    {
        // Gets the path of the layer, checks if it exists, and returns a Task<> that reads the file
        string cachePath = CachePathFor(cityId, layer, query);
        if (File.Exists(cachePath))
        {
            return await File.ReadAllTextAsync(cachePath, cancellationToken);
        }

        // Create the Overpass QL (specific to OSM) query to send in the OSM POST request under the 'data' parameter
        Dictionary<string, string> data = new()
        { 
            ["data"] = query
        };

        // Encode the data into a form-urlencoded payload
        using (FormUrlEncodedContent form = new(data))
        {
            // Send the POST request, wait for response 
            using (HttpResponseMessage response = await httpClient.PostAsync(overpassUrl, form, cancellationToken))
            {
                // If the request returns anything other than a success code
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Overpass returned HTTP {(int)response.StatusCode} for the '{layer}' layer of '{cityId}'.",
                        inner: null,
                        response.StatusCode);
                }

                // Read the body of the response
                string body = await response.Content.ReadAsStringAsync(cancellationToken);

                // Check that the response is valid before storing
                OverpassReader.Read(body);

                // Write outside the target directory, then move the file in to place
                // This way an interrupted write never leaves a file with partial data
                Directory.CreateDirectory(cacheDirectory);
                string downloadPath = cachePath + ".download";
                await File.WriteAllTextAsync(downloadPath, body, cancellationToken);
                File.Move(downloadPath, cachePath, overwrite: true);
                return body;
            }
        }
        
    }
}

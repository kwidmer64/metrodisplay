namespace MetroDisplay.Server;

/// <summary>
/// The Server's own settings, bound from the <c>MetroDisplay</c> configuration section.
/// </summary>
/// <param name="CityId">The city to show. Until rotation arrives (slice 13), the Server shows one.</param>
/// <param name="CitiesDirectory">Directory holding the city configs. A relative path resolves against the content root.</param>
/// <param name="FeedCacheDirectory">Where downloaded static feeds are kept between runs. A relative path resolves against the content root.</param>
public sealed record ServerSettings(string CityId, string CitiesDirectory, string FeedCacheDirectory);

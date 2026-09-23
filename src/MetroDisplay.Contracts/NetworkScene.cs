namespace MetroDisplay.Contracts;

/// <summary>
/// The `network` message: everything a renderer needs to draw one city's map.
/// </summary>
/// <param name="ArtifactVersion">Version id of the artifact this scene was built from, e.x. <c>mbta@2026-09-01.a3f1</c>. Shape ids in frame messages are only valid against this version.</param>
/// <param name="City">Name of the city.</param>
/// <param name="Extent">The geographic window that every normalized coordinate in the scene is relative to.</param>
/// <param name="Lines">Rail lines to draw.</param>
/// <param name="Stations">Parent stations inside the extent.</param>
/// <param name="EdgeLabels">Terminus labels placed where clipped lines leave the extent.</param>
public sealed record NetworkScene(string ArtifactVersion, CityMetadata City, ExtentInfo Extent, IReadOnlyList<LineScene> Lines, IReadOnlyList<StationMarker> Stations, IReadOnlyList<EdgeLabel> EdgeLabels);

/// <summary>
/// Display identity of a city.
/// </summary>
/// <param name="Id">Registry id, matching the city's config file name.</param>
/// <param name="Name">Name of the city</param>
/// <param name="Agency">Operating agency.</param>
/// <param name="Timezone">IANA timezone id for the city's local clock, such as <c>America/New_York</c>.</param>
public sealed record CityMetadata(string Id, string Name, string Agency, string Timezone);

/// <summary>
/// Geographic window the scene is drawn within.
/// </summary>
/// <param name="Aspect">Aspect ratio of the extent.</param>
/// <param name="CoreRadiusKm">Configured radius around the city core, as ground distance.</param>
/// <param name="SpanKm">Ground distance across the extent's longest axis.</param>
public sealed record ExtentInfo(double Aspect, double CoreRadiusKm, double SpanKm);

/// <summary>
/// A rail line and the shapes that draw it.
/// </summary>
/// <param name="Id">GTFS route id.</param>
/// <param name="Name">Name as shown on screen.</param>
/// <param name="Color">Colour of the line as <c>#RRGGBB</c>, leading <c>#</c> included. Grey when the agency publishes no <c>route_color</c>.</param>
/// <param name="Shapes">Polylines for this line after deduplication and clipping.</param>
public sealed record LineScene(string Id, string Name, string Color, IReadOnlyList<ShapeGeometry> Shapes);

/// <summary>
/// A clipped, simplified polyline belonging to a line.
/// </summary>
/// <param name="Id">Canonical shape id. When clipping splits a shape into several runs, runs after the first carry a <c>#1</c>, <c>#2</c> suffix.</param>
/// <param name="Points">A flat [x0,y0,x1,y1,...] array in the normalized extent space.</param>
/// <param name="LengthM">Ground length of this polyline in metres.</param>
public sealed record ShapeGeometry(string Id, IReadOnlyList<double> Points, double LengthM);

/// <summary>
/// A station marker.
/// </summary>
/// <param name="X">Horizontal position within the extent.</param>
/// <param name="Y">Vertical position within the extent. 0 is the top.</param>
/// <param name="Name">Name as shown on screen.</param>
/// <param name="Rank">Prominence hint for drawing. Currently always 1, the rule is decided once stations are visible.</param>
public sealed record StationMarker(double X, double Y, string Name, int Rank);

/// <summary>
/// Drawn where a clipped line leaves the extent.
/// </summary>
/// <param name="X">Normalized horizontal position of the exit point.</param>
/// <param name="Y">Normalized vertical position of the exit point, screen convention: 0 is the top.</param>
/// <param name="Text">Label text, such as <c>TO ALEWIFE</c>.</param>
/// <param name="Angle">Direction of travel at the exit, in degrees, screen convention.</param>
/// <param name="Line">Id of the <see cref="LineScene"/> this label belongs to.</param>
public sealed record EdgeLabel(double X, double Y, string Text, double Angle, string Line);

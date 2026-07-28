#nullable enable
using System.Globalization;
using System.Xml.Linq;

namespace BazaarPlusPlus.Game.CollectionPanel.Ui;

// Deliberately narrow reader for the two Collection search-mode SVGs. Those resources use only
// circles and lines, so this keeps their authored geometry as the runtime source without adding
// a general SVG package or rendering subsystem.
internal sealed class CollectionSearchSvgIconData
{
    private CollectionSearchSvgIconData(
        float minX,
        float minY,
        float width,
        float height,
        float strokeWidth,
        IReadOnlyList<CollectionSearchSvgCircle> circles,
        IReadOnlyList<CollectionSearchSvgSegment> segments
    )
    {
        MinX = minX;
        MinY = minY;
        Width = width;
        Height = height;
        StrokeWidth = strokeWidth;
        Circles = circles;
        Segments = segments;
    }

    public float MinX { get; }

    public float MinY { get; }

    public float Width { get; }

    public float Height { get; }

    public float StrokeWidth { get; }

    public IReadOnlyList<CollectionSearchSvgCircle> Circles { get; }

    public IReadOnlyList<CollectionSearchSvgSegment> Segments { get; }

    public static CollectionSearchSvgIconData LoadEmbedded(string resourceName)
    {
        var assembly = typeof(CollectionSearchSvgIconData).Assembly;
        using var stream =
            assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded Collection SVG resource '{resourceName}' was not found."
            );
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    internal static CollectionSearchSvgIconData Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("SVG source must not be empty.", nameof(source));

        var root =
            XDocument.Parse(source, LoadOptions.None).Root
            ?? throw new InvalidOperationException("Collection SVG resource has no root element.");
        if (
            root.Name.LocalName != "svg"
            || (string?)root.Attribute("stroke-linecap") != "round"
            || (string?)root.Attribute("stroke-linejoin") != "round"
        )
        {
            throw new InvalidOperationException(
                "Collection icon must be an SVG with round stroke caps and joins."
            );
        }

        var viewBox = ParseList((string?)root.Attribute("viewBox"), 4, "viewBox");
        var strokeWidth = Parse((string?)root.Attribute("stroke-width"), "stroke-width");
        if (viewBox[2] <= 0f || viewBox[3] <= 0f || strokeWidth <= 0f)
            throw new InvalidOperationException(
                "Collection SVG viewBox and stroke width must be positive."
            );

        var circles = new List<CollectionSearchSvgCircle>();
        var segments = new List<CollectionSearchSvgSegment>();
        foreach (var element in root.Elements())
        {
            if (element.Name.LocalName == "circle")
            {
                circles.Add(
                    new CollectionSearchSvgCircle(
                        Parse((string?)element.Attribute("cx"), "circle cx"),
                        Parse((string?)element.Attribute("cy"), "circle cy"),
                        Parse((string?)element.Attribute("r"), "circle r")
                    )
                );
            }
            else if (element.Name.LocalName == "line")
            {
                segments.Add(
                    new CollectionSearchSvgSegment(
                        Parse((string?)element.Attribute("x1"), "line x1"),
                        Parse((string?)element.Attribute("y1"), "line y1"),
                        Parse((string?)element.Attribute("x2"), "line x2"),
                        Parse((string?)element.Attribute("y2"), "line y2")
                    )
                );
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported Collection SVG element '{element.Name.LocalName}'."
                );
            }
        }

        if (circles.Count == 0 && segments.Count == 0)
            throw new InvalidOperationException("Collection SVG resource contains no strokes.");

        return new CollectionSearchSvgIconData(
            viewBox[0],
            viewBox[1],
            viewBox[2],
            viewBox[3],
            strokeWidth,
            circles,
            segments
        );
    }

    private static float[] ParseList(string? source, int count, string name)
    {
        var parts = (source ?? string.Empty).Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries
        );
        if (parts.Length != count)
            throw new InvalidOperationException(
                $"Collection SVG {name} must contain {count} numbers."
            );
        return parts.Select(part => Parse(part, name)).ToArray();
    }

    private static float Parse(string? source, string name)
    {
        if (
            !float.TryParse(source, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        )
            throw new InvalidOperationException($"Collection SVG {name} is not a valid number.");
        return value;
    }
}

internal readonly record struct CollectionSearchSvgCircle(
    float CenterX,
    float CenterY,
    float Radius
);

internal readonly record struct CollectionSearchSvgSegment(
    float StartX,
    float StartY,
    float EndX,
    float EndY
);

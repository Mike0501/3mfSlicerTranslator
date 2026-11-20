using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using static Translator.ProjectArchive;

namespace Translator;

internal enum PlacementChoice { Keep, Adjust, Cancel }

internal sealed record PlacementReview(IReadOnlyList<int> Adjustable, IReadOnlyList<int> Manual, bool Unverified);

internal static class PlatePlacement
{
    private const double Tolerance = 0.01;
    // The supported project slicers lay out plates in a square grid with a 20% gap.
    private const double PlateStride = 1.2;
    private static readonly XNamespace Production = "http://schemas.microsoft.com/3dmanufacturing/production/2015/06";

    internal static void Review(ProjectArchive archive, XElement model, XElement config, JsonObject original,
        JsonObject target, List<string> warnings, Func<PlacementReview, PlacementChoice>? choose)
    {
        if (JsonNode.DeepEquals(original["printable_area"], target["printable_area"]) &&
            JsonNode.DeepEquals(original["bed_exclude_area"], target["bed_exclude_area"]))
            return;

        var changes = new List<(int Plate, XElement Item, double X, double Y)>();
        var manual = new List<int>();
        bool unverified = choose is null;
        if (choose is not null)
        {
            try
            {
                Plan(archive, model, config, original, target, changes, manual);
            }
            catch (Exception error) when (error is InvalidDataException or XmlException)
            {
                changes.Clear();
                manual.Clear();
                unverified = true;
            }
        }

        int[] adjustable = changes.Select(change => change.Plate).Distinct().ToArray();
        if (adjustable.Length == 0 && manual.Count == 0 && !unverified)
            return;

        PlacementChoice choice = choose?.Invoke(new PlacementReview(adjustable, manual, unverified)) ?? PlacementChoice.Keep;
        if (choice == PlacementChoice.Cancel)
            throw new OperationCanceledException();
        if (choice == PlacementChoice.Adjust && adjustable.Length > 0)
        {
            foreach (var change in changes)
            {
                string[] matrix = Split((string?)change.Item.Attribute("transform")) ??
                    ["1", "0", "0", "0", "1", "0", "0", "0", "1", "0", "0", "0"];
                matrix[9] = (Number(matrix[9]) + change.X).ToString("G17", CultureInfo.InvariantCulture);
                matrix[10] = (Number(matrix[10]) + change.Y).ToString("G17", CultureInfo.InvariantCulture);
                change.Item.SetAttributeValue("transform", string.Join(" ", matrix));
            }
            warnings.Add(T($"Object positions were adjusted for destination plate(s) {string.Join(", ", adjustable)}.",
                $"Posizioni degli oggetti adattate sui piatti di destinazione {string.Join(", ", adjustable)}."));
        }
        else if (adjustable.Length > 0)
            warnings.Add(T($"Original positions kept on plate(s) {string.Join(", ", adjustable)}. Check placement in the destination slicer.",
                $"Posizioni originali mantenute sui piatti {string.Join(", ", adjustable)}. Controlla il posizionamento nello slicer di destinazione."));
        if (manual.Count > 0)
            warnings.Add(T($"Plate(s) {string.Join(", ", manual)} could not be fitted by moving the objects together. Arrange them in the destination slicer.",
                $"I piatti {string.Join(", ", manual)} non possono essere adattati spostando gli oggetti insieme. Sistemali nello slicer di destinazione."));
        if (unverified)
            warnings.Add(T("The destination bed layout differs. Check object positions in the destination slicer.",
                "La disposizione del piatto di destinazione è diversa. Controlla le posizioni nello slicer di destinazione."));
    }

    private static void Plan(ProjectArchive archive, XElement model, XElement config, JsonObject original, JsonObject target,
        List<(int Plate, XElement Item, double X, double Y)> changes, List<int> manual)
    {
        if ((string?)model.Attribute("unit") is not (null or "millimeter"))
            throw UnsupportedPlacement();
        if (ReadRectangles(original["printable_area"]) is not [Rectangle sourceBed] ||
            ReadRectangles(target["printable_area"]) is not [Rectangle targetBed])
            throw UnsupportedPlacement();
        var excluded = ReadRectangles(target["bed_exclude_area"]);
        XElement[] plates = config.Elements("plate").ToArray();
        var instances = ReadInstances(model);
        var assigned = new HashSet<XElement>();
        int plateCount = Math.Max(1, plates.Length);
        int columns = (int)Math.Ceiling(Math.Sqrt(plateCount));

        for (int index = 0; index < plateCount; index++)
        {
            XElement[] items = plates.Length == 0 || (plateCount == 1 && !plates[0].Elements("model_instance").Any())
                ? instances.Values.ToArray()
                : plates[index].Elements("model_instance").Select(instance => instances.GetValueOrDefault(
                    (Metadata(instance, "object_id"), Metadata(instance, "instance_id"))) ?? throw UnsupportedPlacement()).ToArray();
            if (items.Any(item => !assigned.Add(item)))
                throw UnsupportedPlacement();
            if (items.Length == 0)
                continue;

            double dx = (index % columns) * (targetBed.Width - sourceBed.Width) * PlateStride;
            double dy = -(index / columns) * (targetBed.Depth - sourceBed.Depth) * PlateStride;
            double sourceX = index % columns * sourceBed.Width * PlateStride;
            double sourceY = -(index / columns) * sourceBed.Depth * PlateStride;
            Rectangle[] footprints = items.Where(item => (string?)item.Attribute("printable") != "0")
                .Select(item => GetFootprint(archive, model, item).Move(-sourceX, -sourceY)).ToArray();
            if (footprints.Length == 0)
                continue;

            if (FindShift(footprints, targetBed, excluded) is not { } shift)
            {
                manual.Add(index + 1);
                continue;
            }

            dx += shift.X;
            dy += shift.Y;
            if (Math.Abs(dx) > Tolerance || Math.Abs(dy) > Tolerance)
                changes.AddRange(items.Select(item => (index + 1, item, dx, dy)));
        }

        if (instances.Values.Any(item => !assigned.Contains(item) && (string?)item.Attribute("printable") != "0"))
            throw UnsupportedPlacement();
    }

    private static Dictionary<(string, string), XElement> ReadInstances(XElement model)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var instances = new Dictionary<(string, string), XElement>();
        foreach (XElement item in model.Element(Core + "build")?.Elements(Core + "item") ?? [])
        {
            string id = (string?)item.Attribute("objectid") ?? throw UnsupportedPlacement();
            int instance = counts.GetValueOrDefault(id);
            counts[id] = instance + 1;
            instances.Add((id, instance.ToString(CultureInfo.InvariantCulture)), item);
        }
        return instances;
    }

    private static string Metadata(XElement element, string key) => element.Elements("metadata")
        .FirstOrDefault(item => (string?)item.Attribute("key") == key)?.Attribute("value")?.Value ?? throw UnsupportedPlacement();

    private static Rectangle GetFootprint(ProjectArchive archive, XElement model, XElement item)
    {
        var bounds = new Rectangle(double.PositiveInfinity, double.PositiveInfinity,
            double.NegativeInfinity, double.NegativeInfinity);
        AddComponent(Model, (string?)item.Attribute(Production + "path"), (string?)item.Attribute("objectid"),
            Matrix((string?)item.Attribute("transform")), 0);
        return bounds.MinX <= bounds.MaxX ? bounds : throw UnsupportedPlacement();

        void AddComponent(string part, string? path, string? id, Matrix4x4 transform, int depth)
        {
            if (id is null || depth > 24)
                throw UnsupportedPlacement();
            if (path is not null)
            {
                part = path.StartsWith('/') ? path[1..] : part[..(part.LastIndexOf('/') + 1)] + path;
                if (!IsPartName(part) || !part.StartsWith("3D/", StringComparison.OrdinalIgnoreCase) ||
                    !part.EndsWith(".model", StringComparison.OrdinalIgnoreCase))
                    throw UnsupportedPlacement();
            }

            if (part.Equals(Model, StringComparison.OrdinalIgnoreCase))
            {
                var obj = model.Element(Core + "resources")?.Elements(Core + "object")
                    .FirstOrDefault(element => (string?)element.Attribute("id") == id) ?? throw UnsupportedPlacement();
                foreach (XElement vertex in obj.Element(Core + "mesh")?.Element(Core + "vertices")?.Elements(Core + "vertex") ?? [])
                    AddVertex((string?)vertex.Attribute("x"), (string?)vertex.Attribute("y"), (string?)vertex.Attribute("z"), transform);
                foreach (XElement child in obj.Element(Core + "components")?.Elements(Core + "component") ?? [])
                    AddComponent(part, (string?)child.Attribute(Production + "path"), (string?)child.Attribute("objectid"),
                        Matrix((string?)child.Attribute("transform")) * transform, depth + 1);
                return;
            }

            var children = new List<(string? Path, string? Id, Matrix4x4 Transform)>();
            using (XmlReader reader = archive.OpenXml(part))
            {
                bool found = false;
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "object" ||
                        reader.NamespaceURI != Core.NamespaceName || reader.GetAttribute("id") != id)
                        continue;
                    found = true;
                    using XmlReader geometry = reader.ReadSubtree();
                    while (geometry.Read())
                    {
                        if (geometry.NodeType != XmlNodeType.Element || geometry.NamespaceURI != Core.NamespaceName)
                            continue;
                        if (geometry.LocalName == "vertex")
                            AddVertex(geometry.GetAttribute("x"), geometry.GetAttribute("y"), geometry.GetAttribute("z"), transform);
                        else if (geometry.LocalName == "component")
                            children.Add((geometry.GetAttribute("path", Production.NamespaceName), geometry.GetAttribute("objectid"),
                                Matrix(geometry.GetAttribute("transform")) * transform));
                    }
                    break;
                }
                if (!found)
                    throw UnsupportedPlacement();
            }
            foreach (var child in children)
                AddComponent(part, child.Path, child.Id, child.Transform, depth + 1);
        }

        void AddVertex(string? x, string? y, string? z, Matrix4x4 transform)
        {
            var position = Vector3.Transform(new Vector3((float)Number(x), (float)Number(y), (float)Number(z)), transform);
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
                throw UnsupportedPlacement();
            bounds = new Rectangle(Math.Min(bounds.MinX, position.X), Math.Min(bounds.MinY, position.Y),
                Math.Max(bounds.MaxX, position.X), Math.Max(bounds.MaxY, position.Y));
        }
    }

    private static (double X, double Y)? FindShift(Rectangle[] footprints, Rectangle bed, Rectangle[] excluded)
    {
        double minX = bed.MinX - footprints.Min(item => item.MinX);
        double maxX = bed.MaxX - footprints.Max(item => item.MaxX);
        double minY = bed.MinY - footprints.Min(item => item.MinY);
        double maxY = bed.MaxY - footprints.Max(item => item.MaxY);
        if (minX > maxX || minY > maxY)
            return null;

        double x = Math.Clamp(0, minX, maxX);
        double y = Math.Clamp(0, minY, maxY);
        if (Fits(x, y))
            return (x, y);

        // Candidate offsets where a footprint meets an exclusion edge, with bounded search cost.
        if ((long)footprints.Length * excluded.Length > 64)
            return null;
        var xOptions = new HashSet<double> { x };
        var yOptions = new HashSet<double> { y };
        foreach (Rectangle zone in excluded)
        foreach (Rectangle item in footprints)
        {
            xOptions.Add(Math.Clamp(zone.MinX - item.MaxX - Tolerance, minX, maxX));
            xOptions.Add(Math.Clamp(zone.MaxX - item.MinX + Tolerance, minX, maxX));
            yOptions.Add(Math.Clamp(zone.MinY - item.MaxY - Tolerance, minY, maxY));
            yOptions.Add(Math.Clamp(zone.MaxY - item.MinY + Tolerance, minY, maxY));
        }
        (double X, double Y)? nearest = null;
        double nearestDistance = double.PositiveInfinity;
        foreach (double candidateX in xOptions)
        foreach (double candidateY in yOptions)
        {
            double distance = candidateX * candidateX + candidateY * candidateY;
            if (distance >= nearestDistance || !Fits(candidateX, candidateY))
                continue;
            nearest = (candidateX, candidateY);
            nearestDistance = distance;
        }
        return nearest;

        bool Fits(double dx, double dy) =>
            footprints.All(item => excluded.All(zone => !item.Move(dx, dy).Overlaps(zone)));
    }

    private static Rectangle[] ReadRectangles(JsonNode? node)
    {
        if (node is null)
            return [];
        if (node is not JsonArray points || points.Count % 4 != 0)
            throw UnsupportedPlacement();
        return points.Chunk(4).Select(chunk =>
        {
            var vertices = chunk.Select(point =>
            {
                string[] fields = ProcessSettings.StringValue(point)?.Split('x') ?? [];
                if (fields.Length != 2)
                    throw UnsupportedPlacement();
                return (X: Number(fields[0]), Y: Number(fields[1]));
            }).Distinct().ToArray();
            var bounds = new Rectangle(vertices.Min(p => p.X), vertices.Min(p => p.Y),
                vertices.Max(p => p.X), vertices.Max(p => p.Y));
            if (vertices.Length != 4 || bounds.Width <= 0 || bounds.Depth <= 0 ||
                vertices.Any(p => (p.X != bounds.MinX && p.X != bounds.MaxX) || (p.Y != bounds.MinY && p.Y != bounds.MaxY)))
                throw UnsupportedPlacement();
            return bounds;
        }).ToArray();
    }

    private static string[]? Split(string? value) =>
        value?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static Matrix4x4 Matrix(string? value)
    {
        if (Split(value) is not string[] fields)
            return Matrix4x4.Identity;
        if (fields.Length != 12)
            throw UnsupportedPlacement();
        float[] m = fields.Select(field => (float)Number(field)).ToArray();
        return new Matrix4x4(m[0], m[1], m[2], 0, m[3], m[4], m[5], 0, m[6], m[7], m[8], 0, m[9], m[10], m[11], 1);
    }

    private static double Number(string? value) =>
        value is not null && ProcessSettings.TryNumber(value, out double number) ? number : throw UnsupportedPlacement();

    private static InvalidDataException UnsupportedPlacement() => new("Unsupported plate layout or geometry.");

    private readonly record struct Rectangle(double MinX, double MinY, double MaxX, double MaxY)
    {
        internal double Width => MaxX - MinX;
        internal double Depth => MaxY - MinY;
        internal Rectangle Move(double x, double y) => new(MinX + x, MinY + y, MaxX + x, MaxY + y);
        internal bool Overlaps(Rectangle other) =>
            MinX < other.MaxX && MaxX > other.MinX && MinY < other.MaxY && MaxY > other.MinY;
    }
}

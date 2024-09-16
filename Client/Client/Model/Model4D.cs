using Silk.NET.Maths;
using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Client.Model;

public class Model4D
{
    public readonly List<float> Vertices;
    public readonly List<uint> CellIndices;
    //public readonly List<uint> FaceIndices;
    public readonly List<uint> EdgeIndices;

    private static readonly Dictionary<string, Primitive> s_Primitives;
    private static readonly Dictionary<int, List<Vector4D<float>>> s_Colors;

    private Model4D(List<float> vertices, List<uint> cellIndices, List<uint> edgeIndices)
    {
        Vertices = vertices;
        CellIndices = cellIndices;
        EdgeIndices = edgeIndices;
    }

    /* ===== Model loading ===== */

    private readonly struct Primitive(IReadOnlyList<int> cellIndices, IReadOnlyList<Edge> edges, int vertexCount)
    {
        public readonly IReadOnlyList<int> CellIndices = cellIndices;
        public readonly IReadOnlyList<Edge> Edges = edges;
        public readonly int VertexCount = vertexCount;
    }

    // Effectively a glorified Vector2UI.
    // Edges are equal even if they have their index1/2 swapped (for removing duplicates).
    // They're also enumerable for flattening edge lists into uint lists.
    private struct Edge(uint index1, uint index2) : IEnumerable<uint>
    {
        public uint Index1 = index1;
        public uint Index2 = index2;

        public override readonly bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is Edge edge)
            {
                return
                    (Index1 == edge.Index1 && Index2 == edge.Index2) ||
                    (Index1 == edge.Index2 && Index2 == edge.Index1)
                ;
            }
            return false;
        }

        public override readonly int GetHashCode() => (int)(Index1 ^ Index2);

        public readonly IEnumerator<uint> GetEnumerator() => new EdgeEnumerator(this);
        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // The simple, two-element, edge enumerator.
    private struct EdgeEnumerator(Edge edge) : IEnumerator<uint>
    {
        private Edge _edge = edge;
        private int _index = 0;

        public readonly uint Current => _index < 2 ? _edge.Index1 : _edge.Index2;
        readonly object IEnumerator.Current => Current;

        public readonly void Dispose() {}

        public bool MoveNext()
        {
            if (_index < 2)
            {
                ++_index;
                return true;
            }
            return false;
        }

        public void Reset() => _index = 0;
    }

    // Loads a 4D model.
    // If loading succeeds, returns true and sets the output model.
    // Otherwise returns false and does not set the output model.
    public static bool Load(string filepath, out Model4D? outModel)
    {
        Console.Error.WriteLine($"Loading model at \"{filepath}\".");

        // Default the model to null so that it doesn't
        // have to be set everywhere loading fails.
        outModel = null;

        // Read all lines if possible.
        string[] lines;
        try
        {
            lines = File.ReadAllLines(filepath);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            return false;
        }

        List<Vector4D<float>> positions = [];
        List<uint> cellIndices = [];
        List<Edge> edges = [];
        string primitiveType = "Tetrahedron";

        // Process each line.
        for (int i = 0; i < lines.Length; )
        {
            // Remove comments and leading and trailing whitespace.
            // NOTE: The index increment is here to make logging line numbers easier.
            string line = lines[i++];
            int commentIndex = line.IndexOf('#');
            if (commentIndex != -1)
                line = line[..commentIndex];
            // If the line is only comments and whitespace, ignore it.
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Split the line into its tokens.
            string[] tokens = line.Split(Array.Empty<char>(), StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            // Cache the first token.
            string command = tokens[0];

            // Decide what to do based on command.
            switch (command)
            {
                // Primitive Type
                case "pt":
                    if (tokens.Length != 2)
                    {
                        Console.Error.WriteLine($"Incorrect number of tokens for command \"{command}\" on line {i}.");
                        return false;
                    }

                    primitiveType = tokens[1];

                    if (!s_Primitives.ContainsKey(primitiveType))
                    {
                        Console.Error.WriteLine($"Unknown primitive type \"{primitiveType}\" on line {i}.");
                        return false;
                    }
                    break;

                // Vertex (Position)
                case "v":
                    if (tokens.Length != 5)
                    {
                        Console.Error.WriteLine($"Incorrect number of tokens for command \"{command}\" on line {i}.");
                        return false;
                    }

                    Vector4D<float> position = new();
                    if (
                        !float.TryParse(tokens[1], out position.X) ||
                        !float.TryParse(tokens[2], out position.Y) ||
                        !float.TryParse(tokens[3], out position.Z) ||
                        !float.TryParse(tokens[4], out position.W)
                    ) {
                        Console.Error.WriteLine($"Failed to parse vertex position on line {i}.");
                        return false;
                    }

                    positions.Add(position);
                    break;

                // Vertex Texcoord
                case "vt":
                    Console.Error.WriteLine($"Unimplemented command \"{command}\" on line {i}.");
                    return false;

                // Cell
                case "c":
                    Primitive primitive = s_Primitives[primitiveType];

                    if (tokens.Length - 1 != primitive.VertexCount)
                    {
                        Console.Error.WriteLine($"Incorrect number of tokens for command \"{command}\" on line {i}.");
                        return false;
                    }

                    // Parse all the primitive's indices.
                    List<uint> primitiveIndices = new(tokens.Length - 1);
                    for (uint j = 1; j < tokens.Length; ++j)
                    {
                        if (uint.TryParse(tokens[j], out uint index))
                            // - 1 is because obj files start at index 1 instead of 0.
                            primitiveIndices.Add(index - 1);
                        else
                        {
                            Console.Error.WriteLine($"Failed to parse cell index on line {i}.");
                            return false;
                        }
                    }

                    // Tetrahedralize the primitive.
                    cellIndices.Capacity += primitive.CellIndices.Count;
                    foreach (int index in primitive.CellIndices)
                        cellIndices.Add(primitiveIndices[index]);

                    edges.Capacity += primitive.Edges.Count;
                    foreach (var edge in primitive.Edges)
                        edges.Add(new(primitiveIndices[(int)edge.Index1], primitiveIndices[(int)edge.Index2]));
                    break;

                // Unknown
                default:
                    Console.Error.WriteLine($"Ignoring unknown command \"{command}\" on line {i}.");
                    break;
            }
        }

        if (cellIndices.Count == 0)
        {
            Console.Error.WriteLine($"There was no loadable data in the model at \"{filepath}\".");
            return false;
        }

        // Bake vertices
        var colors = GetColors(positions.Count);
        List<float> vertices = new(positions.Count * 8);
        for (int i = 0; i < positions.Count; ++i)
        {
            var position = positions[i];
            vertices.Add(position.X);
            vertices.Add(position.Y);
            vertices.Add(position.Z);
            vertices.Add(position.W);

            var color = colors[i];
            vertices.Add(color.X);
            vertices.Add(color.Y);
            vertices.Add(color.Z);
            vertices.Add(color.W);
        }

        // Remove duplicate edges, then flatten their indices (baking them).
        List<uint> edgeIndices = edges.Distinct().SelectMany(edge => edge).ToList();

        // The model has been loaded.
        // Create the output model and return success.
        outModel = new(vertices, cellIndices, edgeIndices);
        Console.Error.WriteLine($"Successfully loaded model at \"{filepath}\".");
        return true;
    }

    // Returns a cached list of colors to use for vertices.
    // If a list is not cached for the given vertex count,
    // it is created, cached, and returned.
    // Lists returned have n ^ 3 colors, where n is an int >= 2.
    private static List<Vector4D<float>> GetColors(int vertexCount)
    {
        int optionsPerComponent = int.Max(2, (int)float.Ceiling(float.Cbrt(vertexCount)));
        if (!s_Colors.TryGetValue(optionsPerComponent, out var colors))
        {
            int colorCount = optionsPerComponent * optionsPerComponent * optionsPerComponent;
            s_Colors[optionsPerComponent] = colors = new(colorCount);

            float normalization = 1f / (optionsPerComponent - 1);
            for (uint b = 0; b < optionsPerComponent; ++b)
                for (uint g = 0; g < optionsPerComponent; ++g)
                    for (uint r = 0; r < optionsPerComponent; ++r)
                        colors.Add(new(r * normalization, g * normalization, b * normalization, 1f));
        }
        return colors;
    }

    static Model4D()
    {
        s_Primitives = new()
        {
            ["Tetrahedron"] = new Primitive([
                    0,  1,  2,  3,
            ], [
                new( 0,  1),
                new( 0,  2),
                new( 0,  3),
                new( 1,  2),
                new( 1,  3),
                new( 2,  3),
            ], 4),
            // Cube
            ["Hexahedron"] = new Primitive([
                    0,  1,  2,  4,
                    1,  4,  5,  7,
                    2,  4,  6,  7,
                    1,  2,  3,  7,
                    1,  2,  4,  7,
            ], [
                new( 0,  1),
                new( 0,  2),
                new( 0,  4),
                new( 1,  3),
                new( 1,  5),
                new( 2,  3),
                new( 2,  6),
                new( 3,  7),
                new( 4,  5),
                new( 4,  6),
                new( 5,  7),
                new( 6,  7),
            ], 8),
            /* Vertex Order:
                *  right, left, back, front, top, bottom
                */
            ["Octahedron"] = new Primitive([
                    0,  1,  2,  5,
                    0,  4,  2,  5,
                    3,  1,  2,  5,
                    3,  4,  2,  5,
            ], [
                new( 0,  1),
                new( 0,  2),
                new( 0,  4),
                new( 0,  5),
                new( 1,  2),
                new( 1,  3),
                new( 1,  5),
                new( 2,  3),
                new( 2,  4),
                new( 3,  4),
                new( 3,  5),
                new( 4,  5),
            ], 6),
            // TODO: translate new vertex order from blender
            ["Dodecahedron"] = new Primitive([
                    0,  3,  5,  6,
                    0,  2,  3,  6,
                    0,  4,  5,  6,
                    0,  1,  3,  5,
                    3,  5,  6,  7,
                    0,  2,  3, 12,
                    0,  3, 12, 13,
                    0,  1,  3, 13,
                    4,  5,  6, 14,
                    5,  6, 14, 15,
                    5,  6,  7, 15,
                    0,  1,  5,  8,
                    0,  5,  8,  9,
                    0,  4,  5,  9,
                    2,  3,  6, 10,
                    3,  6, 10, 11,
                    3,  6,  7, 11,
                    0,  4,  6, 16,
                    0,  6, 16, 17,
                    0,  2,  6, 17,
                    1,  3,  5, 18,
                    3,  5, 18, 19,
                    3,  5,  7, 19,
            ], [
                new( 0,  8),
                new( 0, 12),
                new( 0, 16),
                new( 1,  8),
                new( 1, 13),
                new( 1, 18),
                new( 2, 10),
                new( 2, 12),
                new( 2, 17),
                new( 3, 10),
                new( 3, 13),
                new( 3, 19),
                new( 4,  9),
                new( 4, 14),
                new( 4, 16),
                new( 5,  9),
                new( 5, 15),
                new( 5, 18),
                new( 6, 11),
                new( 6, 14),
                new( 6, 17),
                new( 7, 11),
                new( 7, 15),
                new( 7, 19),
                new( 8,  9),
                new(10, 11),
                new(12, 13),
                new(14, 15),
                new(16, 17),
                new(18, 19),
            ], 20),
            ["Icosahedron"] = new Primitive([
                    6,  7,  9, 11,
                    0,  1,  6,  9,
                    0,  4,  8,  9,
                    2,  5,  8,  9,
                    2,  3,  7,  9,
                    3,  7, 10, 11,
                    2,  3,  5, 10,
                    4,  5,  8, 10,
                    0,  1,  4, 10,
                    1,  6, 10, 11,
                    7,  9, 10, 11,
                    6,  9, 10, 11,
                    1,  6,  9, 10,
                    3,  7,  9, 10,
                    4,  8,  9, 10,
                    5,  8,  9, 10,
                    0,  4,  9, 10,
                    2,  5,  9, 10,
                    0,  1,  9, 10,
                    2,  3,  9, 10,
            ], [
                new( 0,  1),
                new( 0,  4),
                new( 0,  6),
                new( 0,  8),
                new( 0,  9),
                new( 1,  4),
                new( 1,  6),
                new( 1, 10),
                new( 1, 11),
                new( 2,  3),
                new( 2,  5),
                new( 2,  7),
                new( 2,  8),
                new( 2,  9),
                new( 3,  5),
                new( 3,  7),
                new( 3, 10),
                new( 3, 11),
                new( 4,  5),
                new( 4,  8),
                new( 4, 10),
                new( 5,  8),
                new( 5, 10),
                new( 6,  7),
                new( 6,  9),
                new( 6, 11),
                new( 7,  9),
                new( 7, 11),
                new( 8,  9),
                new(10, 11),
            ], 12),
            /* Vertex Order:
                *  bottom then top counter clockwise
                */
            ["TriangularPrism"] = new Primitive([
                    1,  3,  4,  5,
                    0,  1,  2,  3,
                    1,  2,  3,  5,
            ], [
                new( 0,  1),
                new( 0,  2),
                new( 0,  3),
                new( 1,  2),
                new( 1,  4),
                new( 2,  5),
                new( 3,  4),
                new( 3,  5),
                new( 4,  5),
            ], 6),
            /* Vertex Order:
                *  bottom then top counter clockwise
                */
            ["PentagonalPrism"] = new Primitive([
                    1,  2,  3,  7,
                    3,  7,  8,  9,
                    0,  3,  4,  9,
                    1,  6,  7,  9,
                    1,  3,  7,  9,
                    0,  1,  3,  9,
                    0,  1,  5,  9,
                    1,  5,  6,  9,
                    1,  3,  7,  9,
            ], [
                new( 0,  1),
                new( 0,  4),
                new( 0,  5),
                new( 1,  2),
                new( 1,  6),
                new( 2,  3),
                new( 2,  7),
                new( 3,  4),
                new( 3,  8),
                new( 4,  9),
                new( 5,  6),
                new( 5,  9),
                new( 6,  7),
                new( 7,  8),
                new( 8,  9),
            ], 10),
            // Square-based pyramid
            /* Vertex Order:
                *  binary representation of square base, then top point
                */
            ["TetragonalPyramid"] = new Primitive([
                    0,  1,  3,  4,
                    0,  3,  2,  4,
            ], [
                new( 0,  1),
                new( 0,  2),
                new( 0,  4),
                new( 1,  3),
                new( 1,  4),
                new( 2,  3),
                new( 2,  4),
                new( 3,  4),
            ], 5),
            // Pentagon-based pyramid
            /* Vertex Order:
                *  bottom counter clockwise, then top point
                */
            ["PentagonalPyramid"] = new Primitive([
                    0,  1,  2,  5,
                    0,  2,  3,  5,
                    0,  3,  4,  5,
            ], [
                new( 0,  1),
                new( 0,  4),
                new( 0,  5),
                new( 1,  2),
                new( 1,  5),
                new( 2,  3),
                new( 2,  5),
                new( 3,  4),
                new( 3,  5),
                new( 4,  5),
            ], 6),
        };

        s_Colors = [];
    }
}

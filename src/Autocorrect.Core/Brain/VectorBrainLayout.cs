namespace Autocorrect.Core.Brain;

public sealed class VectorLayoutResult
{
    public double[] X { get; init; } = Array.Empty<double>();
    public double[] Y { get; init; } = Array.Empty<double>();
    public List<(int A, int B)> Edges { get; init; } = new();
}

// Projects high-dimensional embeddings to 2D (PCA) and links each node to its nearest neighbors (cosine).
public static class VectorBrainLayout
{
    public static VectorLayoutResult Compute(IReadOnlyList<VectorPoint> points, int neighbors = 3)
    {
        var n = points.Count;
        if (n == 0)
        {
            return new VectorLayoutResult();
        }

        var dim = points[0].Vector.Length;
        var centered = BuildCentered(points, n, dim);
        var pc1 = PowerIteration(centered, dim, null);
        var pc2 = PowerIteration(centered, dim, pc1);

        var x = new double[n];
        var y = new double[n];
        for (var i = 0; i < n; i++)
        {
            x[i] = Dot(centered[i], pc1);
            y[i] = Dot(centered[i], pc2);
        }

        Normalize(x);
        Normalize(y);

        return new VectorLayoutResult { X = x, Y = y, Edges = NearestNeighborEdges(points, neighbors) };
    }

    private static double[][] BuildCentered(IReadOnlyList<VectorPoint> points, int n, int dim)
    {
        var mean = new double[dim];
        foreach (var point in points)
        {
            for (var k = 0; k < dim; k++)
            {
                mean[k] += point.Vector[k];
            }
        }

        for (var k = 0; k < dim; k++)
        {
            mean[k] /= n;
        }

        var centered = new double[n][];
        for (var i = 0; i < n; i++)
        {
            var row = new double[dim];
            var vector = points[i].Vector;
            for (var k = 0; k < dim; k++)
            {
                row[k] = vector[k] - mean[k];
            }

            centered[i] = row;
        }

        return centered;
    }

    // Finds a dominant principal component via power iteration, optionally deflating a prior component.
    private static double[] PowerIteration(double[][] centered, int dim, double[]? deflate)
    {
        var rnd = new Random(42);
        var v = new double[dim];
        for (var k = 0; k < dim; k++)
        {
            v[k] = rnd.NextDouble() - 0.5;
        }

        Unit(v);

        for (var iteration = 0; iteration < 64; iteration++)
        {
            var w = new double[dim];
            foreach (var row in centered)
            {
                var projection = Dot(row, v);
                for (var k = 0; k < dim; k++)
                {
                    w[k] += projection * row[k];
                }
            }

            if (deflate is not null)
            {
                var c = Dot(w, deflate);
                for (var k = 0; k < dim; k++)
                {
                    w[k] -= c * deflate[k];
                }
            }

            Unit(w);
            v = w;
        }

        return v;
    }

    private static List<(int A, int B)> NearestNeighborEdges(IReadOnlyList<VectorPoint> points, int neighbors)
    {
        var n = points.Count;
        var seen = new HashSet<long>();
        var edges = new List<(int A, int B)>();
        var best = new (int Index, double Score)[neighbors];

        for (var i = 0; i < n; i++)
        {
            for (var b = 0; b < neighbors; b++)
            {
                best[b] = (-1, double.NegativeInfinity);
            }

            for (var j = 0; j < n; j++)
            {
                if (i == j)
                {
                    continue;
                }

                var score = Dot(points[i].Vector, points[j].Vector);
                if (score <= best[neighbors - 1].Score)
                {
                    continue;
                }

                var slot = neighbors - 1;
                while (slot > 0 && score > best[slot - 1].Score)
                {
                    best[slot] = best[slot - 1];
                    slot--;
                }

                best[slot] = (j, score);
            }

            foreach (var (index, _) in best)
            {
                if (index < 0)
                {
                    continue;
                }

                var a = Math.Min(i, index);
                var c = Math.Max(i, index);
                if (seen.Add((long)a * n + c))
                {
                    edges.Add((a, c));
                }
            }
        }

        return edges;
    }

    private static double Dot(double[] a, double[] b)
    {
        double sum = 0;
        for (var k = 0; k < a.Length; k++)
        {
            sum += a[k] * b[k];
        }

        return sum;
    }

    private static double Dot(double[] a, float[] b)
    {
        double sum = 0;
        for (var k = 0; k < a.Length; k++)
        {
            sum += a[k] * b[k];
        }

        return sum;
    }

    private static double Dot(float[] a, float[] b)
    {
        double sum = 0;
        var length = Math.Min(a.Length, b.Length);
        for (var k = 0; k < length; k++)
        {
            sum += a[k] * b[k];
        }

        return sum;
    }

    private static void Unit(double[] v)
    {
        double norm = 0;
        foreach (var value in v)
        {
            norm += value * value;
        }

        norm = Math.Sqrt(norm);
        if (norm <= 1e-9)
        {
            return;
        }

        for (var k = 0; k < v.Length; k++)
        {
            v[k] /= norm;
        }
    }

    private static void Normalize(double[] values)
    {
        var min = values.Min();
        var max = values.Max();
        var range = max - min;
        if (range <= 1e-9)
        {
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = 0.5;
            }

            return;
        }

        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (values[i] - min) / range;
        }
    }
}

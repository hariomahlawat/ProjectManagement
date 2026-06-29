namespace ProjectManagement.Features.MediaLibrary.Services;

/// <summary>
/// Model-agnostic similarity helpers shared by identity matching and unnamed-face grouping.
/// Embeddings are expected to be L2-normalised, but the cosine implementation remains safe
/// when a future adapter supplies an unnormalised vector.
/// </summary>
public static class FaceSimilarityScoring
{
    public static double CosineSimilarity(IReadOnlyList<float> first, IReadOnlyList<float> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Count == 0 || first.Count != second.Count)
        {
            return -1d;
        }

        double dot = 0d;
        double firstNorm = 0d;
        double secondNorm = 0d;
        for (var index = 0; index < first.Count; index++)
        {
            var firstValue = first[index];
            var secondValue = second[index];
            dot += firstValue * secondValue;
            firstNorm += firstValue * firstValue;
            secondNorm += secondValue * secondValue;
        }

        return firstNorm <= 1e-12d || secondNorm <= 1e-12d
            ? -1d
            : Math.Clamp(dot / Math.Sqrt(firstNorm * secondNorm), -1d, 1d);
    }

    public static FaceReferenceScore ScoreReferences(
        IReadOnlyList<float> query,
        IEnumerable<IReadOnlyList<float>> references,
        int maximumReferences)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(references);

        var similarities = references
            .Take(Math.Clamp(maximumReferences, 1, 50))
            .Select(reference => CosineSimilarity(query, reference))
            .Where(value => value >= -1d && double.IsFinite(value))
            .OrderByDescending(value => value)
            .ToArray();
        if (similarities.Length == 0)
        {
            return FaceReferenceScore.Empty;
        }

        var topCount = Math.Min(3, similarities.Length);
        var topMean = similarities.Take(topCount).Average();
        var best = similarities[0];

        // A single accidental high reference must not dominate a well-established person.
        // With one reference the best score remains useful; with multiple references the
        // top-reference mean contributes enough weight to reward repeatable identity evidence.
        var aggregate = similarities.Length == 1
            ? best
            : best * 0.65d + topMean * 0.35d;

        return new FaceReferenceScore(
            Math.Clamp(aggregate, -1d, 1d),
            best,
            topMean,
            similarities.Length);
    }

    public static float[] CreateNormalisedCentroid(IEnumerable<IReadOnlyList<float>> vectors)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        var materialised = vectors.Where(vector => vector.Count > 0).ToArray();
        if (materialised.Length == 0)
        {
            return Array.Empty<float>();
        }

        var dimension = materialised[0].Count;
        if (materialised.Any(vector => vector.Count != dimension))
        {
            throw new ArgumentException("All face embeddings in a centroid must have the same dimension.", nameof(vectors));
        }

        var centroid = new float[dimension];
        foreach (var vector in materialised)
        {
            for (var index = 0; index < dimension; index++)
            {
                centroid[index] += vector[index];
            }
        }

        double normSquared = 0d;
        for (var index = 0; index < centroid.Length; index++)
        {
            centroid[index] /= materialised.Length;
            normSquared += centroid[index] * centroid[index];
        }

        var norm = Math.Sqrt(normSquared);
        if (norm <= 1e-12d)
        {
            return Array.Empty<float>();
        }

        for (var index = 0; index < centroid.Length; index++)
        {
            centroid[index] = (float)(centroid[index] / norm);
        }

        return centroid;
    }
}

public sealed record FaceReferenceScore(
    double AggregateSimilarity,
    double BestSimilarity,
    double MeanTopSimilarity,
    int ReferenceCount)
{
    public static FaceReferenceScore Empty { get; } = new(-1d, -1d, -1d, 0);
}

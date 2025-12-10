using System.Numerics.Tensors;

namespace MemoryIndexer.Core.Utilities;

/// <summary>
/// Vector math utilities for similarity calculations.
/// </summary>
public static class VectorMath
{
    /// <summary>
    /// Calculates cosine similarity between two vectors.
    /// Returns value between -1 and 1, where 1 means identical direction.
    /// </summary>
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        var dotProduct = TensorPrimitives.Dot(a, b);
        var normA = TensorPrimitives.Norm(a);
        var normB = TensorPrimitives.Norm(b);

        if (normA == 0 || normB == 0)
            return 0f;

        return dotProduct / (normA * normB);
    }

    /// <summary>
    /// Calculates cosine similarity between two vectors.
    /// </summary>
    public static float CosineSimilarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
        => CosineSimilarity(a.Span, b.Span);

    /// <summary>
    /// Calculates cosine similarity between two vectors.
    /// </summary>
    public static float CosineSimilarity(float[] a, float[] b)
        => CosineSimilarity(a.AsSpan(), b.AsSpan());

    /// <summary>
    /// Calculates dot product between two vectors.
    /// </summary>
    public static float DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            return 0f;

        return TensorPrimitives.Dot(a, b);
    }

    /// <summary>
    /// Calculates Euclidean distance between two vectors.
    /// </summary>
    public static float EuclideanDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            return float.MaxValue;

        return TensorPrimitives.Distance(a, b);
    }

    /// <summary>
    /// Normalizes a vector to unit length.
    /// </summary>
    public static float[] Normalize(ReadOnlySpan<float> vector)
    {
        var norm = TensorPrimitives.Norm(vector);
        if (norm == 0)
            return vector.ToArray();

        var result = new float[vector.Length];
        for (var i = 0; i < vector.Length; i++)
        {
            result[i] = vector[i] / norm;
        }

        return result;
    }
}

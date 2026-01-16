namespace LancerMcp.Models;

public sealed class QueryEmbeddingInput
{
    public float[] Vector { get; init; } = Array.Empty<float>();
    public int Dims => Vector.Length;
    public string? Model { get; init; }
}

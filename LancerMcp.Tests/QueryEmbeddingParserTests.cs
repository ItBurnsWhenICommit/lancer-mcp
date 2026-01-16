using System;
using LancerMcp.Services;
using Xunit;

namespace LancerMcp.Tests;

public sealed class QueryEmbeddingParserTests
{
    [Fact]
    public void Parse_InvalidBase64_ReturnsError()
    {
        var result = QueryEmbeddingParser.TryParse("not-base64", null, null, 4096);

        Assert.False(result.Success);
        Assert.Equal("invalid_query_embedding", result.ErrorCode);
    }

    [Fact]
    public void Parse_DimsMismatch_ReturnsError()
    {
        var bytes = new byte[8]; // 2 floats
        var base64 = Convert.ToBase64String(bytes);

        var result = QueryEmbeddingParser.TryParse(base64, 3, null, 4096);

        Assert.False(result.Success);
        Assert.Equal("invalid_query_embedding_dims", result.ErrorCode);
    }

    [Fact]
    public void Parse_ValidEmbedding_ReturnsVector()
    {
        var bytes = new byte[4]; // 1 float = 0
        var base64 = Convert.ToBase64String(bytes);

        var result = QueryEmbeddingParser.TryParse(base64, 1, "Model-A", 4096);

        Assert.True(result.Success);
        Assert.NotNull(result.Vector);
        Assert.Single(result.Vector);
        Assert.Equal("model-a", result.Model);
    }
}

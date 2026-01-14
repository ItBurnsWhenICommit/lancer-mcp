using Npgsql;
using Pgvector;
using LancerMcp.Services;

namespace LancerMcp.Tests;

public sealed class VectorTypeHandlerTests
{
    [Fact]
    public void SetValue_SetsVectorTypeName()
    {
        var handler = new VectorTypeHandler();
        var parameter = new NpgsqlParameter();

        handler.SetValue(parameter, new Vector(new float[] { 1f, 2f, 3f }));

        Assert.Equal("vector", parameter.DataTypeName);
    }
}

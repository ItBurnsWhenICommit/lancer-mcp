namespace TestData.Security;

public sealed class PasswordHasher
{
    public string HashPassword(string password)
    {
        return $"hash:{password}";
    }

    public bool Verify(string password, string salt)
    {
        return HashPassword(password).Contains(salt, StringComparison.Ordinal);
    }
}

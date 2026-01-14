using TestData.Models;
using TestData.Security;

namespace TestData.Services;

public sealed class AuthService
{
    private readonly PasswordHasher _hasher;

    public AuthService(PasswordHasher hasher)
    {
        _hasher = hasher;
    }

    public bool Login(User user, string password)
    {
        return _hasher.Verify(password, user.Email);
    }
}

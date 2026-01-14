using TestData.Interfaces;
using TestData.Models;

namespace TestData.Services;

public sealed class UserService
{
    private readonly IUserStore _store;

    public UserService(IUserStore store)
    {
        _store = store;
    }

    public User CreateUser(string email)
    {
        var user = new User(Guid.NewGuid().ToString("N"), email);
        _store.Save(user);
        return user;
    }

    public User? FindByEmail(string email)
    {
        return _store.FindByEmail(email);
    }
}

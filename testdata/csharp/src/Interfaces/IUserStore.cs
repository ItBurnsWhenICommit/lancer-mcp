using TestData.Models;

namespace TestData.Interfaces;

public interface IUserStore
{
    User? FindByEmail(string email);
    void Save(User user);
}

namespace TestData.Security;

public static class PasswordPolicy
{
    public static bool IsStrong(string password)
    {
        return password.Length >= 12;
    }
}

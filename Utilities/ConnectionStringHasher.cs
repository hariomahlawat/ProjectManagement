using System;
using System.Security.Cryptography;
using System.Text;

namespace ProjectManagement.Utilities;

public static class ConnectionStringHasher
{
    private const string NullHashValue = "null";

    public static string Hash(string? connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            return NullHashValue;
        }

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(connectionString);
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hashBytes);
    }
}

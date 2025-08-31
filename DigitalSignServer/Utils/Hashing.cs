using System.Security.Cryptography;

namespace DigitalSignServer.Utils;
public static class Hashing
{
    public static string ComputeSha256(Stream stream)
    {
        // NOTE: stream position should be at 0
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

using System.Globalization;
using System.Security.Cryptography;

namespace DigitalSignServer.Utils
{
    // services/security/TokenGenerator.cs
    public static class TokenGenerator
    {
        public static string CreateUrlToken(int bytes = 48) // ~64 chars Base64Url
        {
            var buf = RandomNumberGenerator.GetBytes(bytes);
            return Base64UrlEncode(buf);
        }

        public static string CreateNumericOtp(int digits = 6)
        {
            var rnd = RandomNumberGenerator.GetInt32((int)Math.Pow(10, digits - 1), (int)Math.Pow(10, digits));
            return rnd.ToString(CultureInfo.InvariantCulture);
        }

        private static string Base64UrlEncode(byte[] input) =>
            Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

}

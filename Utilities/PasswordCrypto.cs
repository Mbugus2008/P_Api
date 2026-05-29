using System.Security.Cryptography;
using System.Text;

namespace ParcelAPI.Utilities
{
    public static class PasswordCrypto
    {
        private const string Scheme = "sha256";

        public static string HashIfNeeded(string? password)
        {
            var clean = (password ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(clean) || IsHashedPassword(clean))
            {
                return clean;
            }

            return HashPassword(clean);
        }

        public static bool IsHashedPassword(string value)
        {
            var clean = value.Trim();
            if (!clean.StartsWith($"{Scheme}$", StringComparison.Ordinal))
            {
                return false;
            }

            var parts = clean.Split('$');
            return parts.Length == 3 &&
                   !string.IsNullOrWhiteSpace(parts[1]) &&
                   !string.IsNullOrWhiteSpace(parts[2]);
        }

        public static string HashPassword(string password)
        {
            var clean = password.Trim();
            if (string.IsNullOrEmpty(clean))
            {
                return string.Empty;
            }

            var saltBytes = RandomNumberGenerator.GetBytes(16);
            var salt = Convert.ToBase64String(saltBytes);
            var hash = HashWithSalt(clean, salt);
            return $"{Scheme}${salt}${hash}";
        }

        private static string HashWithSalt(string password, string salt)
        {
            var payload = Encoding.UTF8.GetBytes($"{salt}:{password}");
            var hashBytes = SHA256.HashData(payload);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }
}
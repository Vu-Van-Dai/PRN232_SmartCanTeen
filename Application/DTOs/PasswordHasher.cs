using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public static class PasswordHasher
    {
        public static string Hash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(bytes);
        }

        public static bool Verify(string input, string hash)
        {
            var hashedInput = Hash(input);
            return hashedInput == hash;
        }
    }
}

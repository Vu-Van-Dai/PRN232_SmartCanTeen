using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public static class PasswordHasher
    {
        public static bool Verify(string input, string hash)
            => input == hash; // demo, vì admin đang seed "HASHED_PASSWORD"
    }
}

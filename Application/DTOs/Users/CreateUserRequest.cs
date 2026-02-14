using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs.Users
{
    public class CreateUserRequest
    {
        public string Email { get; set; } = default!;
        public string FullName { get; set; } = default!;
        public string? StudentCode { get; set; }
        public string? Password { get; set; }
        public string Role { get; set; } = default!;
    }
}

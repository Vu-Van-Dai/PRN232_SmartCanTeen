using Core.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Entities
{
    public class User : BaseEntity
    {
        public string Email { get; set; } = default!;
        public string PasswordHash { get; set; } = default!;
        public string? FullName { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; } = false;

        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    }
}

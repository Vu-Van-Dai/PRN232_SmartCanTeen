using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Entities
{
    public class StaffAssignment
    {
        public Guid UserId { get; set; }
        public User User { get; set; } = default!;

        public Guid CampusId { get; set; }
        public Campus Campus { get; set; } = default!;

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    }
}

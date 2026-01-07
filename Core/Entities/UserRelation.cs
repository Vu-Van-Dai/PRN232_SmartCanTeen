using Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Entities
{
    public class UserRelation
    {
        public Guid ParentId { get; set; }
        public User Parent { get; set; } = default!;

        public Guid StudentId { get; set; }
        public User Student { get; set; } = default!;

        public RelationType RelationType { get; set; }
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

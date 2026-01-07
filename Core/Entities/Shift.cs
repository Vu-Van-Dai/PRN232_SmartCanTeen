using Core.Common;
using Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Entities
{
    public class Shift : BaseEntity
    {
        public Guid CampusId { get; set; }
        public Campus Campus { get; set; } = default!;

        public Guid UserId { get; set; }
        public User User { get; set; } = default!;

        public DateTime OpenedAt { get; set; }
        public DateTime? ClosedAt { get; set; }

        public decimal OpeningCash { get; set; }
        public decimal ClosingCash { get; set; }

        public ShiftStatus Status { get; set; }
    }
}

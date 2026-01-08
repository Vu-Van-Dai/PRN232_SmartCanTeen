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

        public Guid UserId { get; set; }           // Staff
        public User User { get; set; } = default!;

        public DateTime OpenedAt { get; set; }
        public DateTime? ClosedAt { get; set; }

        // ===== SYSTEM TOTAL (READONLY LOGIC) =====
        public decimal SystemCashTotal { get; set; }
        public decimal SystemQrTotal { get; set; }
        public decimal SystemOnlineTotal { get; set; }

        // ===== STAFF DECLARE (MUST MATCH SYSTEM) =====
        public decimal? StaffCashInput { get; set; }
        public decimal? StaffQrInput { get; set; }

        public ShiftStatus Status { get; set; }
    }
}

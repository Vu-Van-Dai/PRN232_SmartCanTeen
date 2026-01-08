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
        public Guid Id { get; set; }
        public Guid CampusId { get; set; }
        public Campus Campus { get; set; } = null!;

        public Guid UserId { get; set; }       // nhân viên
        public User User { get; set; } = null!;

        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        // ===== SYSTEM TOTAL (readonly logic) =====
        public decimal SystemCashTotal { get; set; }
        public decimal SystemQrTotal { get; set; }
        public decimal SystemOnlineTotal { get; set; }

        // ===== STAFF DECLARE (PHẢI = SYSTEM) =====
        public decimal? StaffCashInput { get; set; }
        public decimal? StaffQrInput { get; set; }

        public ShiftStatus Status { get; set; }
    }
}

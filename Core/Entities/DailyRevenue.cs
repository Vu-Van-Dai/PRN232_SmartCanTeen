using Core.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Entities
{
    public class DailyRevenue : BaseEntity
    {
        public Guid CampusId { get; set; }
        public Campus Campus { get; set; } = default!;

        public DateTime Date { get; set; }   // yyyy-MM-dd

        // ===== SYSTEM TOTAL =====
        public decimal TotalCash { get; set; }
        public decimal TotalQr { get; set; }
        public decimal TotalOnline { get; set; }

        // ===== AUDIT =====
        public Guid ClosedByUserId { get; set; }   // Manager
        public User ClosedByUser { get; set; } = default!;

        public DateTime ClosedAt { get; set; }
    }
}

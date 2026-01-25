using Core.Common;
using Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Entities
{
    public class InventoryLog : BaseEntity
    {
        public Guid ItemId { get; set; }
        public MenuItem Item { get; set; } = default!;

        // Số lượng thay đổi (+ / -)
        public int ChangeQuantity { get; set; }

        public InventoryLogReason Reason { get; set; }

        // Tham chiếu Order / Shift
        public Guid? ReferenceId { get; set; }

        public Guid PerformedByUserId { get; set; }
        public User PerformedByUser { get; set; } = default!;
    }
}

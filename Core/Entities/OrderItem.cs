using Core.Common;
using Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Entities
{
    public class OrderItem : BaseEntity
    {
        public Guid OrderId { get; set; }
        public Order Order { get; set; } = default!;

        public Guid ItemId { get; set; }
        public MenuItem Item { get; set; } = default!;

        public int Quantity { get; set; }

        // Quantity already refunded/cancelled for this line (for KDS + audit).
        public int CancelledQuantity { get; set; } = 0;

        // ReadyMade items are completed immediately; Prepared items start pending.
        public OrderItemStatus Status { get; set; } = OrderItemStatus.Pending;

        // Snapshot price tại thời điểm mua
        public decimal UnitPrice { get; set; }
    }
}

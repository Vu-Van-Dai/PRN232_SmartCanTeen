using Core.Common;
using System;

namespace Core.Entities
{
    public class RefundReceiptItem : BaseEntity
    {
        public Guid RefundReceiptId { get; set; }
        public RefundReceipt RefundReceipt { get; set; } = default!;

        public Guid OrderItemId { get; set; }
        public OrderItem OrderItem { get; set; } = default!;

        public int Quantity { get; set; }

        // Snapshot unit price at time of refund (copied from OrderItem.UnitPrice)
        public decimal UnitPrice { get; set; }
    }
}

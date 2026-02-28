using Core.Common;
using Core.Enums;
using System;
using System.Collections.Generic;

namespace Core.Entities
{
    public class RefundReceipt : BaseEntity
    {
        public Guid OriginalOrderId { get; set; }
        public Order OriginalOrder { get; set; } = default!;

        public Guid ShiftId { get; set; }
        public Shift Shift { get; set; } = default!;

        public decimal RefundAmount { get; set; }

        public PaymentMethod RefundMethod { get; set; }

        // Actual amount returned to customer (may differ due to rounding / partial returns).
        public decimal AmountReturned { get; set; }

        public Guid PerformedByUserId { get; set; }
        public User PerformedByUser { get; set; } = default!;

        public string Reason { get; set; } = string.Empty;

        public ICollection<RefundReceiptItem> Items { get; set; } = new List<RefundReceiptItem>();
    }
}

using System;
using System.Collections.Generic;

namespace Application.DTOs
{
    public class RefundPosOrderItemsRequest
    {
        public List<RefundPosOrderItemLine> Items { get; set; } = new();

        // Actual amount returned to customer (optional; defaults to computed refund amount).
        public decimal? AmountReturned { get; set; }

        public string? Reason { get; set; }
    }

    public class RefundPosOrderItemLine
    {
        public Guid OrderItemId { get; set; }
        public int Quantity { get; set; }
    }
}

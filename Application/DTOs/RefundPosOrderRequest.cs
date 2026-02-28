using Core.Enums;

namespace Application.DTOs
{
    public class RefundPosOrderRequest
    {
        // Refund a partial amount (<= original total). Required.
        public decimal RefundAmount { get; set; }

        // Actual amount returned to customer (optional; defaults to RefundAmount).
        public decimal? AmountReturned { get; set; }

        public string Reason { get; set; } = string.Empty;
    }
}

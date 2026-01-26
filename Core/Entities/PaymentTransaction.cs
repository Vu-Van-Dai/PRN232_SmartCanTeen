using Core.Common;
using Core.Enums;
using System;

namespace Core.Entities
{
    public class PaymentTransaction : BaseEntity
    {
        public Guid PerformedByUserId { get; set; }

        // Provider-specific reference (for PayOS we'll store: PAYOS-{orderCode})
        public string PaymentRef { get; set; } = null!;

        public decimal Amount { get; set; }

        public PaymentPurpose Purpose { get; set; }

        public Guid? WalletId { get; set; }
        public Guid? ShiftId { get; set; }
        public Guid? OrderId { get; set; }

        public bool IsSuccess { get; set; }
    }
}

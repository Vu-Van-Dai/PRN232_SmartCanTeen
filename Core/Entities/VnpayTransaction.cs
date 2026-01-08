using Core.Common;
using Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Entities
{
    public class VnpayTransaction : BaseEntity
    {
        public Guid PerformedByUserId { get; set; }
        public string VnpTxnRef { get; set; } = null!;
        public decimal Amount { get; set; }

        public PaymentPurpose Purpose { get; set; }

        public Guid? WalletId { get; set; }
        public Guid? ShiftId { get; set; }
        public Guid? OrderId { get; set; }

        public bool IsSuccess { get; set; }
    }
}

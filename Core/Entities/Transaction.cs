using Core.Common;
using Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Entities
{
    public class Transaction : BaseEntity
    {
        public Guid CampusId { get; set; }
        public Campus Campus { get; set; } = default!;

        public Guid? WalletId { get; set; }
        public Wallet? Wallet { get; set; }

        public Guid? OrderId { get; set; }
        public Order? Order { get; set; }

        public decimal Amount { get; set; }

        public TransactionType Type { get; set; }
        public TransactionStatus Status { get; set; }

        public PaymentMethod PaymentMethod { get; set; }

        public Guid PerformedByUserId { get; set; }
        public User PerformedByUser { get; set; } = default!;
    }
}

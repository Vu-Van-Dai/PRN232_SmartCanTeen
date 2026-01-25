using Core.Common;
using Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Entities
{
    public class Wallet : BaseEntity
    {
        public Guid UserId { get; set; }
        public User User { get; set; } = default!;

        public decimal Balance { get; set; }
        public WalletStatus Status { get; set; } = WalletStatus.Active;

        public DateTime? ClosedAt { get; set; }
    }
}

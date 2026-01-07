using Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Entities
{
    public class WalletAccess
    {
        public Guid WalletId { get; set; }
        public Wallet Wallet { get; set; } = default!;

        public Guid UserId { get; set; }
        public User User { get; set; } = default!;

        public WalletAccessType AccessType { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

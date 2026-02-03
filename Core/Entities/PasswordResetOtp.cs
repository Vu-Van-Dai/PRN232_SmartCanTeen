using Core.Common;
using System;

namespace Core.Entities
{
    public class PasswordResetOtp : BaseEntity
    {
        public Guid UserId { get; set; }
        public User User { get; set; } = default!;

        public string CodeHash { get; set; } = default!;
        public DateTime ExpiresAt { get; set; }

        public int Attempts { get; set; } = 0;
        public DateTime? ConsumedAt { get; set; }
    }
}

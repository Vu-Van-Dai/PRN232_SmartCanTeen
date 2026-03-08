using Core.Common;

namespace Core.Entities
{
    public class UserFcmToken : BaseEntity
    {
        public Guid UserId { get; set; }
        public string Token { get; set; } = default!;

        public DateTime? LastSeenAt { get; set; }
        public bool IsActive { get; set; } = true;

        public User? User { get; set; }
    }
}

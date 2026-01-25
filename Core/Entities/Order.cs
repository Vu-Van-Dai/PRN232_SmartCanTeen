using Core.Common;
using Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Entities
{
    public class Order : BaseEntity
    {
        // ===== ONLINE =====
        public Guid? WalletId { get; set; }
        public Wallet? Wallet { get; set; }

        // ===== WHO ORDER =====
        public Guid OrderedByUserId { get; set; }
        public User OrderedByUser { get; set; } = default!;

        // ===== OFFLINE =====
        public Guid? ShiftId { get; set; }
        public Shift? Shift { get; set; }

        public OrderSource OrderSource { get; set; }   // Online / Offline
        public PaymentMethod PaymentMethod { get; set; }

        public OrderStatus Status { get; set; }

        // ===== PRICE =====
        public decimal SubTotal { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TotalPrice { get; set; }
        //===== Kitchen ====
        public DateTime? PickupTime { get; set; }   // Giờ khách muốn lấy
        public bool IsUrgent { get; set; }           // Cần nấu gấp
        public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    }
}

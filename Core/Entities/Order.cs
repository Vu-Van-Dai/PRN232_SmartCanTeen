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
        public Guid CampusId { get; set; }
        public Campus Campus { get; set; } = default!;

        public Guid? WalletId { get; set; }
        public Wallet? Wallet { get; set; }

        public Guid OrderedByUserId { get; set; }
        public User OrderedByUser { get; set; } = default!;

        public Guid? ShiftId { get; set; }
        public Shift? Shift { get; set; }

        public OrderSource OrderSource { get; set; }
        public PaymentMethod PaymentMethod { get; set; }

        public OrderStatus Status { get; set; }

        public decimal SubTotal { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TotalPrice { get; set; }

        public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    }
}

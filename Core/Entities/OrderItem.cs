using Core.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Entities
{
    public class OrderItem : BaseEntity
    {
        public Guid OrderId { get; set; }
        public Order Order { get; set; } = default!;

        public Guid ItemId { get; set; }
        public MenuItem Item { get; set; } = default!;

        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }
}

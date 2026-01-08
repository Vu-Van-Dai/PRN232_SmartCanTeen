using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class CreateOfflineOrderRequest
    {
        public decimal TotalPrice { get; set; }
        public List<CreateOfflineOrderItem> Items { get; set; } = new();
    }

    public class CreateOfflineOrderItem
    {
        public Guid ItemId { get; set; }
        public int Quantity { get; set; }
    }
}

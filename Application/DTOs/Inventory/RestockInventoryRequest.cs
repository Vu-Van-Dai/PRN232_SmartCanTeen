using System;

namespace Application.DTOs.Inventory
{
    public class RestockInventoryRequest
    {
        public Guid ItemId { get; set; }
        public int Quantity { get; set; }
        public string? Note { get; set; }
    }
}

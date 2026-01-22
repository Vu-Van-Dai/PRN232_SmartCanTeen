using System;
using System.Collections.Generic;

namespace Application.DTOs
{
    public class CreateOnlineOrderRequest
    {
        public List<CreateOrderItemDto> Items { get; set; } = new();
    }

    public class CreateOrderItemDto
    {
        public Guid ItemId { get; set; }
        public int Quantity { get; set; }
    }
}

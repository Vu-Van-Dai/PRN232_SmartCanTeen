using System;
using System.Collections.Generic;

namespace Application.DTOs
{
    public class PromotionQuoteRequest
    {
        public List<PromotionQuoteItemDto> Items { get; set; } = new();
        public string? PromoCode { get; set; }
    }

    public class PromotionQuoteItemDto
    {
        public Guid ItemId { get; set; }
        public int Quantity { get; set; }
    }
}

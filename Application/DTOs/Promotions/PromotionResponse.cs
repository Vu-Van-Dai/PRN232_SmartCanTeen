using System;

namespace Application.DTOs.Promotions
{
    public class PromotionResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string Code { get; set; } = default!;
        public string Type { get; set; } = default!;
        public bool IsActive { get; set; }
        public DateTime? StartAt { get; set; }
        public DateTime? EndAt { get; set; }
        public string ConfigJson { get; set; } = "{}";
        public DateTime CreatedAt { get; set; }
    }
}

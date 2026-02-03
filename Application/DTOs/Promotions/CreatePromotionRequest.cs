using System;
using System.Text.Json;

namespace Application.DTOs.Promotions
{
    public class CreatePromotionRequest
    {
        public string Name { get; set; } = default!;
        public string Code { get; set; } = default!;
        public string Type { get; set; } = default!;

        public bool IsActive { get; set; } = true;

        public DateTime? StartAt { get; set; }
        public DateTime? EndAt { get; set; }

        public JsonElement Config { get; set; }
    }
}

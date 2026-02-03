using Core.Common;
using Core.Enums;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Entities
{
    public class Promotion : BaseEntity, ISoftDelete
    {
        public string Name { get; set; } = default!;
        public string Code { get; set; } = default!;

        public PromotionType Type { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? StartAt { get; set; }
        public DateTime? EndAt { get; set; }

        [Column(TypeName = "jsonb")]
        public string ConfigJson { get; set; } = "{}";

        public bool IsDeleted { get; set; } = false;
    }
}

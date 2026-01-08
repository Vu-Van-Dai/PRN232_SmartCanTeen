using Core.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Entities
{
    public class MenuItem : BaseEntity, ISoftDelete
    {
        public Guid CampusId { get; set; }
        public Campus Campus { get; set; } = default!;

        public Guid CategoryId { get; set; }
        public Category Category { get; set; } = default!;

        public string Name { get; set; } = default!;
        public decimal Price { get; set; }

        public int InventoryQuantity { get; set; }
        public string? ImageUrl { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; } = false;

        // Kiểm soát truy cập đồng thời lạc quan trong PostgreSQL
        [ConcurrencyCheck]
        public uint xmin { get; set; }
    }
}

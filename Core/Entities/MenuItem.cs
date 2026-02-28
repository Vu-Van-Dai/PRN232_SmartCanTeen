using Core.Common;
using Core.Enums;
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
        public Guid CategoryId { get; set; }
        public Category Category { get; set; } = default!;

        public string Name { get; set; } = default!;
        public decimal Price { get; set; }

        public ProductType ProductType { get; set; } = ProductType.Prepared;

        public int InventoryQuantity { get; set; }
        public string? ImageUrl { get; set; }

        public ICollection<MenuItemImage> Images { get; set; } = new List<MenuItemImage>();

        public bool IsActive { get; set; } = true;
        public bool IsDeleted { get; set; } = false;
    }
}

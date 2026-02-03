using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs.MenuItems
{
    public class UpdateMenuItemRequest
    {
        public string Name { get; set; } = default!;
        public decimal Price { get; set; }
        public int InventoryQuantity { get; set; }
        public List<string>? ImageUrls { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsActive { get; set; }
    }
}

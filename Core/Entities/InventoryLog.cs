using Core.Common;
using Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Entities
{
    public class InventoryLog : BaseEntity
    {
        public Guid CampusId { get; set; }
        public Campus Campus { get; set; } = default!;

        public Guid ItemId { get; set; }
        public MenuItem Item { get; set; } = default!;

        public int ChangeQuantity { get; set; }
        public InventoryLogReason Reason { get; set; }

        public Guid? ReferenceId { get; set; }
        public string? Note { get; set; }

        public Guid PerformedByUserId { get; set; }
        public User PerformedByUser { get; set; } = default!;
    }
}

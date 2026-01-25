using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Enums
{
    public enum RelationType
    {
        Father = 0,
        Mother = 1,
        Guardian = 2
    }

    public enum InventoryLogReason
    {
        Sale = 1,
        Damage = 2,
        Expired = 3,
        Adjustment = 4,
        Restock = 5
    }
}

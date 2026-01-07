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
        Sale = 0,
        Damage = 1,
        Expired = 2,
        Adjustment = 3
    }
}

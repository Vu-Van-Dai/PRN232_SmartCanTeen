using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Common
{
    public interface IInventoryNotifier
    {
        Task MenuItemStockChanged(
            Guid itemId,
            int inventoryQuantity
        );
    }
}

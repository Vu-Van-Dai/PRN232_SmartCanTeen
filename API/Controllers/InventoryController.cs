using Application.DTOs.Inventory;
using Core.Entities;
using Core.Enums;
using Core.Common;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace API.Controllers
{
    [ApiController]
    [Route("api/inventory")]
    [Authorize(Roles = "StaffKitchen,Manager,AdminSystem")]
    public class InventoryController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IInventoryNotifier _notifier;

        public InventoryController(AppDbContext db, IInventoryNotifier notifier)
        {
            _db = db;
            _notifier = notifier;
        }

        /// <summary>
        /// Nhập kho (tăng tồn) cho MenuItem
        /// </summary>
        [HttpPost("restock")]
        public async Task<IActionResult> Restock(RestockInventoryRequest request)
        {
            if (request.Quantity <= 0)
                return BadRequest("Invalid quantity");

            var rawUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(rawUserId) || !Guid.TryParse(rawUserId, out var performedByUserId))
                return Unauthorized();

            var item = await _db.MenuItems.FirstOrDefaultAsync(x => x.Id == request.ItemId && !x.IsDeleted);
            if (item == null)
                return NotFound("Menu item not found");

            item.InventoryQuantity += request.Quantity;

            _db.InventoryLogs.Add(new InventoryLog
            {
                Id = Guid.NewGuid(),
                ItemId = item.Id,
                ChangeQuantity = request.Quantity,
                Reason = InventoryLogReason.Restock,
                ReferenceId = null,
                PerformedByUserId = performedByUserId,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            await _notifier.MenuItemStockChanged(item.Id, item.InventoryQuantity);

            return Ok(new { itemId = item.Id, inventoryQuantity = item.InventoryQuantity });
        }
    }
}

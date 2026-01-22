using API.Hubs;
using Application.DTOs.MenuItems;
using Core.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace API.Controllers
{
    [ApiController]
    [Route("api/menu-items")]
    [Authorize(Roles = "Manager")]
    public class MenuItemsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ICurrentCampusService _campus;
        private readonly IHubContext<ManagementHub> _hub;

        public MenuItemsController(AppDbContext db, ICurrentCampusService campus, IHubContext<ManagementHub> hub)
        {
            _db = db;
            _campus = campus;
            _hub = hub;
        }

        // =========================
        // GET LIST
        // =========================
        [HttpGet]
        public async Task<IActionResult> GetList()
        {
            var items = await _db.MenuItems
                .Include(x => x.Category)
                .Where(x =>
                    x.CampusId == _campus.CampusId &&
                    !x.IsDeleted
                )
                .Select(x => new MenuItemResponse
                {
                    Id = x.Id,
                    CategoryId = x.CategoryId,
                    CategoryName = x.Category.Name,
                    Name = x.Name,
                    Price = x.Price,
                    InventoryQuantity = x.InventoryQuantity,
                    ImageUrl = x.ImageUrl,
                    IsActive = x.IsActive,
                    xmin = x.xmin
                })
                .ToListAsync();

            return Ok(items);
        }

        // =========================
        // CREATE
        // =========================
        [HttpPost]
        public async Task<IActionResult> Create(CreateMenuItemRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return BadRequest("Name is required");

            if (request.Price <= 0)
                return BadRequest("Invalid price");

            if (request.InventoryQuantity < 0)
                return BadRequest("Invalid inventory quantity");

            var categoryExists = await _db.Categories.AnyAsync(x =>
                x.Id == request.CategoryId &&
                x.CampusId == _campus.CampusId &&
                !x.IsDeleted
            );

            if (!categoryExists)
                return BadRequest("Category not found");

            var duplicate = await _db.MenuItems.AnyAsync(x =>
                x.CampusId == _campus.CampusId &&
                x.Name == request.Name &&
                !x.IsDeleted
            );

            if (duplicate)
                return BadRequest("Menu item name already exists");

            var item = new MenuItem
            {
                Id = Guid.NewGuid(),
                CampusId = _campus.CampusId,
                CategoryId = request.CategoryId,
                Name = request.Name,
                Price = request.Price,
                InventoryQuantity = request.InventoryQuantity,
                ImageUrl = request.ImageUrl,
                IsActive = request.IsActive,
                IsDeleted = false
            };

            _db.MenuItems.Add(item);
            await _db.SaveChangesAsync();

            await _hub.Clients
            .Group($"campus-{_campus.CampusId}")
            .SendAsync("MenuItemCreated", new
            {
                id = item.Id,
                categoryId = item.CategoryId,
                name = item.Name,
                price = item.Price,
                inventoryQuantity = item.InventoryQuantity,
                imageUrl = item.ImageUrl,
                isActive = item.IsActive
            });

            return Ok(item.Id);
        }

        // =========================
        // UPDATE (OPTIMISTIC LOCK)
        // =========================
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, UpdateMenuItemRequest request)
        {
            var item = await _db.MenuItems.FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.CampusId == _campus.CampusId &&
                !x.IsDeleted
            );

            if (item == null)
                return NotFound();

            if (item.xmin != request.xmin)
                return Conflict("Data was modified by another user");

            var duplicate = await _db.MenuItems.AnyAsync(x =>
                x.Id != id &&
                x.CampusId == _campus.CampusId &&
                x.Name == request.Name &&
                !x.IsDeleted
            );

            if (duplicate)
                return BadRequest("Menu item name already exists");

            item.Name = request.Name;
            item.Price = request.Price;
            item.InventoryQuantity = request.InventoryQuantity;
            item.ImageUrl = request.ImageUrl;
            item.IsActive = request.IsActive;

            await _db.SaveChangesAsync();

            await _hub.Clients
            .Group($"campus-{_campus.CampusId}")
            .SendAsync("MenuItemUpdated", new
            {
                id = item.Id,
                name = item.Name,
                price = item.Price,
                inventoryQuantity = item.InventoryQuantity,
                imageUrl = item.ImageUrl,
                isActive = item.IsActive
            });

            return Ok();
        }

        // =========================
        // DELETE (SOFT)
        // =========================
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var item = await _db.MenuItems.FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.CampusId == _campus.CampusId &&
                !x.IsDeleted
            );

            if (item == null)
                return NotFound();

            item.IsDeleted = true;
            await _db.SaveChangesAsync();

            await _hub.Clients
            .Group($"campus-{_campus.CampusId}")
            .SendAsync("MenuItemDeleted", new
            {
                id = item.Id
            });

            return Ok();
        }
    }
}

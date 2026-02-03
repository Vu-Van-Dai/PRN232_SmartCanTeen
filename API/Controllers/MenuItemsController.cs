using API.Hubs;
using Application.DTOs.MenuItems;
using Core.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;

namespace API.Controllers
{
    [ApiController]
    [Route("api/menu-items")]
    [Authorize]
    public class MenuItemsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<ManagementHub> _hub;

        public MenuItemsController(AppDbContext db, IHubContext<ManagementHub> hub)
        {
            _db = db;
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
                .Include(x => x.Images)
                .Where(x =>
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
                    ImageUrls = x.Images
                        .OrderBy(i => i.SortOrder)
                        .Select(i => i.Url)
                        .ToList(),
                    ImageUrl = x.ImageUrl,
                    IsActive = x.IsActive
                })
                .ToListAsync();

            return Ok(items);
        }

        // =========================
        // CREATE
        // =========================
        [HttpPost]
        [Authorize(Roles = "Manager")]
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
                !x.IsDeleted
            );

            if (!categoryExists)
                return BadRequest("Category not found");

            var duplicate = await _db.MenuItems.AnyAsync(x =>
                x.Name == request.Name &&
                !x.IsDeleted
            );

            if (duplicate)
                return BadRequest("Menu item name already exists");

            var urls = (request.ImageUrls ?? new List<string>())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Trim())
                .ToList();

            if (urls.Count == 0 && !string.IsNullOrWhiteSpace(request.ImageUrl))
                urls.Add(request.ImageUrl.Trim());

            var item = new MenuItem
            {
                Id = Guid.NewGuid(),
                CategoryId = request.CategoryId,
                Name = request.Name,
                Price = request.Price,
                InventoryQuantity = request.InventoryQuantity,
                ImageUrl = urls.FirstOrDefault(),
                IsActive = request.IsActive,
                IsDeleted = false
            };

            _db.MenuItems.Add(item);

            if (urls.Count > 0)
            {
                for (var i = 0; i < urls.Count; i++)
                {
                    _db.MenuItemImages.Add(new MenuItemImage
                    {
                        Id = Guid.NewGuid(),
                        MenuItemId = item.Id,
                        Url = urls[i],
                        SortOrder = i,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            await _db.SaveChangesAsync();

            await _hub.Clients
            .All
            .SendAsync("MenuItemCreated", new
            {
                id = item.Id,
                categoryId = item.CategoryId,
                name = item.Name,
                price = item.Price,
                inventoryQuantity = item.InventoryQuantity,
                imageUrl = item.ImageUrl,
                imageUrls = urls,
                isActive = item.IsActive
            });

            return Ok(item.Id);
        }

        // =========================
        // UPDATE (OPTIMISTIC LOCK)
        // =========================
        [HttpPut("{id}")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> Update(Guid id, UpdateMenuItemRequest request)
        {
            var item = await _db.MenuItems.FirstOrDefaultAsync(x =>
                x.Id == id &&
                !x.IsDeleted
            );

            if (item == null)
                return NotFound();

            var duplicate = await _db.MenuItems.AnyAsync(x =>
                x.Id != id &&
                x.Name == request.Name &&
                !x.IsDeleted
            );

            if (duplicate)
                return BadRequest("Menu item name already exists");

            var urls = (request.ImageUrls ?? new List<string>())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Trim())
                .ToList();

            if (urls.Count == 0 && !string.IsNullOrWhiteSpace(request.ImageUrl))
                urls.Add(request.ImageUrl.Trim());

            item.Name = request.Name;
            item.Price = request.Price;
            item.InventoryQuantity = request.InventoryQuantity;
            item.ImageUrl = urls.FirstOrDefault();
            item.IsActive = request.IsActive;

            // Replace images (simple approach)
            var existing = await _db.MenuItemImages.Where(x => x.MenuItemId == item.Id).ToListAsync();
            if (existing.Count > 0)
                _db.MenuItemImages.RemoveRange(existing);

            if (urls.Count > 0)
            {
                for (var i = 0; i < urls.Count; i++)
                {
                    _db.MenuItemImages.Add(new MenuItemImage
                    {
                        Id = Guid.NewGuid(),
                        MenuItemId = item.Id,
                        Url = urls[i],
                        SortOrder = i,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            await _db.SaveChangesAsync();

            await _hub.Clients
            .All
            .SendAsync("MenuItemUpdated", new
            {
                id = item.Id,
                name = item.Name,
                price = item.Price,
                inventoryQuantity = item.InventoryQuantity,
                imageUrl = item.ImageUrl,
                imageUrls = urls,
                isActive = item.IsActive
            });

            return Ok();
        }

        // =========================
        // DELETE (SOFT)
        // =========================
        [HttpDelete("{id}")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var item = await _db.MenuItems.FirstOrDefaultAsync(x =>
                x.Id == id &&
                !x.IsDeleted
            );

            if (item == null)
                return NotFound();

            item.IsDeleted = true;
            await _db.SaveChangesAsync();

            await _hub.Clients
            .All
            .SendAsync("MenuItemDeleted", new
            {
                id = item.Id
            });

            return Ok();
        }
    }
}

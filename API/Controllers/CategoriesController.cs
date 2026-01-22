using Application.DTOs.Categories;
using Core.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace API.Controllers
{
    [ApiController]
    [Route("api/categories")]
    [Authorize(Roles = "Manager")]
    public class CategoriesController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ICurrentCampusService _campus;

        public CategoriesController(
            AppDbContext db,
            ICurrentCampusService campus)
        {
            _db = db;
            _campus = campus;
        }

        // =========================
        // GET: LIST
        // =========================
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var categories = await _db.Categories
                .Where(x =>
                    x.CampusId == _campus.CampusId &&
                    !x.IsDeleted)
                .OrderBy(x => x.Name)
                .Select(x => new CategoryResponse
                {
                    Id = x.Id,
                    Name = x.Name,
                    IsActive = x.IsActive
                })
                .ToListAsync();

            return Ok(categories);
        }

        // =========================
        // GET: DETAIL
        // =========================
        [HttpGet("{id}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var category = await _db.Categories
                .Where(x =>
                    x.Id == id &&
                    x.CampusId == _campus.CampusId &&
                    !x.IsDeleted)
                .Select(x => new CategoryResponse
                {
                    Id = x.Id,
                    Name = x.Name,
                    IsActive = x.IsActive
                })
                .FirstOrDefaultAsync();

            if (category == null)
                return NotFound();

            return Ok(category);
        }

        // =========================
        // POST: CREATE
        // =========================
        [HttpPost]
        public async Task<IActionResult> Create(CreateCategoryRequest request)
        {
            var exists = await _db.Categories.AnyAsync(x =>
                x.CampusId == _campus.CampusId &&
                !x.IsDeleted &&
                x.Name == request.Name);

            if (exists)
                return BadRequest("Category name already exists");

            var category = new Category
            {
                Id = Guid.NewGuid(),
                CampusId = _campus.CampusId,
                Name = request.Name,
                IsActive = true,
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            };

            _db.Categories.Add(category);
            await _db.SaveChangesAsync();

            return Ok(category.Id);
        }

        // =========================
        // PUT: UPDATE
        // =========================
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(
            Guid id,
            UpdateCategoryRequest request)
        {
            var category = await _db.Categories.FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.CampusId == _campus.CampusId &&
                !x.IsDeleted);

            if (category == null)
                return NotFound();

            var duplicate = await _db.Categories.AnyAsync(x =>
                x.Id != id &&
                x.CampusId == _campus.CampusId &&
                !x.IsDeleted &&
                x.Name == request.Name);

            if (duplicate)
                return BadRequest("Category name already exists");

            category.Name = request.Name;
            category.IsActive = request.IsActive;

            await _db.SaveChangesAsync();
            return Ok();
        }

        // =========================
        // DELETE: SOFT DELETE
        // =========================
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var category = await _db.Categories.FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.CampusId == _campus.CampusId &&
                !x.IsDeleted);

            if (category == null)
                return NotFound();

            var hasItems = await _db.MenuItems.AnyAsync(x =>
                x.CategoryId == id &&
                !x.IsDeleted);

            if (hasItems)
                return BadRequest("Cannot delete category with menu items");

            category.IsDeleted = true;

            await _db.SaveChangesAsync();
            return Ok();
        }
    }
}

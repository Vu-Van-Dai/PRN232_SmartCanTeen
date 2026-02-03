using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Core.Entities;

namespace API.Controllers;

[ApiController]
[Route("api/manager/screens")]
[Authorize(Roles = "Manager")]
public class ManagerScreensController : ControllerBase
{
    private readonly AppDbContext _db;

    public ManagerScreensController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var screens = await _db.Set<DisplayScreen>()
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                id = x.Id,
                key = x.Key,
                name = x.Name,
                isActive = x.IsActive,
                categoryIds = x.ScreenCategories.Select(sc => sc.CategoryId).ToList()
            })
            .ToListAsync();

        return Ok(new { items = screens });
    }

    public record UpsertScreenRequest(string Key, string Name, bool IsActive, List<Guid> CategoryIds);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UpsertScreenRequest req)
    {
        var key = (req.Key ?? "").Trim();
        if (key.Length == 0) return BadRequest("Key is required");

        var exists = await _db.Set<DisplayScreen>().AnyAsync(x => x.Key == key);
        if (exists) return BadRequest("Key already exists");

        var screen = new DisplayScreen
        {
            Id = Guid.NewGuid(),
            Key = key,
            Name = (req.Name ?? "").Trim(),
            IsActive = req.IsActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false,
        };

        _db.Add(screen);

        var uniqueCategoryIds = (req.CategoryIds ?? new List<Guid>()).Distinct().ToList();
        foreach (var catId in uniqueCategoryIds)
        {
            _db.Add(new DisplayScreenCategory { ScreenId = screen.Id, CategoryId = catId });
        }

        await _db.SaveChangesAsync();

        return Ok(new { id = screen.Id });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpsertScreenRequest req)
    {
        var screen = await _db.Set<DisplayScreen>()
            .Include(x => x.ScreenCategories)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (screen == null) return NotFound();

        var key = (req.Key ?? "").Trim();
        if (key.Length == 0) return BadRequest("Key is required");

        var keyUsedByOther = await _db.Set<DisplayScreen>().AnyAsync(x => x.Key == key && x.Id != id);
        if (keyUsedByOther) return BadRequest("Key already exists");

        screen.Key = key;
        screen.Name = (req.Name ?? "").Trim();
        screen.IsActive = req.IsActive;
        screen.UpdatedAt = DateTime.UtcNow;

        var desired = (req.CategoryIds ?? new List<Guid>()).Distinct().ToHashSet();
        var current = screen.ScreenCategories.Select(sc => sc.CategoryId).ToHashSet();

        // remove
        var toRemove = screen.ScreenCategories.Where(sc => !desired.Contains(sc.CategoryId)).ToList();
        if (toRemove.Count > 0) _db.RemoveRange(toRemove);

        // add
        var toAdd = desired.Except(current).ToList();
        foreach (var catId in toAdd)
        {
            _db.Add(new DisplayScreenCategory { ScreenId = screen.Id, CategoryId = catId });
        }

        await _db.SaveChangesAsync();

        return Ok();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var screen = await _db.Set<DisplayScreen>().FirstOrDefaultAsync(x => x.Id == id);
        if (screen == null) return NotFound();

        screen.IsDeleted = true;
        screen.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok();
    }
}

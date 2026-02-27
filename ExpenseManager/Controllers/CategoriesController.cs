using ExpenseManager.Data;
using ExpenseManager.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManager.Controllers;

[Authorize]
public class CategoriesController(ApplicationDbContext dbContext) : Controller
{
    public async Task<IActionResult> Index()
    {
        var categories = await dbContext.Categories
            .OrderBy(c => c.Type)
            .ThenBy(c => c.Name)
            .ToListAsync();
        return View(categories);
    }

    public IActionResult Create()
    {
        return View(new Category());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Category model)
    {
        model.Name = model.Name.Trim();

        var exists = await dbContext.Categories.AnyAsync(c =>
            c.Type == model.Type &&
            c.Name.ToLower() == model.Name.ToLower());
        if (exists)
        {
            ModelState.AddModelError(nameof(model.Name), "Category already exists for this type.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        model.IsSystem = true;
        dbContext.Categories.Add(model);
        await dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(Guid id)
    {
        var category = await dbContext.Categories.SingleOrDefaultAsync(c => c.Id == id);
        if (category is null)
        {
            return NotFound();
        }

        return View(category);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, Category input)
    {
        var category = await dbContext.Categories.SingleOrDefaultAsync(c => c.Id == id);
        if (category is null)
        {
            return NotFound();
        }

        input.Name = input.Name.Trim();
        var exists = await dbContext.Categories.AnyAsync(c =>
            c.Id != id &&
            c.Type == input.Type &&
            c.Name.ToLower() == input.Name.ToLower());
        if (exists)
        {
            ModelState.AddModelError(nameof(input.Name), "Category already exists for this type.");
        }

        if (!ModelState.IsValid)
        {
            return View(input);
        }

        category.Name = input.Name;
        category.Type = input.Type;
        await dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var category = await dbContext.Categories.SingleOrDefaultAsync(c => c.Id == id);
        if (category is null)
        {
            return NotFound();
        }

        var isUsed = await dbContext.Transactions.AnyAsync(t => t.CategoryId == id);
        if (isUsed)
        {
            TempData["Error"] = "Cannot delete category because it is used by transactions.";
            return RedirectToAction(nameof(Index));
        }

        dbContext.Categories.Remove(category);
        await dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}

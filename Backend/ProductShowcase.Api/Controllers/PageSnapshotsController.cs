using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductShowcase.Api.Data;
using ProductShowcase.Api.DTOs;
using ProductShowcase.Api.Entities;

namespace ProductShowcase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class PageSnapshotsController(
    ApplicationDbContext dbContext,
    ILogger<PageSnapshotsController> logger) : ControllerBase
{
    private const int MaxSnapshotsPerPage = 20;

    /// <summary>
    /// Sürüm geçmişini listeler:
    /// - pageKey verilirse ("Şu Anki" filtresi): yalnızca o sayfanın son 20 sürümü.
    /// - pageKey boşsa ("Tümü" filtresi): tüm sayfalardaki son 40 kayıt tarih/saat sırasıyla.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PageSnapshotSummaryDto>>> GetSnapshots(
        [FromQuery] string? pageKey = null,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, 60);
        var query = dbContext.PageSnapshots.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(pageKey))
        {
            var normalized = pageKey.Trim().ToLowerInvariant();
            query = query.Where(s => s.PageKey == normalized);
        }

        var list = await query
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Take(take)
            .Select(s => new PageSnapshotSummaryDto(
                s.Id,
                s.PageKey,
                s.PageTitle,
                s.CategoryId,
                s.CategorySlug,
                s.IsPublished,
                s.Summary,
                s.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(list);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PageSnapshotDetailDto>> GetSnapshotById(
        int id,
        CancellationToken cancellationToken)
    {
        var snap = await dbContext.PageSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (snap is null)
        {
            throw new KeyNotFoundException($"ID={id} olan sürüm kaydı bulunamadı.");
        }

        return Ok(new PageSnapshotDetailDto(
            snap.Id,
            snap.PageKey,
            snap.PageTitle,
            snap.CategoryId,
            snap.CategorySlug,
            snap.IsPublished,
            snap.SnapshotJson,
            snap.Summary,
            snap.CreatedAt));
    }

    [HttpPost]
    public async Task<ActionResult<PageSnapshotSummaryDto>> CreateSnapshot(
        [FromBody] CreatePageSnapshotRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedPageKey = request.PageKey.Trim().ToLowerInvariant();

        var snapshot = new PageSnapshot
        {
            PageKey = normalizedPageKey,
            PageTitle = request.PageTitle.Trim(),
            CategoryId = request.CategoryId,
            CategorySlug = string.IsNullOrWhiteSpace(request.CategorySlug) ? null : request.CategorySlug.Trim().ToLowerInvariant(),
            IsPublished = request.IsPublished,
            SnapshotJson = request.SnapshotJson,
            Summary = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        dbContext.PageSnapshots.Add(snapshot);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Sayfa başına yalnızca son 20 sürümü tut
        var excessSnapshots = await dbContext.PageSnapshots
            .Where(s => s.PageKey == normalizedPageKey)
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Skip(MaxSnapshotsPerPage)
            .ToListAsync(cancellationToken);

        if (excessSnapshots.Count > 0)
        {
            dbContext.PageSnapshots.RemoveRange(excessSnapshots);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Sürüm yedeği kaydedildi: {PageTitle} ({PageKey}) -> Snapshot ID={Id}",
            snapshot.PageTitle, snapshot.PageKey, snapshot.Id);

        return CreatedAtAction(nameof(GetSnapshotById), new { id = snapshot.Id }, new PageSnapshotSummaryDto(
            snapshot.Id,
            snapshot.PageKey,
            snapshot.PageTitle,
            snapshot.CategoryId,
            snapshot.CategorySlug,
            snapshot.IsPublished,
            snapshot.Summary,
            snapshot.CreatedAt));
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductShowcase.Api.Data;
using ProductShowcase.Api.DTOs;
using ProductShowcase.Api.Entities;
using ProductShowcase.Api.Services;

namespace ProductShowcase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SiteSettingsController(
    ApplicationDbContext dbContext,
    IFileStorageService fileStorageService) : ControllerBase
{
    private const string DefaultKey = "default";

    [HttpGet]
    public async Task<ActionResult<SiteSettingDto>> GetSettings(CancellationToken cancellationToken)
    {
        var setting = await dbContext.SiteSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == DefaultKey, cancellationToken);

        if (setting is null)
        {
            return Ok(new SiteSettingDto(
                LogoUrl: "images/LOGO.png",
                LogoZoomPercent: 100,
                HeaderBannerText: null,
                FooterText: "Yenilikçi dijital çözümlerle geleceği inşa ediyoruz.\nSizlerle birlikte büyümeye ve üretmeye devam ediyoruz.",
                HomeIsPublished: true,
                UpdatedAt: DateTime.UtcNow));
        }

        return Ok(MapToDto(setting));
    }

    [HttpPut]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<SiteSettingDto>> UpdateSettings(
        [FromForm] UpdateSiteSettingRequest request,
        CancellationToken cancellationToken)
    {
        var setting = await dbContext.SiteSettings
            .FirstOrDefaultAsync(s => s.Key == DefaultKey, cancellationToken);

        if (setting is null)
        {
            setting = new SiteSetting
            {
                Key = DefaultKey,
                LogoUrl = "images/LOGO.png",
                LogoZoomPercent = 100,
                HomeIsPublished = true
            };
            dbContext.SiteSettings.Add(setting);
        }

        string? oldUploadedLogo = null;
        if (request.LogoFile is not null && request.LogoFile.Length > 0)
        {
            var (newLogoUrl, _) = await fileStorageService.SaveMediaFileAsync(request.LogoFile, cancellationToken);
            if (!string.IsNullOrWhiteSpace(setting.LogoUrl) &&
                (setting.LogoUrl.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) ||
                 setting.LogoUrl.Contains("res.cloudinary.com/", StringComparison.OrdinalIgnoreCase)))
            {
                oldUploadedLogo = setting.LogoUrl;
            }
            setting.LogoUrl = newLogoUrl;
        }
        else if (request.LogoUrl is not null)
        {
            setting.LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim();
        }

        if (request.LogoZoomPercent.HasValue)
        {
            if (request.LogoZoomPercent.Value < 20 || request.LogoZoomPercent.Value > 200)
            {
                throw new ArgumentException("Logo boyutu %20 ile %200 arasında olmalıdır.");
            }
            setting.LogoZoomPercent = request.LogoZoomPercent.Value;
        }

        // ASP.NET Core [FromForm] boş string'i null'a çevirebildiği için form alanının gönderilip gönderilmediğini doğrudan Request.Form ile kontrol et
        if (Request.HasFormContentType && Request.Form.ContainsKey("headerBannerText"))
        {
            var rawBanner = Request.Form["headerBannerText"].ToString();
            setting.HeaderBannerText = string.IsNullOrWhiteSpace(rawBanner) ? null : rawBanner.Trim();
        }
        else if (request.HeaderBannerText is not null)
        {
            setting.HeaderBannerText = string.IsNullOrWhiteSpace(request.HeaderBannerText) ? null : request.HeaderBannerText.Trim();
        }

        if (Request.HasFormContentType && Request.Form.ContainsKey("footerText"))
        {
            var rawFooter = Request.Form["footerText"].ToString();
            setting.FooterText = string.IsNullOrWhiteSpace(rawFooter) ? null : rawFooter.Trim();
        }
        else if (request.FooterText is not null)
        {
            setting.FooterText = string.IsNullOrWhiteSpace(request.FooterText) ? null : request.FooterText.Trim();
        }

        if (request.HomeIsPublished.HasValue)
        {
            setting.HomeIsPublished = request.HomeIsPublished.Value;
        }

        setting.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        if (oldUploadedLogo is not null)
        {
            fileStorageService.DeleteFileIfExists(oldUploadedLogo);
        }

        return Ok(MapToDto(setting));
    }

    private static SiteSettingDto MapToDto(SiteSetting s) => new(
        s.LogoUrl,
        Math.Clamp(s.LogoZoomPercent == 0 ? 100 : s.LogoZoomPercent, 20, 200),
        s.HeaderBannerText,
        s.FooterText,
        s.HomeIsPublished,
        s.UpdatedAt);
}

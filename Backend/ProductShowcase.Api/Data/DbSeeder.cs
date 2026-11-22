using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProductShowcase.Api.Entities;
using ProductShowcase.Api.Services;

namespace ProductShowcase.Api.Data;

public static class DbSeeder
{
    private static readonly string[] FakeLegacyCategorySlugs =
    [
        "mds-pick",
        "yeni-gelenler",
        "dis-giyim",
        "aksesuar"
    ];

    private sealed record SeedSubGroup(string Name, string Slug, (string Name, string Slug)[] Items);
    private sealed record SeedMainGroup(string Name, string Slug, SeedSubGroup[] SubGroups);

    // Başlangıç verisi olarak orijinal Mega Menü (ustAltVerileri.megaMenu) 3 seviyeli yapısı birebir MySQL'e aktarılır.
    // Bundan sonra tek gerçek veri kaynağı (Single Source of Truth) MySQL veritabanıdır.
    private static readonly SeedMainGroup[] InitialMegaMenuHierarchy =
    [
        new("EV VE YAŞAM", "ev-ve-yasam",
        [
            new("YATAK VE SALON", "yatak-ve-salon",
            [
                ("Yatak Örtüsü", "yatak-ortusu"),
                ("TV Battaniyesi", "tv-battaniyesi"),
                ("Yastık Kılıfı", "yastik-kilifi")
            ]),
            new("MUTFAK VE BANYO", "mutfak-ve-banyo",
            [
                ("Runner", "runner"),
                ("Lif ve Sabunluk", "lif-ve-sabunluk")
            ])
        ]),
        new("GİYİM VE ÇANTA", "giyim-ve-canta",
        [
            new("YETİŞKİN ÜRÜNLERİ", "yetiskin-urunleri",
            [
                ("Yelek ve Hırka", "yelek-ve-hirka"),
                ("Çanta", "canta")
            ]),
            new("BEBEK ÜRÜNLERİ", "bebek-urunleri",
            [
                ("Bebek Yeleği", "bebek-yelegi"),
                ("Oyuncak Bebek", "oyuncak-bebek"),
                ("Bebek Battaniyesi", "bebek-battaniyesi")
            ])
        ]),
        new("AKSESUAR ÜRÜNLERİ", "aksesuar-urunleri",
        [
            new("KAFA AKSESUARLARI", "kafa-aksesuarlari",
            [
                ("Bandana", "bandana"),
                ("Bere", "bere"),
                ("Boyun Yastığı", "boyun-yastigi")
            ]),
            new("GENEL AKSESUARLAR", "genel-aksesuarlar",
            [
                ("Patik", "patik"),
                ("Bardak Altlığı", "bardak-altligi"),
                ("Seccade", "seccade"),
                ("İğnelik", "ignelik"),
                ("Paspas", "paspas"),
                ("Kulaklık Kılıfı", "kulaklik-kilifi")
            ])
        ])
    ];

    public static async Task SeedAsync(ApplicationDbContext db, ILogger logger, IConfiguration configuration)
    {
        // 1) İlk kurulumda başlangıç Admin (ve isteğe bağlı Demo) kullanıcılarını yapılandırmadan (appsettings.Local.json / ENV) oluştur
        if (!await db.Users.AnyAsync())
        {
            var adminEmail = configuration["Seed:AdminEmail"]?.Trim().ToLowerInvariant();
            var adminPassword = configuration["Seed:AdminPassword"];
            var adminFullName = configuration["Seed:AdminFullName"]?.Trim();
            if (string.IsNullOrWhiteSpace(adminFullName)) adminFullName = "Site Yöneticisi";

            static bool IsPlaceholder(string? val) =>
                string.IsNullOrWhiteSpace(val) ||
                val.StartsWith("REPLACE_WITH_", StringComparison.OrdinalIgnoreCase) ||
                val.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase);

            if (IsPlaceholder(adminEmail) || IsPlaceholder(adminPassword))
            {
                logger.LogWarning(
                    "Başlangıç Admin hesabı oluşturulmadı: 'Seed:AdminEmail' ve 'Seed:AdminPassword' yapılandırması tanımlı değil. " +
                    "Güvenli ilk admin oluşturmak için appsettings.Local.json dosyasında veya ortam değişkenlerinde (Seed__AdminEmail, Seed__AdminPassword) tanımlama yapın.");
            }
            else
            {
                var hasher = new PasswordHasher<User>();
                var usersToCreate = new List<User>();

                var admin = new User
                {
                    FullName = adminFullName,
                    Email = adminEmail!,
                    Role = "Admin"
                };
                admin.PasswordHash = hasher.HashPassword(admin, adminPassword!);
                usersToCreate.Add(admin);

                var demoEmail = configuration["Seed:DemoEmail"]?.Trim().ToLowerInvariant();
                var demoPassword = configuration["Seed:DemoPassword"];
                var demoFullName = configuration["Seed:DemoFullName"]?.Trim();
                if (string.IsNullOrWhiteSpace(demoFullName)) demoFullName = "Demo Kullanıcı";

                if (!IsPlaceholder(demoEmail) && !IsPlaceholder(demoPassword) &&
                    !string.Equals(demoEmail, adminEmail, StringComparison.OrdinalIgnoreCase))
                {
                    var demoUser = new User
                    {
                        FullName = demoFullName,
                        Email = demoEmail!,
                        Role = "User"
                    };
                    demoUser.PasswordHash = hasher.HashPassword(demoUser, demoPassword!);
                    usersToCreate.Add(demoUser);
                }

                await db.Users.AddRangeAsync(usersToCreate);
                await db.SaveChangesAsync();
                logger.LogInformation("Yapılandırmadan başlangıç kullanıcıları oluşturuldu (Admin: {AdminEmail}, Toplam: {Count}).",
                    admin.Email, usersToCreate.Count);
            }
        }

        // 2) İstenmeyen eski sahte varsayılan kategorileri (MD'S PICK, Yeni Gelenler, Dış Giyim, Aksesuar) temizle
        var legacyCategories = await db.Categories
            .Include(c => c.Products)
            .Include(c => c.PageSections)
            .Where(c => FakeLegacyCategorySlugs.Contains(c.Slug))
            .ToListAsync();

        if (legacyCategories.Count > 0)
        {
            foreach (var leg in legacyCategories)
            {
                if (leg.Products.Count > 0)
                {
                    db.Products.RemoveRange(leg.Products);
                }
                if (leg.PageSections.Count > 0)
                {
                    db.PageSections.RemoveRange(leg.PageSections);
                }
            }
            db.Categories.RemoveRange(legacyCategories);
            await db.SaveChangesAsync();
            logger.LogInformation("Eski sahte varsayılan kategoriler temizlendi: {Count} adet", legacyCategories.Count);
        }

        // 3) İlk kurulumda Level 1 (Ana Başlık) hiç yoksa gerçek 3 seviyeli Mega Menü ağacını MySQL'e aktar
        var hasHierarchy = await db.Categories.AnyAsync(c => c.Level == 1);
        if (!hasHierarchy)
        {
            var existingBySlug = await db.Categories.ToDictionaryAsync(c => c.Slug.ToLowerInvariant());

            int mainOrder = 10;
            foreach (var main in InitialMegaMenuHierarchy)
            {
                var mainCat = new Category
                {
                    Name = main.Name,
                    Slug = main.Slug,
                    Description = $"{main.Name} ana başlığı",
                    Level = 1,
                    ParentId = null,
                    DisplayOrder = mainOrder,
                    IsPublished = true,
                    TemplateInitialized = true,
                    CreatedAt = DateTime.UtcNow
                };
                db.Categories.Add(mainCat);
                await db.SaveChangesAsync();
                mainOrder += 10;

                int subOrder = 10;
                foreach (var sub in main.SubGroups)
                {
                    var subCat = new Category
                    {
                        Name = sub.Name,
                        Slug = sub.Slug,
                        Description = $"{sub.Name} alt başlığı",
                        Level = 2,
                        ParentId = mainCat.Id,
                        DisplayOrder = subOrder,
                        IsPublished = true,
                        TemplateInitialized = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    db.Categories.Add(subCat);
                    await db.SaveChangesAsync();
                    subOrder += 10;

                    int itemOrder = 10;
                    foreach (var (itemName, itemSlug) in sub.Items)
                    {
                        if (existingBySlug.TryGetValue(itemSlug, out var existingLeaf))
                        {
                            existingLeaf.Name = itemName;
                            existingLeaf.Level = 3;
                            existingLeaf.ParentId = subCat.Id;
                            existingLeaf.DisplayOrder = itemOrder;
                        }
                        else
                        {
                            db.Categories.Add(new Category
                            {
                                Name = itemName,
                                Slug = itemSlug,
                                Description = $"{itemName} kategorisi",
                                Level = 3,
                                ParentId = subCat.Id,
                                DisplayOrder = itemOrder,
                                IsPublished = true,
                                TemplateInitialized = false,
                                CreatedAt = DateTime.UtcNow
                            });
                        }
                        itemOrder += 10;
                    }
                    await db.SaveChangesAsync();
                }
            }

            logger.LogInformation("Orijinal 3 seviyeli Mega Menü hiyerarşisi MySQL veritabanına aktarıldı.");
        }

        // 4) Mevcut boş Video bölümlerine 3 sekmeli varsayılan (Instagram, TikTok, YouTube) yapıyı ekle
        var emptyVideoSections = await db.PageSections
            .Where(s => s.TemplateType == "video" && (s.SlotsJson == null || s.SlotsJson == "" || s.SlotsJson == "[]"))
            .ToListAsync();

        if (emptyVideoSections.Count > 0)
        {
            var defaultJson = CategoryTemplateService.BuildDefaultVideoCarouselSlotsJson();
            foreach (var vs in emptyVideoSections)
            {
                vs.SlotsJson = defaultJson;
                if (string.IsNullOrWhiteSpace(vs.Title))
                {
                    vs.Title = "Şu An Trend Olanlar";
                }
            }
            await db.SaveChangesAsync();
        }

        // 5) Eski ek_1.html?kategori=... PageKey kayıtlarını tek çatı index.html?kategori=... yapısına güvenle geçir
        var legacySections = await db.PageSections
            .Where(s => s.PageKey.StartsWith("ek_1.html?kategori="))
            .ToListAsync();

        if (legacySections.Count > 0)
        {
            var existingKeys = await db.PageSections
                .AsNoTracking()
                .Where(s => s.PageKey.StartsWith("index.html?kategori="))
                .Select(s => new { s.PageKey, s.SectionKey })
                .ToListAsync();
            var existingSet = new HashSet<string>(
                existingKeys.Select(x => $"{x.PageKey}::{x.SectionKey}"),
                StringComparer.OrdinalIgnoreCase);

            foreach (var sec in legacySections)
            {
                var nextKey = "index.html?" + sec.PageKey["ek_1.html?".Length..];
                if (existingSet.Contains($"{nextKey}::{sec.SectionKey}"))
                {
                    db.PageSections.Remove(sec);
                }
                else
                {
                    sec.PageKey = nextKey;
                    existingSet.Add($"{nextKey}::{sec.SectionKey}");
                }
            }
            await db.SaveChangesAsync();
            logger.LogInformation("Eski ek_1.html PageSections kayıtları index.html formatına geçirildi: {Count} adet", legacySections.Count);
        }

        var legacySnapshots = await db.PageSnapshots
            .Where(s => s.PageKey.StartsWith("ek_1.html?kategori="))
            .ToListAsync();

        if (legacySnapshots.Count > 0)
        {
            foreach (var snap in legacySnapshots)
            {
                snap.PageKey = "index.html?" + snap.PageKey["ek_1.html?".Length..];
            }
            await db.SaveChangesAsync();
            logger.LogInformation("Eski ek_1.html PageSnapshots kayıtları index.html formatına geçirildi: {Count} adet", legacySnapshots.Count);
        }
    }
}

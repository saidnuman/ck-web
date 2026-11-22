using Microsoft.EntityFrameworkCore;
using ProductShowcase.Api.Entities;

namespace ProductShowcase.Api.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<User> Users => Set<User>();
    public DbSet<FavoriteProduct> FavoriteProducts => Set<FavoriteProduct>();
    public DbSet<PageSection> PageSections => Set<PageSection>();
    public DbSet<PageSnapshot> PageSnapshots => Set<PageSnapshot>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();
    public DbSet<PasswordResetCode> PasswordResetCodes => Set<PasswordResetCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Category yapılandırması (3 Seviyeli Hiyerarşik Ağaç: Ana Başlık -> Alt Başlık -> Kategori)
        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Name).IsRequired().HasMaxLength(100);
            entity.Property(c => c.Slug).IsRequired().HasMaxLength(120);
            entity.Property(c => c.Level).HasDefaultValue(3);
            entity.Property(c => c.IsDeleted).HasDefaultValue(false);
            entity.Property(c => c.DeletedFromPath).IsRequired(false).HasMaxLength(300);
            entity.HasIndex(c => c.Slug).IsUnique();
            entity.HasIndex(c => new { c.Level, c.ParentId, c.DisplayOrder });
            entity.HasIndex(c => c.IsDeleted);

            entity.HasOne(c => c.Parent)
                .WithMany(c => c.Children)
                .HasForeignKey(c => c.ParentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Product yapılandırması (One-to-Many -> Category)
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Brand).IsRequired().HasMaxLength(100);
            entity.Property(p => p.Title).IsRequired().HasMaxLength(200);
            entity.Property(p => p.Price).HasPrecision(18, 2);
            entity.Property(p => p.ImageUrl).IsRequired().HasMaxLength(500);
            entity.Property(p => p.SectionKey).HasMaxLength(60).HasDefaultValue("bolum-d");

            entity.HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // User yapılandırması
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.FullName).IsRequired().HasMaxLength(120);
            entity.Property(u => u.Email).IsRequired().HasMaxLength(200);
            entity.Property(u => u.PasswordHash).IsRequired().HasMaxLength(500);
            entity.Property(u => u.Role).IsRequired().HasMaxLength(30).HasDefaultValue("User");
            entity.HasIndex(u => u.Email).IsUnique();
        });

        // FavoriteProduct yapılandırması (Many-to-Many: User <-> Product)
        modelBuilder.Entity<FavoriteProduct>(entity =>
        {
            entity.ToTable("FavoriteProducts");
            entity.HasKey(fp => new { fp.UserId, fp.ProductId });

            entity.HasOne(fp => fp.User)
                .WithMany(u => u.FavoriteProducts)
                .HasForeignKey(fp => fp.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(fp => fp.Product)
                .WithMany(p => p.FavoriteProducts)
                .HasForeignKey(fp => fp.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // PageSection yapılandırması (Kategori bazlı izole dinamik sayfa bölümleri — NULL destekli)
        modelBuilder.Entity<PageSection>(entity =>
        {
            entity.ToTable("PageSections");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.PageKey).IsRequired().HasMaxLength(120);
            entity.Property(s => s.CategorySlug).IsRequired(false).HasMaxLength(120);
            entity.Property(s => s.SectionKey).IsRequired().HasMaxLength(100);
            entity.Property(s => s.TemplateType).IsRequired().HasMaxLength(40);
            entity.Property(s => s.Title).IsRequired(false).HasMaxLength(200);
            entity.Property(s => s.Price).HasPrecision(18, 2);
            entity.Property(s => s.SlotsJson).HasColumnType("longtext");
            entity.HasIndex(s => new { s.PageKey, s.SectionKey }).IsUnique();
            entity.HasIndex(s => s.CategorySlug);

            entity.HasOne(s => s.Category)
                .WithMany(c => c.PageSections)
                .HasForeignKey(s => s.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // PageSnapshot yapılandırması (Yayın Sonrası Sürüm Geçmişi)
        modelBuilder.Entity<PageSnapshot>(entity =>
        {
            entity.ToTable("PageSnapshots");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.PageKey).IsRequired().HasMaxLength(120);
            entity.Property(s => s.PageTitle).IsRequired().HasMaxLength(150);
            entity.Property(s => s.CategorySlug).IsRequired(false).HasMaxLength(120);
            entity.Property(s => s.SnapshotJson).IsRequired().HasColumnType("longtext");
            entity.HasIndex(s => new { s.PageKey, s.CreatedAt });
            entity.HasIndex(s => s.CreatedAt);
        });

        // SiteSetting yapılandırması (Logo, Logo Boyutu, Üst Menü, Footer ve Ana Sayfa Yayın Durumu)
        modelBuilder.Entity<SiteSetting>(entity =>
        {
            entity.ToTable("SiteSettings");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Key).IsRequired().HasMaxLength(50);
            entity.Property(s => s.LogoZoomPercent).HasDefaultValue(100);
            entity.HasIndex(s => s.Key).IsUnique();
        });

        // PasswordResetCode yapılandırması (Şifremi Unuttum 6 haneli tek kullanımlık doğrulama kodları)
        modelBuilder.Entity<PasswordResetCode>(entity =>
        {
            entity.ToTable("PasswordResetCodes");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Email).IsRequired().HasMaxLength(200);
            entity.Property(r => r.CodeHash).IsRequired().HasMaxLength(128);
            entity.HasIndex(r => new { r.Email, r.IsUsed, r.ExpiresAt });

            entity.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

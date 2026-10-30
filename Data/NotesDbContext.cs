using ABAPKnowledgeBase.Models;
using Microsoft.EntityFrameworkCore;
namespace ABAPKnowledgeBase.Data;
public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options):DbContext(options) {
 public DbSet<Note> Notes => Set<Note>();
 public DbSet<Category> Categories => Set<Category>();
 public DbSet<NoteImage> NoteImages => Set<NoteImage>();
 public DbSet<Company> Companies => Set<Company>();
 protected override void OnModelCreating(ModelBuilder modelBuilder) {
   modelBuilder.Entity<Note>().HasOne(x=>x.Category).WithMany(x=>x.Notes).HasForeignKey(x=>x.CategoryId).OnDelete(DeleteBehavior.Restrict);
   modelBuilder.Entity<Note>().HasIndex(x=>x.Title);
   modelBuilder.Entity<Note>().HasIndex(x=>new{x.CategoryId,x.SortOrder});
   // Not silinince resimleri de silinir.
   modelBuilder.Entity<NoteImage>().HasOne(x=>x.Note).WithMany(x=>x.Images).HasForeignKey(x=>x.NoteId).OnDelete(DeleteBehavior.Cascade);
   modelBuilder.Entity<NoteImage>().HasIndex(x=>new{x.NoteId,x.Order}).IsUnique();
   // Firması olan not varken firma silinemez (önce notlar taşınmalı/silinmeli).
   modelBuilder.Entity<Note>().HasOne(x=>x.Company).WithMany(x=>x.Notes).HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict);
   modelBuilder.Entity<Company>().HasIndex(x=>x.Name).IsUnique();
 }
}
public static class CategorySeed {
 // Firmalara ayrılabilen tek kategori.
 public const string Projects = "Projeler";
 public static readonly string[] All = [
 "ABAP Temelleri", "Internal Table ve Modern ABAP", "SQL, CDS ve Dictionary",
 "Nesne Yönelimli ABAP", "ALV ve Ekran İşlemleri", "Fonksiyonlar, BAPI ve Entegrasyon",
 "Forms ve Yazdırma", "Excel, Dosya ve Mail", "Debug ve Hatalar", "SAP Araçları ve Transport",
 Projects ];
}

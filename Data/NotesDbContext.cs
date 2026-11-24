using ABAPKnowledgeBase.Models;
using Microsoft.EntityFrameworkCore;
namespace ABAPKnowledgeBase.Data;
public sealed class NotesDbContext(DbContextOptions<NotesDbContext> options):DbContext(options) {
 public DbSet<Note> Notes => Set<Note>();
 public DbSet<Category> Categories => Set<Category>();
 public DbSet<NoteImage> NoteImages => Set<NoteImage>();
 public DbSet<Company> Companies => Set<Company>();
 public DbSet<Subcategory> Subcategories => Set<Subcategory>();
 public DbSet<Tag> Tags => Set<Tag>();
 public DbSet<NoteTag> NoteTags => Set<NoteTag>();
 public DbSet<Source> Sources => Set<Source>();
 public DbSet<QuizDay> QuizDays => Set<QuizDay>();
 public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
 public DbSet<NoteReview> NoteReviews => Set<NoteReview>();
 public DbSet<StickyNote> StickyNotes => Set<StickyNote>();
 public DbSet<StickyImage> StickyImages => Set<StickyImage>();
 protected override void OnModelCreating(ModelBuilder modelBuilder) {
   // Aramada Türkçe uyumlu, büyük/küçük harf duyarsız "içinde geçiyor mu" (bkz. SqliteSetup.cs).
   modelBuilder.HasDbFunction(typeof(TextMatch).GetMethod(nameof(TextMatch.Contains))!).HasName("tr_contains");
   modelBuilder.Entity<Note>().HasOne(x=>x.Category).WithMany(x=>x.Notes).HasForeignKey(x=>x.CategoryId).OnDelete(DeleteBehavior.Restrict);
   modelBuilder.Entity<Note>().HasIndex(x=>x.Title);
   modelBuilder.Entity<Note>().HasIndex(x=>new{x.CategoryId,x.SortOrder});
   // Not silinince resimleri de silinir.
   modelBuilder.Entity<NoteImage>().HasOne(x=>x.Note).WithMany(x=>x.Images).HasForeignKey(x=>x.NoteId).OnDelete(DeleteBehavior.Cascade);
   modelBuilder.Entity<NoteImage>().HasIndex(x=>new{x.NoteId,x.Order}).IsUnique();
   // Firması olan not varken firma silinemez (önce notlar taşınmalı/silinmeli).
   modelBuilder.Entity<Note>().HasOne(x=>x.Company).WithMany(x=>x.Notes).HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Restrict);
   modelBuilder.Entity<Company>().HasIndex(x=>x.Name).IsUnique();
   // Alt kategori: kategori silinemez (Restrict) ama alt kategori silinince notları Genel'e düşer (SetNull).
   modelBuilder.Entity<Subcategory>().HasOne(x=>x.Category).WithMany(x=>x.Subcategories).HasForeignKey(x=>x.CategoryId).OnDelete(DeleteBehavior.Cascade);
   modelBuilder.Entity<Subcategory>().HasIndex(x=>new{x.CategoryId,x.Name}).IsUnique();
   modelBuilder.Entity<Note>().HasOne(x=>x.Subcategory).WithMany(x=>x.Notes).HasForeignKey(x=>x.SubcategoryId).OnDelete(DeleteBehavior.SetNull);
   // Etiketler: not silinince bağlantıları da silinir; etiketin kendisi başka notlarda kullanılabilir.
   modelBuilder.Entity<Tag>().HasIndex(x=>x.Name).IsUnique();
   modelBuilder.Entity<NoteTag>().HasKey(x=>new{x.NoteId,x.TagId});
   modelBuilder.Entity<NoteTag>().HasOne(x=>x.Note).WithMany(x=>x.NoteTags).HasForeignKey(x=>x.NoteId).OnDelete(DeleteBehavior.Cascade);
   modelBuilder.Entity<NoteTag>().HasOne(x=>x.Tag).WithMany(x=>x.NoteTags).HasForeignKey(x=>x.TagId).OnDelete(DeleteBehavior.Cascade);
   // Kaynak: aynı defter/bölüm veya dosya bir kez tutulur; kaynağı silinen notun kaynak bilgisi boşalır.
   modelBuilder.Entity<Source>().HasIndex(x=>new{x.Kind,x.Name,x.Section}).IsUnique();
   modelBuilder.Entity<Note>().HasOne(x=>x.Source).WithMany(x=>x.Notes).HasForeignKey(x=>x.SourceId).OnDelete(DeleteBehavior.SetNull);
   // Quiz: her gün için tek quiz; soru silinen nottan bağımsız kalır; tekrar durumu notla birlikte silinir.
   modelBuilder.Entity<QuizDay>().HasIndex(x=>new{x.Date,x.Round}).IsUnique();
   modelBuilder.Entity<QuizQuestion>().HasOne(x=>x.QuizDay).WithMany(x=>x.Questions).HasForeignKey(x=>x.QuizDayId).OnDelete(DeleteBehavior.Cascade);
   modelBuilder.Entity<QuizQuestion>().HasOne(x=>x.Note).WithMany().HasForeignKey(x=>x.NoteId).OnDelete(DeleteBehavior.SetNull);
   modelBuilder.Entity<QuizQuestion>().HasIndex(x=>new{x.QuizDayId,x.Order}).IsUnique();
   modelBuilder.Entity<NoteReview>().HasOne(x=>x.Note).WithOne().HasForeignKey<NoteReview>(x=>x.NoteId).OnDelete(DeleteBehavior.Cascade);
   modelBuilder.Entity<NoteReview>().HasIndex(x=>x.DueDate);
   modelBuilder.Entity<StickyNote>().HasIndex(x=>new{x.Done,x.SortOrder});
   modelBuilder.Entity<StickyImage>().HasOne(x=>x.StickyNote).WithMany(x=>x.Images).HasForeignKey(x=>x.StickyNoteId).OnDelete(DeleteBehavior.Cascade);
 }
}
public static class CategorySeed {
 // Firmalara ayrılabilen tek kategori.
 public const string Projects = "Projeler";
 public static readonly string[] All = [
 "ABAP Temelleri", "Internal Table ve Modern ABAP", "SQL, CDS ve Dictionary",
 "Nesne Yönelimli ABAP", "ALV ve Ekran İşlemleri", "Fonksiyonlar, BAPI ve Entegrasyon",
 "Forms ve Yazdırma", "Excel, Dosya ve Mail", "Debug ve Hatalar", "SAP Araçları ve Transport",
 Btp, Projects ];
 // SAP BTP (ABAP Cloud, RAP, CAP, cockpit...): ABAP sürüm yazımından ayrı tutulan platform konusu.
 public const string Btp = "SAP BTP";
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using KiComLibBackend.Models;
using KiComLibBackend.Services;

namespace KiComLibBackend.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Manufacturer> Manufacturer { get; set; }
        public DbSet<Distributor> Distributor { get; set; }
        public DbSet<LibraryFile> LibraryFile { get; set; }
        public DbSet<Library> Library { get; set; }
        public DbSet<Blob> Blobs { get; set; }
        public DbSet<Commit> Commits { get; set; }
        public DbSet<SchemaVersion> SchemaVersions { get; set; }
        public DbSet<Datasheet> Datasheet { get; set; }
        public DbSet<DatasheetRevision> DatasheetRevisions { get; set; }
        public DbSet<Model3D> Model3D { get; set; }
        public DbSet<Model3DRevision> Model3DRevisions { get; set; }
        public DbSet<Symbol> Symbol { get; set; }
        public DbSet<SymbolRevision> SymbolRevisions { get; set; }
        public DbSet<Footprint> Footprint { get; set; }
        public DbSet<FootprintRevision> FootprintRevisions { get; set; }
        public DbSet<FootprintRevisionModel3D> FootprintRevisionModel3Ds { get; set; }
        public DbSet<Part> Part { get; set; }
        public DbSet<PartNumber> PartNumber { get; set; }
        public DbSet<OrderNumber> OrderNumber { get; set; }
        public DbSet<CatalogRevision> CatalogRevisions { get; set; }
        public DbSet<Template> Template { get; set; }
        public DbSet<TemplateRevision> TemplateRevisions { get; set; }
        public DbSet<SymbolHeadView> SymbolHeadViews { get; set; }
        public DbSet<FootprintHeadView> FootprintHeadViews { get; set; }
        public DbSet<Model3DHeadView> Model3DHeadViews { get; set; }
        public DbSet<DatasheetHeadView> DatasheetHeadViews { get; set; }
        public DbSet<TemplateHeadView> TemplateHeadViews { get; set; }
        public DbSet<AppUser> AppUsers { get; set; }
        public DbSet<AppGroup> AppGroups { get; set; }
        public DbSet<ApiToken> ApiTokens { get; set; }
        public DbSet<UserSession> UserSessions { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }
        public DbSet<UserGroupLink> UserGroupLinks { get; set; }
        public DbSet<DirectoryConnection> DirectoryConnections { get; set; }
        public DbSet<GitHost> GitHosts { get; set; }
        public DbSet<GitCredential> GitCredentials { get; set; }
        public DbSet<GitRepository> GitRepositories { get; set; }
        public DbSet<GitRef> GitRefs { get; set; }
        public DbSet<Project> Projects { get; set; }
        public DbSet<ProjectGroupAccess> ProjectGroupAccess { get; set; }
        public DbSet<ProjectBlob> ProjectBlobs { get; set; }
        public DbSet<ProjectRevision> ProjectRevisions { get; set; }
        public DbSet<ProjectComponent> ProjectComponents { get; set; }
        public DbSet<ProjectRelease> ProjectReleases { get; set; }
        public DbSet<ProjectArtifact> ProjectArtifacts { get; set; }
        public DbSet<ProjectDiff> ProjectDiffs { get; set; }
        public DbSet<ProjectJob> ProjectJobs { get; set; }
        public DbSet<WarehouseItem> WarehouseItems { get; set; }
        public DbSet<WarehouseBatch> WarehouseBatches { get; set; }

        public IQueryable<Model3DDTO> GetModel3DList()
        {
            return Model3DHeadViews.Select(m => new Model3DDTO
            {
                Id = m.Id,
                Uuid = m.Uuid,
                Filename = m.Filename,
                Directory = m.Directory,
                Comment = m.Comment,
                FileSize = m.FileSize,
                Hash = m.Hash,
                FileHash = m.FileHash ?? m.Hash,
                RevisionNo = m.RevisionNo,
                HeadRevisionId = m.HeadRevisionId,
                UpdatedAt = m.UpdatedAt,
                CreatedAt = m.CreatedAt
            });
        }

        public IQueryable<DatasheetDTO> GetDatasheetList()
        {
            return DatasheetHeadViews.Select(d => new DatasheetDTO
            {
                Id = d.Id,
                Uuid = d.Uuid,
                Filename = d.Filename,
                Directory = d.Directory,
                Comment = d.Comment,
                FileSize = d.FileSize,
                Hash = d.Hash,
                FileHash = d.FileHash ?? d.Hash,
                RevisionNo = d.RevisionNo,
                HeadRevisionId = d.HeadRevisionId,
                UpdatedAt = d.UpdatedAt,
                CreatedAt = d.CreatedAt
            });
        }

        public IQueryable<TemplateDTO> GetTemplateList()
        {
            return TemplateHeadViews.Select(t => new TemplateDTO
            {
                Id = t.Id,
                Uuid = t.Uuid,
                Filename = t.Filename,
                Directory = t.Directory,
                Comment = t.Comment,
                FileSize = t.FileSize,
                Hash = t.Hash,
                FileHash = t.FileHash ?? t.Hash,
                RevisionNo = t.RevisionNo,
                HeadRevisionId = t.HeadRevisionId,
                UpdatedAt = t.UpdatedAt,
                CreatedAt = t.CreatedAt
            });
        }

        public IQueryable<FootprintDTO> GetFootprintList()
        {
            return FootprintHeadViews.Select(f => new FootprintDTO
            {
                Id = f.Id,
                Uuid = f.Uuid,
                LibraryFileId = f.LibraryFileId,
                Name = f.Name,
                Type = f.Type,
                BoardOnly = f.BoardOnly,
                ExcludeFromPosFiles = f.ExcludeFromPosFiles,
                ExcludeFromBom = f.ExcludeFromBom,
                Height = f.Height,
                Layer = f.Layer,
                Description = f.Description,
                Tags = f.Tags,
                Property = f.Property,
                Model3dId = f.Model3dId,
                Hash = f.Hash,
                FileHash = f.FileHash ?? f.Hash,
                RevisionNo = f.RevisionNo,
                HeadRevisionId = f.HeadRevisionId,
                UpdatedAt = f.UpdatedAt,
                CreatedAt = f.CreatedAt
            });
        }

        public IQueryable<SymbolDTO> GetSymbolList()
        {
            return SymbolHeadViews.Select(s => new SymbolDTO
            {
                Id = s.Id,
                Uuid = s.Uuid,
                LibraryFileId = s.LibraryFileId,
                Name = s.Name,
                Reference = s.Reference,
                InBom = s.InBom,
                OnBoard = s.OnBoard,
                PinNumbersHidden = s.PinNumbersHidden,
                PinNamesHidden = s.PinNamesHidden,
                PinNamesOffset = s.PinNamesOffset,
                Description = s.Description,
                Hash = s.Hash,
                FileHash = s.FileHash ?? s.Hash,
                RevisionNo = s.RevisionNo,
                HeadRevisionId = s.HeadRevisionId,
                UpdatedAt = s.UpdatedAt,
                CreatedAt = s.CreatedAt
            });
        }

        public IQueryable<KiCadHttpCategory> GetKiCadHttpCategoryList()
        {
            return Library.Select(category => new KiCadHttpCategory
            {
                Id = category.Id.ToString()!,
                Name = category.Name,
                Description = category.Description,
            });
        }

        public IQueryable<KiCadHttpListPart> GetKiCadHttpListPartList()
        {
            return Part.Select(part => new KiCadHttpListPart
            {
                Id = part.Id.ToString()!,
                Name = part.Name,
                LibraryId = part.LibraryId,
                Description = part.Comment,
            });
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            CreateManufacturer(ref modelBuilder);
            CreateDistributor(ref modelBuilder);
            CreateLibraryFile(ref modelBuilder);
            CreateLibrary(ref modelBuilder);
            CreateBlob(ref modelBuilder);
            CreateCommit(ref modelBuilder);
            CreateSchemaVersion(ref modelBuilder);
            CreateDatasheet(ref modelBuilder);
            CreateModel3D(ref modelBuilder);
            CreateSymbol(ref modelBuilder);
            CreateFootprint(ref modelBuilder);
            CreatePart(ref modelBuilder);
            CreatePartNumber(ref modelBuilder);
            CreateOrderNumber(ref modelBuilder);
            CreateTemplate(ref modelBuilder);
            CreateHeadViews(ref modelBuilder);
            CreateAccess(ref modelBuilder);
            CreateProjects(ref modelBuilder);
            CreateWarehouse(ref modelBuilder);

            base.OnModelCreating(modelBuilder);
        }

        private static void CreateHeadViews(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SymbolHeadView>(e =>
            {
                e.HasNoKey();
                e.ToView("symbol_head_v", "public");
                e.Property(p => p.Id).HasColumnName("id");
                e.Property(p => p.Uuid).HasColumnName("uuid");
                e.Property(p => p.LibraryFileId).HasColumnName("library_file_id");
                e.Property(p => p.Name).HasColumnName("name");
                e.Property(p => p.HeadRevisionId).HasColumnName("head_revision_id");
                e.Property(p => p.DeletedAt).HasColumnName("deleted_at");
                e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
                e.Property(p => p.CreatedAt).HasColumnName("created_at");
                e.Property(p => p.RevisionNo).HasColumnName("revision_no");
                e.Property(p => p.Hash).HasColumnName("hash");
                e.Property(p => p.FileHash).HasColumnName("file_hash");
                e.Property(p => p.PreviewBlobHash).HasColumnName("preview_blob_hash");
                e.Property(p => p.Reference).HasColumnName("reference");
                e.Property(p => p.InBom).HasColumnName("in_bom");
                e.Property(p => p.OnBoard).HasColumnName("on_board");
                e.Property(p => p.PinNumbersHidden).HasColumnName("pin_numbers_hidden");
                e.Property(p => p.PinNamesHidden).HasColumnName("pin_names_hidden");
                e.Property(p => p.PinNamesOffset).HasColumnName("pin_names_offset");
                e.Property(p => p.Description).HasColumnName("description");
                e.Property(p => p.CommitId).HasColumnName("commit_id");
                e.Property(p => p.LibraryFileName).HasColumnName("library_file_name");
                e.Property(p => p.LibraryFileFilename).HasColumnName("library_file_filename");
                e.Property(p => p.LibraryFileVersion).HasColumnName("library_file_version");
            });

            modelBuilder.Entity<FootprintHeadView>(e =>
            {
                e.HasNoKey();
                e.ToView("footprint_head_v", "public");
                e.Property(p => p.Id).HasColumnName("id");
                e.Property(p => p.Uuid).HasColumnName("uuid");
                e.Property(p => p.LibraryFileId).HasColumnName("library_file_id");
                e.Property(p => p.Name).HasColumnName("name");
                e.Property(p => p.HeadRevisionId).HasColumnName("head_revision_id");
                e.Property(p => p.DeletedAt).HasColumnName("deleted_at");
                e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
                e.Property(p => p.CreatedAt).HasColumnName("created_at");
                e.Property(p => p.RevisionNo).HasColumnName("revision_no");
                e.Property(p => p.Hash).HasColumnName("hash");
                e.Property(p => p.FileHash).HasColumnName("file_hash");
                e.Property(p => p.PreviewBlobHash).HasColumnName("preview_blob_hash");
                e.Property(p => p.Type).HasColumnName("type");
                e.Property(p => p.BoardOnly).HasColumnName("board_only");
                e.Property(p => p.ExcludeFromPosFiles).HasColumnName("exclude_from_pos_files");
                e.Property(p => p.ExcludeFromBom).HasColumnName("exclude_from_bom");
                e.Property(p => p.Height).HasColumnName("height");
                e.Property(p => p.Layer).HasColumnName("layer");
                e.Property(p => p.Description).HasColumnName("description");
                e.Property(p => p.Tags).HasColumnName("tags");
                e.Property(p => p.Property).HasColumnName("property");
                e.Property(p => p.CommitId).HasColumnName("commit_id");
                e.Property(p => p.Model3dId).HasColumnName("model3d_id");
                e.Property(p => p.LibraryFileName).HasColumnName("library_file_name");
                e.Property(p => p.LibraryFileFilename).HasColumnName("library_file_filename");
                e.Property(p => p.LibraryFileVersion).HasColumnName("library_file_version");
            });

            modelBuilder.Entity<Model3DHeadView>(e =>
            {
                e.HasNoKey();
                e.ToView("model3d_head_v", "public");
                e.Property(p => p.Id).HasColumnName("id");
                e.Property(p => p.Uuid).HasColumnName("uuid");
                e.Property(p => p.Directory).HasColumnName("directory");
                e.Property(p => p.Filename).HasColumnName("filename");
                e.Property(p => p.HeadRevisionId).HasColumnName("head_revision_id");
                e.Property(p => p.DeletedAt).HasColumnName("deleted_at");
                e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
                e.Property(p => p.CreatedAt).HasColumnName("created_at");
                e.Property(p => p.RevisionNo).HasColumnName("revision_no");
                e.Property(p => p.Hash).HasColumnName("hash");
                e.Property(p => p.FileHash).HasColumnName("file_hash");
                e.Property(p => p.Comment).HasColumnName("comment");
                e.Property(p => p.FileSize).HasColumnName("file_size");
                e.Property(p => p.CommitId).HasColumnName("commit_id");
            });

            modelBuilder.Entity<DatasheetHeadView>(e =>
            {
                e.HasNoKey();
                e.ToView("datasheet_head_v", "public");
                e.Property(p => p.Id).HasColumnName("id");
                e.Property(p => p.Uuid).HasColumnName("uuid");
                e.Property(p => p.Directory).HasColumnName("directory");
                e.Property(p => p.Filename).HasColumnName("filename");
                e.Property(p => p.HeadRevisionId).HasColumnName("head_revision_id");
                e.Property(p => p.DeletedAt).HasColumnName("deleted_at");
                e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
                e.Property(p => p.CreatedAt).HasColumnName("created_at");
                e.Property(p => p.RevisionNo).HasColumnName("revision_no");
                e.Property(p => p.Hash).HasColumnName("hash");
                e.Property(p => p.FileHash).HasColumnName("file_hash");
                e.Property(p => p.Comment).HasColumnName("comment");
                e.Property(p => p.FileSize).HasColumnName("file_size");
                e.Property(p => p.CommitId).HasColumnName("commit_id");
            });

            modelBuilder.Entity<TemplateHeadView>(e =>
            {
                e.HasNoKey();
                e.ToView("template_head_v", "public");
                e.Property(p => p.Id).HasColumnName("id");
                e.Property(p => p.Uuid).HasColumnName("uuid");
                e.Property(p => p.Directory).HasColumnName("directory");
                e.Property(p => p.Filename).HasColumnName("filename");
                e.Property(p => p.HeadRevisionId).HasColumnName("head_revision_id");
                e.Property(p => p.DeletedAt).HasColumnName("deleted_at");
                e.Property(p => p.UpdatedAt).HasColumnName("updated_at");
                e.Property(p => p.CreatedAt).HasColumnName("created_at");
                e.Property(p => p.RevisionNo).HasColumnName("revision_no");
                e.Property(p => p.Hash).HasColumnName("hash");
                e.Property(p => p.FileHash).HasColumnName("file_hash");
                e.Property(p => p.Comment).HasColumnName("comment");
                e.Property(p => p.FileSize).HasColumnName("file_size");
                e.Property(p => p.CommitId).HasColumnName("commit_id");
            });
        }

        private static void CreateBlob(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Blob>().ToTable("blob", "public");
            modelBuilder.Entity<Blob>().HasKey(p => p.Hash);
            modelBuilder.Entity<Blob>().Property(p => p.Hash).HasColumnName("hash").HasColumnType("character(64)");
            modelBuilder.Entity<Blob>().Property(p => p.Size).HasColumnName("size");
            modelBuilder.Entity<Blob>().Property(p => p.MediaType).HasColumnName("media_type");
            modelBuilder.Entity<Blob>().Property(p => p.Data).HasColumnName("data").HasColumnType("bytea");
            modelBuilder.Entity<Blob>().Property(p => p.Storage).HasColumnName("storage");
            modelBuilder.Entity<Blob>().Property(p => p.ExternalRef).HasColumnName("external_ref");
            modelBuilder.Entity<Blob>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }

        private static void CreateCommit(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Commit>().ToTable("commit", "public");
            modelBuilder.Entity<Commit>().Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<Commit>().Property(p => p.Uuid).HasColumnName("uuid").HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Commit>().Property(p => p.ParentCommitId).HasColumnName("parent_commit_id");
            modelBuilder.Entity<Commit>().Property(p => p.Message).HasColumnName("message");
            modelBuilder.Entity<Commit>().Property(p => p.Author).HasColumnName("author");
            modelBuilder.Entity<Commit>().Property(p => p.Client).HasColumnName("client");
            modelBuilder.Entity<Commit>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }

        private static void CreateSchemaVersion(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SchemaVersion>().ToTable("schema_version", "public");
            modelBuilder.Entity<SchemaVersion>().HasKey(p => p.Version);
            modelBuilder.Entity<SchemaVersion>().Property(p => p.Version).HasColumnName("version");
            modelBuilder.Entity<SchemaVersion>().Property(p => p.AppliedAt).HasColumnName("applied_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }

        private void CreateManufacturer(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Manufacturer>().ToTable("manufacturer", "public");
            modelBuilder.Entity<Manufacturer>().Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<Manufacturer>().Property(p => p.Uuid).HasColumnName("uuid").HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Manufacturer>().Property(p => p.Name).HasColumnName("name");
            modelBuilder.Entity<Manufacturer>().Property(p => p.Url).HasColumnName("url");
            modelBuilder.Entity<Manufacturer>().Property(p => p.TrustedPartsId).HasColumnName("trustedparts_id");
            modelBuilder.Entity<Manufacturer>().Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Manufacturer>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }

        private void CreateDistributor(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Distributor>().ToTable("distributor", "public");
            modelBuilder.Entity<Distributor>().Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<Distributor>().Property(p => p.Uuid).HasColumnName("uuid").HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Distributor>().Property(p => p.Name).HasColumnName("name");
            modelBuilder.Entity<Distributor>().Property(p => p.Url).HasColumnName("url");
            modelBuilder.Entity<Distributor>().Property(p => p.TrustedPartsId).HasColumnName("trustedparts_id");
            modelBuilder.Entity<Distributor>().Property(p => p.IsFavorite).HasColumnName("is_favorite");
            modelBuilder.Entity<Distributor>().Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Distributor>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }

        private void CreateLibraryFile(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LibraryFile>().ToTable("library_file", "public");
            modelBuilder.Entity<LibraryFile>().Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<LibraryFile>().Property(p => p.Uuid).HasColumnName("uuid").HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<LibraryFile>().Property(p => p.Type).HasColumnName("type");
            modelBuilder.Entity<LibraryFile>().Property(p => p.Name).HasColumnName("name");
            modelBuilder.Entity<LibraryFile>().Property(p => p.Comment).HasColumnName("comment");
            modelBuilder.Entity<LibraryFile>().Property(p => p.Filename).HasColumnName("filename");
            modelBuilder.Entity<LibraryFile>().Property(p => p.Description).HasColumnName("description");
            modelBuilder.Entity<LibraryFile>().Property(p => p.Version).HasColumnName("version");
            modelBuilder.Entity<LibraryFile>().Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<LibraryFile>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }

        private void CreateLibrary(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Library>().ToTable("library", "public");
            modelBuilder.Entity<Library>().Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<Library>().Property(p => p.Uuid).HasColumnName("uuid").HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Library>().Property(p => p.Name).HasColumnName("name");
            modelBuilder.Entity<Library>().Property(p => p.Filename).HasColumnName("filename");
            modelBuilder.Entity<Library>().Property(p => p.Description).HasColumnName("description");
            modelBuilder.Entity<Library>().Property(p => p.SpecialColumns).HasColumnName("special_columns");
            modelBuilder.Entity<Library>().Property(p => p.SpecialColumns).HasColumnType("text[]");
            modelBuilder.Entity<Library>().Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Library>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }

        private void CreateDatasheet(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Datasheet>().ToTable("datasheet", "public");
            modelBuilder.Entity<Datasheet>().Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<Datasheet>().Property(p => p.Uuid).HasColumnName("uuid").HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Datasheet>().Property(p => p.Directory).HasColumnName("directory");
            modelBuilder.Entity<Datasheet>().Property(p => p.Filename).HasColumnName("filename");
            modelBuilder.Entity<Datasheet>().Property(p => p.HeadRevisionId).HasColumnName("head_revision_id");
            modelBuilder.Entity<Datasheet>().Property(p => p.DeletedAt).HasColumnName("deleted_at");
            modelBuilder.Entity<Datasheet>().Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Datasheet>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Datasheet>()
                .HasOne(d => d.HeadRevision)
                .WithMany()
                .HasForeignKey(d => d.HeadRevisionId)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<Datasheet>()
                .HasMany(d => d.Revisions)
                .WithOne(r => r.Datasheet)
                .HasForeignKey(r => r.DatasheetId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<DatasheetRevision>().ToTable("datasheet_revision", "public");
            modelBuilder.Entity<DatasheetRevision>().Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<DatasheetRevision>().Property(p => p.DatasheetId).HasColumnName("datasheet_id");
            modelBuilder.Entity<DatasheetRevision>().Property(p => p.CommitId).HasColumnName("commit_id");
            modelBuilder.Entity<DatasheetRevision>().Property(p => p.RevisionNo).HasColumnName("revision_no");
            modelBuilder.Entity<DatasheetRevision>().Property(p => p.IsDeleted).HasColumnName("is_deleted");
            modelBuilder.Entity<DatasheetRevision>().Property(p => p.Directory).HasColumnName("directory");
            modelBuilder.Entity<DatasheetRevision>().Property(p => p.Filename).HasColumnName("filename");
            modelBuilder.Entity<DatasheetRevision>().Property(p => p.Comment).HasColumnName("comment");
            modelBuilder.Entity<DatasheetRevision>().Property(p => p.BlobHash).HasColumnName("blob_hash");
            modelBuilder.Entity<DatasheetRevision>().Property(p => p.Size).HasColumnName("size");
            modelBuilder.Entity<DatasheetRevision>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }

        private void CreateModel3D(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Model3D>().ToTable("model3d", "public");
            modelBuilder.Entity<Model3D>().Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<Model3D>().Property(p => p.Uuid).HasColumnName("uuid").HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Model3D>().Property(p => p.Directory).HasColumnName("directory");
            modelBuilder.Entity<Model3D>().Property(p => p.Filename).HasColumnName("filename");
            modelBuilder.Entity<Model3D>().Property(p => p.HeadRevisionId).HasColumnName("head_revision_id");
            modelBuilder.Entity<Model3D>().Property(p => p.DeletedAt).HasColumnName("deleted_at");
            modelBuilder.Entity<Model3D>().Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Model3D>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Model3D>()
                .HasOne(d => d.HeadRevision)
                .WithMany()
                .HasForeignKey(d => d.HeadRevisionId)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<Model3D>()
                .HasMany(d => d.Revisions)
                .WithOne(r => r.Model3D)
                .HasForeignKey(r => r.Model3dId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Model3DRevision>().ToTable("model3d_revision", "public");
            modelBuilder.Entity<Model3DRevision>().Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<Model3DRevision>().Property(p => p.Model3dId).HasColumnName("model3d_id");
            modelBuilder.Entity<Model3DRevision>().Property(p => p.CommitId).HasColumnName("commit_id");
            modelBuilder.Entity<Model3DRevision>().Property(p => p.RevisionNo).HasColumnName("revision_no");
            modelBuilder.Entity<Model3DRevision>().Property(p => p.IsDeleted).HasColumnName("is_deleted");
            modelBuilder.Entity<Model3DRevision>().Property(p => p.Directory).HasColumnName("directory");
            modelBuilder.Entity<Model3DRevision>().Property(p => p.Filename).HasColumnName("filename");
            modelBuilder.Entity<Model3DRevision>().Property(p => p.Comment).HasColumnName("comment");
            modelBuilder.Entity<Model3DRevision>().Property(p => p.BlobHash).HasColumnName("blob_hash");
            modelBuilder.Entity<Model3DRevision>().Property(p => p.Size).HasColumnName("size");
            modelBuilder.Entity<Model3DRevision>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }

        private void CreateSymbol(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Symbol>()
                .HasOne(p => p.LibraryFile)
                .WithMany(b => b.Symbols)
                .HasForeignKey(p => p.LibraryFileId);

            modelBuilder.Entity<Symbol>().ToTable("symbol", "public");
            modelBuilder.Entity<Symbol>().Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<Symbol>().Property(p => p.Uuid).HasColumnName("uuid").HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Symbol>().Property(p => p.LibraryFileId).HasColumnName("library_file_id");
            modelBuilder.Entity<Symbol>().Property(p => p.Name).HasColumnName("name");
            modelBuilder.Entity<Symbol>().Property(p => p.HeadRevisionId).HasColumnName("head_revision_id");
            modelBuilder.Entity<Symbol>().Property(p => p.DeletedAt).HasColumnName("deleted_at");
            modelBuilder.Entity<Symbol>().Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Symbol>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Symbol>()
                .HasOne(d => d.HeadRevision)
                .WithMany()
                .HasForeignKey(d => d.HeadRevisionId)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<Symbol>()
                .HasMany(d => d.Revisions)
                .WithOne(r => r.Symbol)
                .HasForeignKey(r => r.SymbolId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SymbolRevision>().ToTable("symbol_revision", "public");
            modelBuilder.Entity<SymbolRevision>().Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<SymbolRevision>().Property(p => p.SymbolId).HasColumnName("symbol_id");
            modelBuilder.Entity<SymbolRevision>().Property(p => p.CommitId).HasColumnName("commit_id");
            modelBuilder.Entity<SymbolRevision>().Property(p => p.RevisionNo).HasColumnName("revision_no");
            modelBuilder.Entity<SymbolRevision>().Property(p => p.IsDeleted).HasColumnName("is_deleted");
            modelBuilder.Entity<SymbolRevision>().Property(p => p.LibraryFileId).HasColumnName("library_file_id");
            modelBuilder.Entity<SymbolRevision>().Property(p => p.Name).HasColumnName("name");
            modelBuilder.Entity<SymbolRevision>().Property(p => p.BlobHash).HasColumnName("blob_hash");
            modelBuilder.Entity<SymbolRevision>().Property(p => p.PreviewBlobHash).HasColumnName("preview_blob_hash");
            modelBuilder.Entity<SymbolRevision>().Property(p => p.Reference).HasColumnName("reference");
            modelBuilder.Entity<SymbolRevision>().Property(p => p.InBom).HasColumnName("in_bom");
            modelBuilder.Entity<SymbolRevision>().Property(p => p.OnBoard).HasColumnName("on_board");
            modelBuilder.Entity<SymbolRevision>().Property(p => p.PinNumbersHidden).HasColumnName("pin_numbers_hidden");
            modelBuilder.Entity<SymbolRevision>().Property(p => p.PinNamesHidden).HasColumnName("pin_names_hidden");
            modelBuilder.Entity<SymbolRevision>().Property(p => p.PinNamesOffset).HasColumnName("pin_names_offset");
            modelBuilder.Entity<SymbolRevision>().Property(p => p.Description).HasColumnName("description");
            modelBuilder.Entity<SymbolRevision>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }

        private void CreateFootprint(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Footprint>()
                .HasOne(p => p.LibraryFile)
                .WithMany(b => b.Footprints)
                .HasForeignKey(p => p.LibraryFileId);

            modelBuilder.Entity<Footprint>().ToTable("footprint", "public");
            modelBuilder.Entity<Footprint>().Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<Footprint>().Property(p => p.Uuid).HasColumnName("uuid").HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Footprint>().Property(p => p.LibraryFileId).HasColumnName("library_file_id");
            modelBuilder.Entity<Footprint>().Property(p => p.Name).HasColumnName("name");
            modelBuilder.Entity<Footprint>().Property(p => p.HeadRevisionId).HasColumnName("head_revision_id");
            modelBuilder.Entity<Footprint>().Property(p => p.DeletedAt).HasColumnName("deleted_at");
            modelBuilder.Entity<Footprint>().Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Footprint>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Footprint>()
                .HasOne(d => d.HeadRevision)
                .WithMany()
                .HasForeignKey(d => d.HeadRevisionId)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<Footprint>()
                .HasMany(d => d.Revisions)
                .WithOne(r => r.Footprint)
                .HasForeignKey(r => r.FootprintId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<FootprintRevision>().ToTable("footprint_revision", "public");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<FootprintRevision>().Property(p => p.FootprintId).HasColumnName("footprint_id");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.CommitId).HasColumnName("commit_id");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.RevisionNo).HasColumnName("revision_no");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.IsDeleted).HasColumnName("is_deleted");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.LibraryFileId).HasColumnName("library_file_id");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.Name).HasColumnName("name");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.BlobHash).HasColumnName("blob_hash");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.PreviewBlobHash).HasColumnName("preview_blob_hash");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.Type).HasColumnName("type");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.BoardOnly).HasColumnName("board_only");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.ExcludeFromPosFiles).HasColumnName("exclude_from_pos_files");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.ExcludeFromBom).HasColumnName("exclude_from_bom");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.Height).HasColumnName("height");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.Layer).HasColumnName("layer");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.Description).HasColumnName("description");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.Tags).HasColumnName("tags");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.Property).HasColumnName("property");
            modelBuilder.Entity<FootprintRevision>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

            modelBuilder.Entity<FootprintRevisionModel3D>().ToTable("footprint_revision_model3d", "public");
            modelBuilder.Entity<FootprintRevisionModel3D>().HasKey(p => new { p.FootprintRevisionId, p.Position });
            modelBuilder.Entity<FootprintRevisionModel3D>().Property(p => p.FootprintRevisionId).HasColumnName("footprint_revision_id");
            modelBuilder.Entity<FootprintRevisionModel3D>().Property(p => p.Position).HasColumnName("position");
            modelBuilder.Entity<FootprintRevisionModel3D>().Property(p => p.Model3dId).HasColumnName("model3d_id");
            modelBuilder.Entity<FootprintRevisionModel3D>().Property(p => p.OffsetX).HasColumnName("offset_x");
            modelBuilder.Entity<FootprintRevisionModel3D>().Property(p => p.OffsetY).HasColumnName("offset_y");
            modelBuilder.Entity<FootprintRevisionModel3D>().Property(p => p.OffsetZ).HasColumnName("offset_z");
            modelBuilder.Entity<FootprintRevisionModel3D>().Property(p => p.ScaleX).HasColumnName("scale_x");
            modelBuilder.Entity<FootprintRevisionModel3D>().Property(p => p.ScaleY).HasColumnName("scale_y");
            modelBuilder.Entity<FootprintRevisionModel3D>().Property(p => p.ScaleZ).HasColumnName("scale_z");
            modelBuilder.Entity<FootprintRevisionModel3D>().Property(p => p.RotateX).HasColumnName("rotate_x");
            modelBuilder.Entity<FootprintRevisionModel3D>().Property(p => p.RotateY).HasColumnName("rotate_y");
            modelBuilder.Entity<FootprintRevisionModel3D>().Property(p => p.RotateZ).HasColumnName("rotate_z");
            modelBuilder.Entity<FootprintRevisionModel3D>().Property(p => p.Opacity).HasColumnName("opacity");
            modelBuilder.Entity<FootprintRevisionModel3D>().Property(p => p.Hide).HasColumnName("hide");
        }

        private void CreatePart(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Part>().ToTable("part", "public");
            modelBuilder.Entity<Part>().Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<Part>().Property(p => p.Uuid).HasColumnName("uuid").HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Part>().Property(p => p.Name).HasColumnName("name");
            modelBuilder.Entity<Part>().Property(p => p.Comment).HasColumnName("comment");
            modelBuilder.Entity<Part>().Property(p => p.DatasheetId).HasColumnName("datasheet_id");
            modelBuilder.Entity<Part>().Property(p => p.DatasheetIds)
                .HasColumnName("datasheet_ids")
                .HasColumnType("json")
                .HasConversion(
                    new ValueConverter<List<int>, string>(
                        ids => SerializeAlternativeIds(ids),
                        json => ParseAlternativeIds(json)),
                    IntIdListComparer);
            modelBuilder.Entity<Part>().Property(p => p.Datasheet).HasColumnName("datasheet");
            modelBuilder.Entity<Part>().Property(p => p.LibraryId).HasColumnName("library_id");
            modelBuilder.Entity<Part>().Property(p => p.SymbolId).HasColumnName("symbol_id");
            modelBuilder.Entity<Part>().Property(p => p.FootprintId).HasColumnName("footprint_id");
            modelBuilder.Entity<Part>().Property(p => p.LifecycleStatus)
                .HasColumnName("lifecycle_status")
                .HasMaxLength(16)
                .IsRequired()
                .HasDefaultValue("active");
            modelBuilder.Entity<Part>().Property(p => p.Keywords).HasColumnName("keywords");
            modelBuilder.Entity<Part>().Property(p => p.Value).HasColumnName("value");
            modelBuilder.Entity<Part>().Property(p => p.Properties).HasColumnName("properties").HasColumnType("json");
            modelBuilder.Entity<Part>().Property(p => p.Alternatives)
                .HasColumnName("alternatives")
                .HasColumnType("json")
                .HasConversion(
                    new ValueConverter<List<int>, string>(
                        ids => SerializeAlternativeIds(ids),
                        json => ParseAlternativeIds(json)),
                    IntIdListComparer);
            modelBuilder.Entity<Part>().Property(p => p.Links)
                .HasColumnName("links")
                .HasColumnType("json")
                .HasConversion(
                    new ValueConverter<List<PartLink>, string>(
                        links => PartLinks.Serialize(links),
                        json => PartLinks.Parse(json)),
                    LinkListComparer);
            modelBuilder.Entity<Part>().Property(p => p.ImageHash)
                .HasColumnName("image_hash")
                .HasMaxLength(64)
                .IsFixedLength()
                .HasConversion(
                    hash => hash,
                    hash => string.IsNullOrWhiteSpace(hash) ? null : hash.Trim());
            modelBuilder.Entity<Part>().Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Part>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }

        private void CreatePartNumber(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PartNumber>().ToTable("part_number", "public");
            modelBuilder.Entity<PartNumber>().Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<PartNumber>().Property(p => p.Uuid).HasColumnName("uuid").HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<PartNumber>().Property(p => p.PartId).HasColumnName("part_id");
            modelBuilder.Entity<PartNumber>().Property(p => p.ManufacturerId).HasColumnName("manufacturer_id");
            modelBuilder.Entity<PartNumber>().Property(p => p.ManufacturerPartNumber).HasColumnName("manufacturer_part_number");
            modelBuilder.Entity<PartNumber>().Property(p => p.Quantity).HasColumnName("quantity").HasDefaultValue(1);
            modelBuilder.Entity<PartNumber>().Property(p => p.PackageType).HasColumnName("package_type");
            modelBuilder.Entity<PartNumber>().Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<PartNumber>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }

        private void CreateOrderNumber(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OrderNumber>().ToTable("order_number", "public");
            modelBuilder.Entity<OrderNumber>().Property(o => o.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<OrderNumber>().Property(o => o.Uuid).HasColumnName("uuid").HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<OrderNumber>().Property(o => o.OrderNumberValue).HasColumnName("order_number");
            modelBuilder.Entity<OrderNumber>().Property(o => o.PartNumberId).HasColumnName("part_number_id");
            modelBuilder.Entity<OrderNumber>().Property(o => o.DistributorId).HasColumnName("distributor_id");
            modelBuilder.Entity<OrderNumber>().Property(o => o.QuantityMin).HasColumnName("quantity_min").HasDefaultValue(1);
            modelBuilder.Entity<OrderNumber>().Property(o => o.QuantitySteps).HasColumnName("quantity_steps").HasDefaultValue(1);
            modelBuilder.Entity<OrderNumber>().Property(o => o.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<OrderNumber>().Property(o => o.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }

        private void CreateTemplate(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Template>().ToTable("template", "public");
            modelBuilder.Entity<Template>().Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<Template>().Property(p => p.Uuid).HasColumnName("uuid").HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Template>().Property(p => p.Directory).HasColumnName("directory");
            modelBuilder.Entity<Template>().Property(p => p.Filename).HasColumnName("filename");
            modelBuilder.Entity<Template>().Property(p => p.HeadRevisionId).HasColumnName("head_revision_id");
            modelBuilder.Entity<Template>().Property(p => p.DeletedAt).HasColumnName("deleted_at");
            modelBuilder.Entity<Template>().Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAddOrUpdate().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Template>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
            modelBuilder.Entity<Template>()
                .HasOne(d => d.HeadRevision)
                .WithMany()
                .HasForeignKey(d => d.HeadRevisionId)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<Template>()
                .HasMany(d => d.Revisions)
                .WithOne(r => r.Template)
                .HasForeignKey(r => r.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TemplateRevision>().ToTable("template_revision", "public");
            modelBuilder.Entity<TemplateRevision>().Property(p => p.Id).HasColumnName("id").ValueGeneratedOnAdd();
            modelBuilder.Entity<TemplateRevision>().Property(p => p.TemplateId).HasColumnName("template_id");
            modelBuilder.Entity<TemplateRevision>().Property(p => p.CommitId).HasColumnName("commit_id");
            modelBuilder.Entity<TemplateRevision>().Property(p => p.RevisionNo).HasColumnName("revision_no");
            modelBuilder.Entity<TemplateRevision>().Property(p => p.IsDeleted).HasColumnName("is_deleted");
            modelBuilder.Entity<TemplateRevision>().Property(p => p.Directory).HasColumnName("directory");
            modelBuilder.Entity<TemplateRevision>().Property(p => p.Filename).HasColumnName("filename");
            modelBuilder.Entity<TemplateRevision>().Property(p => p.Comment).HasColumnName("comment");
            modelBuilder.Entity<TemplateRevision>().Property(p => p.BlobHash).HasColumnName("blob_hash");
            modelBuilder.Entity<TemplateRevision>().Property(p => p.Size).HasColumnName("size");
            modelBuilder.Entity<TemplateRevision>().Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").ValueGeneratedOnAdd().Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        }

        private static void CreateAccess(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AppUser>().Property(p => p.Uuid).HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
            modelBuilder.Entity<AppGroup>().Property(p => p.Uuid).HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
            modelBuilder.Entity<ApiToken>().Property(p => p.Uuid).HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
            modelBuilder.Entity<ApiToken>()
                .HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserSession>()
                .HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AppSetting>().ToTable("app_setting", "public");
            modelBuilder.Entity<AppSetting>().HasKey(s => s.Key);
            modelBuilder.Entity<AppSetting>().Property(s => s.Key).HasColumnName("key");
            modelBuilder.Entity<AppSetting>().Property(s => s.Value).HasColumnName("value");
            modelBuilder.Entity<AppSetting>().Property(s => s.UpdatedAt).HasColumnName("updated_at");

            modelBuilder.Entity<DirectoryConnection>().Property(p => p.Id).ValueGeneratedNever();

            modelBuilder.Entity<AppUser>()
                .HasMany(u => u.Groups)
                .WithMany(g => g.Users)
                .UsingEntity<UserGroupLink>(
                    right => right.HasOne<AppGroup>().WithMany().HasForeignKey(link => link.GroupId),
                    left => left.HasOne<AppUser>().WithMany().HasForeignKey(link => link.UserId),
                    join =>
                    {
                        join.ToTable("user_group", "public");
                        join.Property(link => link.UserId).HasColumnName("user_id");
                        join.Property(link => link.GroupId).HasColumnName("group_id");
                        join.Property(link => link.Origin).HasColumnName("origin").HasDefaultValue("manual");
                        join.HasKey(link => new { link.UserId, link.GroupId });
                    });
        }

        private static void CreateWarehouse(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<WarehouseItem>().Property(p => p.ImageHash)
                .HasMaxLength(64)
                .IsFixedLength()
                .HasConversion(
                    hash => hash,
                    hash => string.IsNullOrWhiteSpace(hash) ? null : hash.Trim());
            modelBuilder.Entity<WarehouseItem>().Property(p => p.Uuid).HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
            modelBuilder.Entity<WarehouseItem>().Property(p => p.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            modelBuilder.Entity<WarehouseItem>().Property(p => p.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();
            modelBuilder.Entity<WarehouseItem>()
                .HasOne(item => item.Part)
                .WithMany()
                .HasForeignKey(item => item.PartId)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<WarehouseItem>()
                .HasMany(item => item.Batches)
                .WithOne(batch => batch.Item!)
                .HasForeignKey(batch => batch.ItemId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<WarehouseBatch>().Property(p => p.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            modelBuilder.Entity<WarehouseBatch>().Property(p => p.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();
        }

        private static void CreateProjects(ref ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GitHost>().Property(p => p.Uuid).HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
            modelBuilder.Entity<GitHost>().Property(p => p.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            modelBuilder.Entity<GitHost>().Property(p => p.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();
            modelBuilder.Entity<GitHost>()
                .HasMany(h => h.Credentials)
                .WithOne(c => c.Host)
                .HasForeignKey(c => c.HostId)
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<GitHost>()
                .HasMany(h => h.Repositories)
                .WithOne(r => r.Host)
                .HasForeignKey(r => r.HostId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<GitCredential>().Property(p => p.Uuid).HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
            modelBuilder.Entity<GitCredential>().Property(p => p.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            modelBuilder.Entity<GitCredential>().Property(p => p.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            modelBuilder.Entity<GitRepository>().Property(p => p.Uuid).HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
            modelBuilder.Entity<GitRepository>().Property(p => p.TrackedBranches).HasColumnType("text[]");
            modelBuilder.Entity<GitRepository>().Property(p => p.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            modelBuilder.Entity<GitRepository>().Property(p => p.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();
            modelBuilder.Entity<GitRepository>()
                .HasOne(r => r.Credential)
                .WithMany()
                .HasForeignKey(r => r.CredentialId)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<GitRepository>()
                .HasMany(r => r.Refs)
                .WithOne(r => r.Repository)
                .HasForeignKey(r => r.RepositoryId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<GitRepository>()
                .HasMany(r => r.Projects)
                .WithOne(p => p.Repository)
                .HasForeignKey(p => p.RepositoryId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<GitRef>().HasKey(r => new { r.RepositoryId, r.Name });
            modelBuilder.Entity<GitRef>().Property(p => p.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            modelBuilder.Entity<Project>().Property(p => p.Uuid).HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
            modelBuilder.Entity<Project>().Property(p => p.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            modelBuilder.Entity<Project>().Property(p => p.UpdatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();
            modelBuilder.Entity<Project>()
                .HasOne(p => p.HeadRevision)
                .WithMany()
                .HasForeignKey(p => p.HeadRevisionId)
                .OnDelete(DeleteBehavior.SetNull);
            modelBuilder.Entity<Project>()
                .HasMany(p => p.Revisions)
                .WithOne(r => r.Project)
                .HasForeignKey(r => r.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<Project>()
                .HasMany(p => p.GroupAccess)
                .WithOne(a => a.Project)
                .HasForeignKey(a => a.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProjectGroupAccess>().HasKey(a => new { a.ProjectId, a.GroupId });
            modelBuilder.Entity<ProjectGroupAccess>()
                .HasOne(a => a.Group)
                .WithMany()
                .HasForeignKey(a => a.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProjectBlob>().Property(p => p.Hash).ValueGeneratedNever();
            modelBuilder.Entity<ProjectBlob>().Property(p => p.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

            modelBuilder.Entity<ProjectRevision>().Property(p => p.ParentShas).HasColumnType("text[]");
            modelBuilder.Entity<ProjectRevision>().Property(p => p.Refs).HasColumnType("text[]");
            modelBuilder.Entity<ProjectRevision>().Property(p => p.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

            modelBuilder.Entity<ProjectComponent>().Property(p => p.SymbolUuids).HasColumnType("uuid[]");
            modelBuilder.Entity<ProjectComponent>().Property(p => p.Fields).HasColumnType("jsonb");

            modelBuilder.Entity<ProjectRelease>().Property(p => p.Uuid).HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
            modelBuilder.Entity<ProjectRelease>().Property(p => p.ReleasedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

            modelBuilder.Entity<ProjectArtifact>().Property(p => p.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

            modelBuilder.Entity<ProjectDiff>().Property(p => p.Uuid).HasDefaultValueSql("gen_random_uuid()").ValueGeneratedOnAdd();
            modelBuilder.Entity<ProjectDiff>().Property(p => p.Summary).HasColumnType("jsonb");
            modelBuilder.Entity<ProjectDiff>().Property(p => p.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();

            modelBuilder.Entity<ProjectJob>().Property(p => p.Payload).HasColumnType("jsonb");
            modelBuilder.Entity<ProjectJob>().Property(p => p.RunAfter).HasDefaultValueSql("now()");
            modelBuilder.Entity<ProjectJob>().Property(p => p.CreatedAt).HasDefaultValueSql("now()").ValueGeneratedOnAdd();
        }

        private static readonly ValueComparer<List<PartLink>> LinkListComparer = new(
            (left, right) => SameLinks(left, right),
            links => (links ?? new List<PartLink>()).Aggregate(0, (hash, link) => HashCode.Combine(hash, link.Label, link.Url)),
            links => links == null
                ? new List<PartLink>()
                : links.Select(link => new PartLink { Label = link.Label, Url = link.Url }).ToList());

        private static bool SameLinks(List<PartLink>? left, List<PartLink>? right)
        {
            left ??= new List<PartLink>();
            right ??= new List<PartLink>();
            return left.Count == right.Count
                && left.Select((link, index) => link.Label == right[index].Label && link.Url == right[index].Url).All(same => same);
        }

        private static readonly ValueComparer<List<int>> IntIdListComparer = new(
            (left, right) => (left ?? new List<int>()).SequenceEqual(right ?? new List<int>()),
            ids => (ids ?? new List<int>()).Aggregate(0, (hash, id) => HashCode.Combine(hash, id)),
            ids => ids == null ? new List<int>() : ids.ToList());

        private static string SerializeAlternativeIds(List<int>? ids)
        {
            return JsonSerializer.Serialize(ids ?? new List<int>());
        }

        /// <summary>
        /// Reads the alternatives column as a list of part ids. Accepts a JSON array
        /// of numbers and an object with a partIds array; anything else is empty.
        /// </summary>
        private static List<int> ParseAlternativeIds(string? json)
        {
            if (string.IsNullOrWhiteSpace(json) || json == "null")
            {
                return new List<int>();
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("partIds", out JsonElement nested)
                    && nested.ValueKind == JsonValueKind.Array)
                {
                    root = nested;
                }

                if (root.ValueKind != JsonValueKind.Array)
                {
                    return new List<int>();
                }

                List<int> ids = new();
                foreach (JsonElement item in root.EnumerateArray())
                {
                    if (item.TryGetInt32(out int id) && id > 0 && !ids.Contains(id))
                    {
                        ids.Add(id);
                    }
                }

                return ids;
            }
            catch (JsonException)
            {
                return new List<int>();
            }
        }
    }
}

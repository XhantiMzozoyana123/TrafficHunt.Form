using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TrafficHunt.Domain.Entities;

namespace TrafficHunt.Domain
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Design-time constructor used by `dotnet ef` migrations.
        public ApplicationDbContext() : base(DesignTimeDbContextOptions())
        {
        }

        private static DbContextOptions<ApplicationDbContext> DesignTimeDbContextOptions()
        {
            // Prefer the appsettings.json in the TrafficHunt.Maui project folder if present,
            // then fall back to walking up from the current directory.
            var mauiPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "TrafficHunt.Maui"));
            string candidatePath;

            if (Directory.Exists(mauiPath) && File.Exists(Path.Combine(mauiPath, "appsettings.json")))
            {
                candidatePath = mauiPath;
            }
            else
            {
                candidatePath = Directory.GetCurrentDirectory();
                for (int i = 0; i < 5; i++)
                {
                    if (File.Exists(Path.Combine(candidatePath, "appsettings.json")))
                        break;
                    var parent = Directory.GetParent(candidatePath);
                    if (parent == null) break;
                    candidatePath = parent.FullName;
                }
            }

            var configuration = new ConfigurationBuilder()
                .SetBasePath(candidatePath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? "Data Source=traffichunt.db";

            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseSqlite(connectionString);

            return optionsBuilder.Options;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Comment>(entity =>
            {
                // Refetching the same YouTube comment updates the row instead of duplicating it.
                entity.HasIndex(comment => comment.CommentId).IsUnique();
            });
        }

        public DbSet<Lead> Leads { get; set; }

        public DbSet<Comment> Comments { get; set; }
    }

    /// <summary>
    /// Extension methods for registering ApplicationDbContext with SQLite.
    /// </summary>
    public static class ApplicationDbContextExtensions
    {
        /// <summary>
        /// Registers ApplicationDbContext with SQLite using the DefaultConnection from configuration.
        /// </summary>
        public static IServiceCollection AddSqliteDbContext(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? "Data Source=traffichunt.db";

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite(connectionString));

            return services;
        }
    }
}

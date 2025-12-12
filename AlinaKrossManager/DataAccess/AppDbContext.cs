using AlinaKrossManager.DataAccess.Models;
using Microsoft.EntityFrameworkCore;

namespace AlinaKrossManager.DataAccess
{
	public class AppDbContext : DbContext
	{
		public AppDbContext(DbContextOptions<AppDbContext> options)
			: base(options)
		{
		}

		public DbSet<PostEntity> Posts { get; set; }
		public DbSet<PostImageEntity> PostImages { get; set; }
		public DbSet<NetworkStateEntity> NetworkStates { get; set; }

	}
}

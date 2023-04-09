using Microsoft.EntityFrameworkCore;
using MinimalAPIs_7.Entities;

namespace MinimalAPIs_7
{
    public class MyDbContext : DbContext
    {
        public MyDbContext(DbContextOptions<MyDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<City> Cities { get; set; }
        public DbSet<Trip> Trips { get; set; }
    }

}

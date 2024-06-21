using Microsoft.EntityFrameworkCore;
using minimalAPI.Entities;

namespace minimalAPI.Context
{
    public class ApplicationDbContext(DbContextOptions options) : DbContext(options)
    {
        public DbSet<Employee> Employees { get; set; }
    }
}

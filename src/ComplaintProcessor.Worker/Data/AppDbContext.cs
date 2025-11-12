using Microsoft.EntityFrameworkCore;

namespace ComplaintProcessor.Worker.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Complaint> Complaints { get; set; } = null!;
}

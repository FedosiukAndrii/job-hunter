using Microsoft.EntityFrameworkCore;

namespace JobHunter.Infrastructure.Persistence;

public sealed class JobHunterDbContext(DbContextOptions<JobHunterDbContext> options)
    : DbContext(options);

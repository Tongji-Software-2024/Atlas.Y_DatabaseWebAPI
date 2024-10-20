using DatabaseWebAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace DatabaseWebAPI.Data;

public class OracleDbContext(DbContextOptions<OracleDbContext> options) : DbContext(options)
{
    public DbSet<Linker> LinkerSet { get; set; }
    public DbSet<SignalPeptide> SignalPeptideSet { get; set; }
    // public DbSet<QueryLog> QueryLogSet { get; set; }
    // public DbSet<DynamicQueryLog> DynamicQueryLogSet { get; set; }
}
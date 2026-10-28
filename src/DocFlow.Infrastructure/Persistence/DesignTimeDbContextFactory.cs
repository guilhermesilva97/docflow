using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DocFlow.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DocFlowDbContext>
{
    public DocFlowDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<DocFlowDbContext>()
            .UseSqlServer("Server=localhost;Database=DocFlow;Trusted_Connection=False;TrustServerCertificate=True")
            .Options);
}

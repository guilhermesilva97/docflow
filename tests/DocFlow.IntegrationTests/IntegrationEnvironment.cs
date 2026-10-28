using DocFlow.Domain;

namespace DocFlow.IntegrationTests;

public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (!IntegrationEnvironment.Enabled)
        {
            Skip = "Defina DOCFLOW_SQL e DOCFLOW_RABBIT para rodar os testes de integração.";
        }
    }
}

public static class IntegrationEnvironment
{
    public static string? SqlServer => Environment.GetEnvironmentVariable("DOCFLOW_SQL");
    public static string? RabbitHost => Environment.GetEnvironmentVariable("DOCFLOW_RABBIT");
    public static bool Enabled => SqlServer is not null && RabbitHost is not null;

    public static string ConnectionString(string database) =>
        $"Server={SqlServer};Database={database};User Id=sa;Password=DocFlow!2026;TrustServerCertificate=True";

    public static InvoiceData Invoice(string number) => new(
        "Aurora Tecnologia Ltda",
        "11222333000181",
        "Lumen Logística Ltda",
        null,
        number,
        new DateOnly(2026, 6, 1),
        null,
        [new InvoiceItem("Consultoria", 2, 100m, 200m)],
        200m);
}

using Microsoft.Data.SqlClient;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

// Strip out default blocking console logs to maximize Kestrel throughput
builder.Logging.ClearProviders();

// Register the memory cache service
builder.Services.AddMemoryCache();

const string ConnectionString = @"Server=(localdb)\MSSQLLocalDB;Database=BiCorePoC;Integrated Security=True;TrustServerCertificate=True;";

var app = builder.Build();

app.MapGet("/api/sales/summary", async (string? region, IMemoryCache cache) =>
{
    if (string.IsNullOrWhiteSpace(region))
    {
        return Results.BadRequest(new { Message = "Region query parameter is required." });
    }

    var cacheKey = $"SalesSummary_{region.Trim().ToUpperInvariant()}";

    // Step 1: Check cache.TryGetValue. If true, return immediately.
    if (cache.TryGetValue(cacheKey, out SalesSummary? summary))
    {
        return Results.Ok(summary);
    }

    // Step 2: Get or add a SemaphoreSlim(1, 1) for the specific region from a static ConcurrentDictionary.
    var semaphore = LockStore.Semaphores.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));

    // Step 3: await semaphore.WaitAsync().
    await semaphore.WaitAsync();

    try
    {
        // Step 4: Check cache.TryGetValue AGAIN (crucial for threads that were waiting in line).
        if (!cache.TryGetValue(cacheKey, out summary))
        {
            // Step 5: If still not in cache, execute the Dapper query, set the cache (60 seconds)
            using var connection = new SqlConnection(ConnectionString);
            
            const string sql = @"
                SELECT 
                    SUM(Amount) AS TotalSales, 
                    AVG(Amount) AS AverageSale, 
                    COUNT(Id) AS TransactionCount 
                FROM SalesTransactions 
                WHERE Region = @Region";

            summary = await connection.QuerySingleOrDefaultAsync<SalesSummary>(sql, new { Region = region });

            // Set cache expiration to 60 seconds
            cache.Set(cacheKey, summary, TimeSpan.FromSeconds(60));
        }
    }
    finally
    {
        // Release the semaphore in a finally block
        semaphore.Release();
    }

    return summary is null || summary.TransactionCount == 0 
        ? Results.NotFound(new { Message = $"No transaction data found for region: {region}" }) 
        : Results.Ok(summary);
});

app.Run("http://localhost:5000");

// Static store for our region-specific semaphores to implement Double-Check Locking
public static class LockStore
{
    public static readonly ConcurrentDictionary<string, SemaphoreSlim> Semaphores = new();
}

public record SalesSummary(decimal? TotalSales, decimal? AverageSale, int TransactionCount);
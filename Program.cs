using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using minimalAPI.Context;
using minimalAPI.Entities;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container. (Services)
builder.Services.AddDbContext<ApplicationDbContext>(opt =>
{
    opt.UseSqlServer("name=Default");
    opt.UseLoggerFactory(LoggerFactory.Create(builder =>
    {
        builder.AddFilter((category, level) => category == DbLoggerCategory.Database.Command.Name && level == LogLevel.Information).AddConsole();
    }));
});

//builder.Services.AddOutputCache();
builder.Services.AddStackExchangeRedisOutputCache(opt => opt.Configuration = builder.Configuration.GetConnectionString("redis"));

var app = builder.Build();

// Configure the HTTP request pipeline. (middleware)

app.UseHttpsRedirection();

//remove age header
app.Use(async (context, next) =>
{
    context.Response.OnStarting(state =>
    {
        var httpContext = (HttpContext)state;
        httpContext.Response.Headers.Remove("Age");
        return Task.CompletedTask;
    }, context);

    await next.Invoke(context);
});

app.UseOutputCache();

app.MapGet("/employees", async (ApplicationDbContext contextDb) =>
{
    var employees = await contextDb.Employees.ToListAsync();
    return employees;
}).CacheOutput(c => c.Expire(TimeSpan.FromSeconds(100)).Tag("employee-get"));

app.MapGet("/employees/{id:int}", async Task<Results<NotFound, Ok<Employee>>> (int id, ApplicationDbContext contextDb) =>
{
    var employee = await contextDb.Employees.FirstOrDefaultAsync(e => e.Id.Equals(id));

    if (employee is null)
        return TypedResults.NotFound();

    return TypedResults.Ok(employee);
}).WithName("GetEmployee");

app.MapPost("/employees", async (Employee employee, ApplicationDbContext contextDb, IOutputCacheStore outputCacheStore) =>
{
    contextDb.Add(employee);
    await contextDb.SaveChangesAsync();
    await outputCacheStore.EvictByTagAsync("employee-get", default);

    return TypedResults.CreatedAtRoute(employee, "GetEmployee", new { id = employee.Id });
});

app.MapPut("/employees/{id:int}", async Task<Results<BadRequest<string>, NotFound, NoContent>> (int id, Employee employee, ApplicationDbContext contextDb, IOutputCacheStore outputCacheStore) =>
{
    if (id != employee.Id)
        return TypedResults.BadRequest("the ids do not match");

    var exists = await contextDb.Employees.AnyAsync(e => e.Id == employee.Id);
    if (!exists)
        return TypedResults.NotFound();

    contextDb.Update(employee);
    await contextDb.SaveChangesAsync();
    await outputCacheStore.EvictByTagAsync("employee-get", default);

    return TypedResults.NoContent();
});

app.MapDelete("/employees/{id:int}", async Task<Results<NotFound, NoContent>> (int id, ApplicationDbContext contextDb, IOutputCacheStore outputCacheStore) =>
{
    var rowsDeleted = await contextDb.Employees.Where(e => e.Id == id).ExecuteDeleteAsync();

    if (rowsDeleted == 0)
        return TypedResults.NotFound();

    await outputCacheStore.EvictByTagAsync("employee-get", default);

    return TypedResults.NoContent();
});


var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 25).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
});

var message = builder.Configuration.GetValue<string>("message");
app.MapGet("/message", () => message);

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

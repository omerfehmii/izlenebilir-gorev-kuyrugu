using System.Diagnostics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using AIService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Add AI Services
builder.Services.AddScoped<ITaskPredictionService, TaskPredictionService>();

// Add OpenTelemetry
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("AIService.Prediction")
        .AddSource("AIService.API")
        .ConfigureResource(resource => resource
            .AddService("AIService", "1.0.0"))
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri("http://localhost:4317");
        }));

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() 
    { 
        Title = "AI Service API", 
        Version = "v1",
        Description = "Task Queue AI Optimization Service - ML-powered task prediction and optimization"
    });
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add health checks
builder.Services.AddHealthChecks();

// Logging configuration
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "AI Service API v1");
        c.RoutePrefix = string.Empty; // Swagger UI at root
    });
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthorization();

// Add health check endpoint
app.MapHealthChecks("/health");

app.MapControllers();

// Add startup logging
app.Logger.LogInformation("AI Service başlatılıyor...");
app.Logger.LogInformation("OpenTelemetry endpoint: http://localhost:4317");
app.Logger.LogInformation("Swagger UI: {BaseUrl}", app.Environment.IsDevelopment() ? "https://localhost:7001" : "Production URL");

app.Run();

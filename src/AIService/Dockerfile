# AI Service Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src

# Copy project files
COPY ["AIService/AIService.csproj", "AIService/"]
COPY ["TaskQueue.Shared/TaskQueue.Shared.csproj", "TaskQueue.Shared/"]

# Restore dependencies
RUN dotnet restore "AIService/AIService.csproj"

# Copy source code
COPY . .

# Build the application
WORKDIR "/src/AIService"
RUN dotnet build "AIService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AIService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy the published app
COPY --from=publish /app/publish .

# Set environment variables
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:80

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost/health || exit 1

ENTRYPOINT ["dotnet", "AIService.dll"]

# Consumer Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
# Copy project files for restore (context is ./src)
COPY ["Consumer/Consumer.csproj", "Consumer/"]
COPY ["TaskQueue.Shared/TaskQueue.Shared.csproj", "TaskQueue.Shared/"]
RUN dotnet restore "Consumer/Consumer.csproj"
# Copy the rest of the source
COPY . .
WORKDIR "/src/Consumer"
RUN dotnet build "Consumer.csproj" -c Release -o /app/build

FROM build AS publish
WORKDIR "/src/Consumer"
RUN dotnet publish "Consumer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Consumer.dll"] 
# Producer Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
# Copy project files for restore (context is ./src)
COPY ["Producer/Producer.csproj", "Producer/"]
COPY ["TaskQueue.Shared/TaskQueue.Shared.csproj", "TaskQueue.Shared/"]
RUN dotnet restore "Producer/Producer.csproj"
# Copy the rest of the source
COPY . .
WORKDIR "/src/Producer"
RUN dotnet build "Producer.csproj" -c Release -o /app/build

FROM build AS publish
WORKDIR "/src/Producer"
RUN dotnet publish "Producer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Producer.dll"] 
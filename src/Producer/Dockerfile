# Producer Dockerfile
FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["Producer.csproj", "."]
RUN dotnet restore "Producer.csproj"
COPY . .
WORKDIR "/src"
RUN dotnet build "Producer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Producer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Producer.dll"] 
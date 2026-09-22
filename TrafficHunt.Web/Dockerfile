# See https://learn.microsoft.com/dotnet/core/docker/build-container
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Restore app-local project references first for layer caching.
COPY ["TrafficHunt.Web/TrafficHunt.Web.csproj", "TrafficHunt.Web/"]
COPY ["TrafficHunt.Application/TrafficHunt.Application.csproj", "TrafficHunt.Application/"]
COPY ["TrafficHunt.Domain/TrafficHunt.Domain.csproj", "TrafficHunt.Domain/"]
COPY ["TrafficHunt.Infrastructure/TrafficHunt.Infrastructure.csproj", "TrafficHunt.Infrastructure/"]
RUN dotnet restore "TrafficHunt.Web/TrafficHunt.Web.csproj"

COPY . .
WORKDIR "/src/TrafficHunt.Web"
RUN dotnet publish "TrafficHunt.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p App_Data
ENTRYPOINT ["dotnet", "TrafficHunt.Web.dll"]

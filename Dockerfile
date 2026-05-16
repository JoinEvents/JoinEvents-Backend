# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["EventEase/EventEase.sln", "."]
COPY ["EventEase/EventEase/EventEase.Api.csproj", "EventEase/"]
COPY ["EventEase/EventEase.Application/EventEase.Application.csproj", "EventEase.Application/"]
COPY ["EventEase/EventEase.Core/EventEase.Core.csproj", "EventEase.Core/"]
COPY ["EventEase/EventEase.Infrastructure/EventEase.Infrastructure.csproj", "EventEase.Infrastructure/"]
COPY ["EventEase/EventEase.Tests/EventEase.Tests.csproj", "EventEase.Tests/"]

# Restore dependencies
RUN dotnet restore "EventEase.sln"

# Copy source code
COPY ["EventEase/", "."]

# Build the application
RUN dotnet build "EventEase/EventEase.Api.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "EventEase/EventEase.Api.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
EXPOSE 8080

# Copy published files
COPY --from=publish /app/publish .

# Create a non-root user for security
RUN useradd -m -u 1000 appuser && chown -R appuser:appuser /app
USER appuser

ENTRYPOINT ["dotnet", "EventEase.Api.dll"]

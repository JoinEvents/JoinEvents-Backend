# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy everything first to preserve directory structure for restore
COPY ["EventEase/", "EventEase/"]

# Restore dependencies
RUN dotnet restore "EventEase/EventEase.sln"

# The rest of the source is already there, but we can copy again if needed
# though COPY ["EventEase/", "EventEase/"] already covered it.

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

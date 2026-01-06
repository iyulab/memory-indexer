# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["tools/McpServer/McpServer.csproj", "tools/McpServer/"]
COPY ["src/MemoryIndexer/MemoryIndexer.csproj", "src/MemoryIndexer/"]
COPY ["src/MemoryIndexer.Sdk/MemoryIndexer.Sdk.csproj", "src/MemoryIndexer.Sdk/"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]

RUN dotnet restore "tools/McpServer/McpServer.csproj"

# Copy remaining source code
COPY . .

# Build the application
WORKDIR "/src/tools/McpServer"
RUN dotnet build "McpServer.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "McpServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Create non-root user
RUN groupadd -r memoryindexer && useradd -r -g memoryindexer memoryindexer

# Create directories for data and logs
RUN mkdir -p /app/data /app/logs && \
    chown -R memoryindexer:memoryindexer /app

# Copy published files
COPY --from=publish /app/publish .

# Install curl for health checks
RUN apt-get update && \
    apt-get install -y curl && \
    rm -rf /var/lib/apt/lists/*

# Switch to non-root user
USER memoryindexer

# Expose ports
EXPOSE 8080 8081

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8081/health/ready || exit 1

# Entry point
ENTRYPOINT ["dotnet", "McpServer.dll"]

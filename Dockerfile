# syntax=docker/dockerfile:1

# ---- Build stage --------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (layer-cached on project/solution changes only).
COPY PayLibre.slnx ./
COPY src/PayLibre.Domain/PayLibre.Domain.csproj                 src/PayLibre.Domain/
COPY src/PayLibre.Application/PayLibre.Application.csproj        src/PayLibre.Application/
COPY src/PayLibre.Infrastructure/PayLibre.Infrastructure.csproj src/PayLibre.Infrastructure/
COPY src/PayLibre.Api/PayLibre.Api.csproj                        src/PayLibre.Api/
RUN dotnet restore PayLibre.slnx

# Copy the rest and publish.
COPY . .
RUN dotnet publish src/PayLibre.Api/PayLibre.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime stage ------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Listen on 8080 (the default non-root port for the .NET runtime images).
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080 \
    LOG_DIRECTORY=/app/logs

# Create the log directory owned by the non-root "app" user (UID 1654, shipped
# in the .NET images). A named volume mounted here inherits this ownership, so
# the non-root process can write logs.
RUN mkdir -p /app/logs && chown -R app:app /app/logs

COPY --from=build --chown=app:app /app/publish .

# Run as the non-root user baked into the base image.
USER app

ENTRYPOINT ["dotnet", "PayLibre.Api.dll"]

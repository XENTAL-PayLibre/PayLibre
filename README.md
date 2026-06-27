# PayLibre Backend

ASP.NET Core (.NET 10) backend built on **Clean Architecture**. This is the
foundation onto which feature modules are added one at a time.

## Solution structure

```
PayLibre.slnx
src/
  PayLibre.Domain/          # Enterprise rules: entities, value objects. No dependencies.
  PayLibre.Application/      # Use cases, abstractions (ports), DTOs. Depends on Domain.
  PayLibre.Infrastructure/   # Adapters: persistence, external services. Depends on Application.
  PayLibre.Api/              # Presentation: controllers, Swagger, DI composition root.
```

Dependency rule (inward only): `Api → Infrastructure → Application → Domain`.
The Domain layer depends on nothing; the Api layer composes everything.

Each layer exposes a DI entry point used by the API's composition root:
- `PayLibre.Application` → `AddApplication()`
- `PayLibre.Infrastructure` → `AddInfrastructure(IConfiguration)`

New modules register their handlers/services inside these methods.

## Environment configuration

Runtime config lives in a `.env` file (git-ignored). Create it from the template:

```bash
cp .env.example .env
```

| Variable | Purpose |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | Development / Staging / Production |
| `ASPNETCORE_HTTP_PORTS` | Port the app listens on inside the container (8080) |
| `API_PORT` | Host port Docker publishes the API on |

## Running locally

```bash
dotnet run --project src/PayLibre.Api
```

Swagger UI: `https://localhost:<port>/swagger`.
Health endpoints: `GET /api/health` (controller) and `GET /health` (probe).

## Running with Docker

The whole stack is deployable from the single compose file:

```bash
cp .env.example .env   # first time only
docker compose up --build
```

The API is published on `http://localhost:${API_PORT}` (default 8081),
Swagger at `http://localhost:${API_PORT}/swagger`.

## Build

```bash
dotnet build PayLibre.slnx -c Release
```

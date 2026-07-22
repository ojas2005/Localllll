# Localll Backend

Event-driven microservices backend for the Localll platform (deliveries, medicines, cyber-cafe services, payments and wallets), built on **.NET 9**, **PostgreSQL**, **Redis**, **RabbitMQ** and a **YARP** API gateway.

## Architecture

```
Angular client
      │
      ▼
┌─────────────────────┐   JWT validation · rate limiting · CORS · compression
│  Gateway (YARP)     │   http://localhost:8080
└─────────┬───────────┘
          │ routes /api/v1/*
  ┌───────┴─────────────────────────────────────────────────────────┐
  │ Identity 5001 │ User 5002 │ Delivery 5003 │ Medicine 5004       │
  │ Payment 5005  │ Wallet 5006 │ Notification 5007 │ CyberCafe 5008│
  │ Partner 5009  │ Analytics 5010                                  │
  └───────┬─────────────────────────────────────────────────────────┘
          │ integration events (MassTransit)
      RabbitMQ ── consumed independently by each service
          │
   PostgreSQL (one DB per service) · Redis (OTP, cache, live location)
```

- **Database-per-service** — no service touches another service's tables; all cross-service communication is REST (via the gateway) or RabbitMQ events defined in `Localll.Contracts`.
- **Shared building blocks** — `Localll.Common` wires Serilog, JWT bearer auth, Swagger, FluentValidation, OpenTelemetry (OTLP), health checks, Redis and MassTransit identically for every service.
- **Event choreography** — e.g. a delivery order: `OrderCreatedEvent` → Partner service shows it in the available-orders feed → partner accepts → `OrderAcceptedEvent` → Delivery service assigns → OTP handed to customer → partner submits OTP → `DeliveryCompletedEvent` → Wallet credits the partner 80% → `WalletUpdatedEvent`; Notification and Analytics react to all of it.

## Services

| Service | Port | Responsibilities |
|---|---|---|
| Gateway | 8080 | Routing, JWT at the edge, rate limiting, CORS, gzip |
| Identity | 5001 | Register, login, JWT + rotating refresh tokens, OTP, password reset, lockout |
| User | 5002 | Profiles (auto-created from `UserRegisteredEvent`), addresses, reviews |
| Delivery | 5003 | Parcel/grocery orders, weight+distance pricing, OTP completion, tracking |
| Medicine | 5004 | Catalog search (cached), prescription orders, pharmacist approval workflow |
| Payment | 5005 | Idempotent payments (`Idempotency-Key` header), refunds |
| Wallet | 5006 | Append-only ledger, delivery earnings credits, withdrawals |
| Notification | 5007 | Email/SMS/Push/WhatsApp consumers (mock providers in dev) |
| CyberCafe | 5008 | Appointments, operator assignment, video session metadata, file metadata |
| Partner | 5009 | Pharmacy/delivery-partner onboarding, order feed with atomic claim, inventory, live location (Redis, 60s TTL) |
| Analytics | 5010 | Daily metric rollups from events, admin dashboards |

## Frontend (Angular 20)

The frontend lives in a separate repo (`Localll_Frontend`) — a standalone-component
Angular app with signals, Tailwind design tokens, GSAP motion, a lazy-loaded Three.js
hero (`@defer`, static fallback for weak devices), JWT auth with silent refresh, and
role-guarded dashboards (Customer / Delivery Partner / Pharmacy / Admin).

```bash
cd ../Localll_Frontend
npm install
npm start            # http://localhost:4200 — expects the gateway on :8080
```

Modules: landing page, auth (login / register + OTP / password reset), delivery
(quote calculator → order → payment → live tracking with OTP handover), medicines
(search, cart, prescription upload, pharmacist approval), cyber cafe booking,
become-a-partner, and the four dashboards.

## Running locally

Prerequisites: .NET 9 SDK (or newer), Docker, Node 20+.

```bash
# 1. Infrastructure (Postgres on host port 5433, Redis 6381, RabbitMQ 5674/15674)
docker compose up -d postgres redis rabbitmq

# 2. Any service (schema is created automatically on startup)
dotnet run --project src/Services/Identity/Localll.Identity.API
dotnet run --project src/Gateway/Localll.Gateway
# ...

# 3. Or the entire stack in containers
docker compose up -d --build
```

Swagger UI is at `/swagger` on each service when `ASPNETCORE_ENVIRONMENT=Development`. RabbitMQ management UI: http://localhost:15674 (`localll` / `localll_dev_password`).

> Ports differ from the image defaults because other projects on this machine already held 5432 and 5672/15672:
> Postgres → host **5433** (container still `postgres:5432` inside the compose network), RabbitMQ AMQP → host **5674**, RabbitMQ UI → host **15674**.
> The `guest`/`guest` default is intentionally not used — RabbitMQ restricts `guest` to loopback connections as seen by the *server*, which fails under Docker Desktop's port proxy. A real account (`localll`/`localll_dev_password`) is created via `RABBITMQ_DEFAULT_USER`/`RABBITMQ_DEFAULT_PASS` instead.

### Try it

```bash
# Register (also emits UserRegisteredEvent → profile + wallet created)
curl -X POST http://localhost:5001/api/v1/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"email":"me@example.com","phoneNumber":"+919876543210","fullName":"Me","password":"Passw0rd123"}'

# Login → access + refresh token
curl -X POST http://localhost:5001/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"me@example.com","password":"Passw0rd123"}'

# Delivery quote (authenticated)
curl -X POST http://localhost:5003/api/v1/deliveries/quote \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"orderType":"Parcel","distanceKm":5,"weightKg":2}'
```

## Tests

```bash
dotnet test Localll.slnx
```

Unit tests cover the delivery pricing engine and the wallet ledger invariants (insufficient balance, running balance, immutable entries).

## Repository layout

```
src/
  BuildingBlocks/
    Localll.SharedKernel/   # Entity, AggregateRoot, Result, PagedResult
    Localll.Contracts/      # integration events (the only cross-service contract)
    Localll.Common/         # platform wiring shared by every service
  Gateway/Localll.Gateway/  # YARP reverse proxy
  Services/<Name>/Localll.<Name>.API/
    Domain/    # entities + domain logic
    Data/      # DbContext per service
    Features/  # minimal-API endpoints
    Consumers/ # MassTransit event consumers
infrastructure/docker/      # shared Dockerfile + Postgres multi-DB init
tests/UnitTests/
.github/workflows/ci.yml    # build → test → per-service Docker images
```

## Security notes

- Passwords hashed with bcrypt; refresh tokens are rotated on use and revoked on password reset.
- Account lockout after 5 failed logins; password-reset endpoint never reveals whether an email exists.
- OTPs live only in Redis with a TTL; payments require an `Idempotency-Key`.
- Role-based authorization (`Customer`, `DeliveryPartner`, `PharmacyPartner`, `CyberCafeOperator`, `Admin`) enforced per endpoint; JWT validated at the gateway *and* in every service.
- **The JWT signing key and DB passwords in `appsettings.json`/compose are dev-only values** — inject real secrets via environment variables or a vault in production, and switch `EnsureCreated` to EF migrations in CI/CD.

## Not yet implemented (from the PRD roadmap)

Kubernetes/Helm manifests, Elasticsearch/Kibana log shipping, Prometheus/Grafana dashboards (OTLP export is already wired — point it at a collector), SignalR live tracking, object-storage presigned uploads (APIs currently accept storage URLs), saga/outbox patterns, and the AI features.

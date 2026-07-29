# Localll Backend — Architecture Diagrams

HLD · LLD · per-service ER diagrams | .NET 9 microservices · PostgreSQL (DB-per-service) · Redis · RabbitMQ (MassTransit) · YARP

## Contents

**HLD**
- [1. System architecture](#1-system-architecture-hld)
- [2. Event choreography](#2-event-choreography-async-message-flow-hld)

**LLD**
- [3. Service internal anatomy](#3-service-internal-anatomy-layers-lld)
- [4. Request lifecycle & middleware pipeline](#4-request-lifecycle--middleware-pipeline-lld)
- [5. Sequence: delivery completion & wallet settlement](#5-sequence--delivery-completion--wallet-settlement-lld)
- [6. Cross-service data ownership map](#6-cross-service-data-ownership-map-lld)

**ER**
- [Identity](#identity-db--er) · [User](#user-db--er) · [Delivery](#delivery-db--er) · [Medicine](#medicine-db--er) · [Payment](#payment-db--er) · [Wallet](#wallet-db--er) · [StoreOrders](#storeorders-db--er) · [Partner](#partner-db--er) · [CyberCafe](#cybercafe-db--er) · [Analytics](#analytics-db--er)

---

## 1. System architecture (HLD)

Every request enters through the YARP gateway. Each service owns its own PostgreSQL database and communicates with others only via RabbitMQ integration events. Redis is shared for OTPs, caching and live location.

```mermaid
flowchart TB
  SPA["Angular SPA :4200"]:::client
  GW["YARP Gateway :8080<br/>JWT · CORS · Rate limit (sliding window) · gzip"]:::gw

  subgraph SV["11 microservices — each with its own DB"]
    direction LR
    ID["Identity :5001<br/>auth · JWT · refresh · OTP"]:::svc
    US["User :5002<br/>profile · address · review"]:::svc
    DL["Delivery :5003<br/>orders · pricing · tracking"]:::svc
    MD["Medicine :5004<br/>catalog · rx approval"]:::svc
    PY["Payment :5005<br/>idempotent pay · refund"]:::svc
    WL["Wallet :5006<br/>ledger · withdrawals"]:::svc
    NT["Notification :5007<br/>email/sms/push/whatsapp"]:::svc
    CC["CyberCafe :5008<br/>appointments · files"]:::svc
    PT["Partner :5009<br/>onboarding · feed · location"]:::svc
    AN["Analytics :5010<br/>daily metric rollups"]:::svc
    ST["StoreOrders<br/>manual pay · settlement"]:::svc
  end

  MQ(["RabbitMQ<br/>(MassTransit event bus)"]):::infra
  PG[("PostgreSQL<br/>one database per service")]:::infra
  RD[("Redis<br/>OTP · cache · live location")]:::infra

  SPA -->|"HTTPS + Bearer JWT"| GW
  GW -->|"path routes /api/v1/*"| SV
  ID -.->|publish / consume| MQ
  ST -.->|publish / consume| MQ
  DL -.->|publish / consume| MQ
  PT -.->|publish / consume| MQ
  WL -.->|publish / consume| MQ
  AN -.->|consume| MQ
  NT -.->|consume| MQ
  ID --> PG
  ST --> PG
  ID --> RD
  DL --> RD
  PT --> RD

  classDef client fill:#ede9fe,stroke:#7c3aed,color:#4c1d95;
  classDef gw fill:#dbeafe,stroke:#2563eb,color:#1e3a8a,font-weight:bold;
  classDef svc fill:#ffffff,stroke:#93c5fd,color:#0f172a;
  classDef infra fill:#ecfeff,stroke:#0891b2,color:#155e75,font-weight:bold;
```

Solid arrows = synchronous HTTP through the gateway. Dashed arrows = asynchronous events on RabbitMQ. JWT is validated **at the gateway and again inside each service** (defense in depth).

---

## 2. Event choreography (async message flow) (HLD)

There is no central orchestrator. Each service reacts to events. This is the end-to-end happy path from registration to a paid, delivered order that settles a wallet.

```mermaid
flowchart LR
  R["Register (Customer)"] -->|UserRegisteredEvent| UP["User: create profile"]
  R -->|UserRegisteredEvent| WP["Wallet: create wallet"]
  R -->|UserRegisteredEvent| AP["Analytics: +NewUsers"]

  O["Create delivery order<br/>(AwaitingPayment)"] -->|OrderCreatedEvent| FEED["Partner: available-orders feed"]
  O -->|OrderCreatedEvent| AO["Analytics: +OrdersCreated"]

  PAY["Pay (Idempotency-Key)"] -->|PaymentCompletedEvent| RFP["Delivery: ReadyForPickup"]
  PAY -->|PaymentCompletedEvent| REV["Analytics: +Revenue"]

  ACC["Partner accepts<br/>(atomic UPDATE)"] -->|OrderAcceptedEvent| ASG["Delivery: Assigned"]

  DONE["Partner submits customer OTP"] -->|DeliveryCompletedEvent| CR["Wallet: credit partner 80%<br/>(idempotent by ReferenceId)"]
  DONE -->|DeliveryCompletedEvent| AD["Analytics: +DeliveriesCompleted"]
  CR -->|WalletUpdatedEvent| PE["Partner: TotalEarnings += "]
  CR -->|WalletUpdatedEvent| NTF["Notification: push"]

  classDef e fill:#ffffff,stroke:#94a3b8,color:#0f172a;
  class R,O,PAY,ACC,DONE,UP,WP,AP,FEED,AO,RFP,REV,ASG,CR,AD,PE,NTF e;
```

RabbitMQ delivery is **at-least-once**, so every consumer is idempotent (a state check or a `ReferenceId` guard makes replays a no-op).

---

## 3. Service internal anatomy (layers) (LLD)

Every service is built the same way. `AddPlatform()` from `Localll.Common` wires all cross-cutting concerns; the service only adds its DbContext, endpoints and consumers.

```mermaid
flowchart TB
  subgraph PROG["Program.cs — composition root"]
    AP["builder.AddPlatform(name, bus => AddConsumer<...>)"]
    DB1["AddDbContext<TContext> (Npgsql)"]
    MAP["app.UsePlatform() + Map...Endpoints()"]
    INIT["InitializeDatabaseAsync (EnsureCreated + retry)"]
  end

  subgraph PLAT["Localll.Common (shared platform)"]
    AUTH["JWT bearer auth"]
    MT["MassTransit + RabbitMQ (retry)"]
    REDIS["Redis (ICacheService, singleton)"]
    OTEL["OpenTelemetry + Serilog"]
    EX["GlobalExceptionHandler → ProblemDetails"]
    VAL["FluentValidation"]
  end

  subgraph FEAT["Features/ (Minimal API endpoints)"]
    EP["endpoint delegates<br/>+ request records + validators"]
  end
  subgraph DOM["Domain/ (entities + logic)"]
    ENT["AggregateRoot / Entity<br/>pure rules (pricing, ledger, settlement)"]
  end
  subgraph DATA["Data/ (persistence)"]
    CTX["DbContext (DbSet, OnModelCreating)"]
  end
  subgraph CONS["Consumers/ (event handlers)"]
    CN["IConsumer<TEvent>"]
  end

  PROG --> PLAT
  EP -->|"DI: DbContext, ICacheService, IPublishEndpoint"| DATA
  EP --> DOM
  CTX --> DOM
  CN --> DATA
  CN --> DOM
  EP -.publish.-> MT
  CN -.consume.-> MT

  classDef a fill:#eff6ff,stroke:#3b82f6,color:#1e3a8a;
  classDef b fill:#f0fdf4,stroke:#22c55e,color:#166534;
  classDef c fill:#fef9c3,stroke:#ca8a04,color:#713f12;
  class PROG,AP,DB1,MAP,INIT a;
  class PLAT,AUTH,MT,REDIS,OTEL,EX,VAL b;
  class FEAT,EP,DOM,ENT,DATA,CTX,CONS,CN c;
```

---

## 4. Request lifecycle & middleware pipeline (LLD)

The ordered pipeline fixed by `UsePlatform()`. Order matters: authentication must precede authorization, and the exception handler wraps everything downstream.

```mermaid
flowchart LR
  C["Client"] --> GW["Gateway: JWT · CORS · rate limit · gzip · route"]
  GW --> EXC["UseExceptionHandler"]
  EXC --> LOG["SerilogRequestLogging"]
  LOG --> AUTHN["UseAuthentication<br/>(populate HttpContext.User)"]
  AUTHN --> AUTHZ["UseAuthorization<br/>(RequireRole / policy)"]
  AUTHZ --> EP["Endpoint delegate"]
  EP --> V{"FluentValidation<br/>valid?"}
  V -- no --> P400["throw ValidationException<br/>→ 400 ProblemDetails"]
  V -- yes --> BL["Domain logic + DbContext"]
  BL --> SAVE["SaveChangesAsync"]
  SAVE --> PUB["publish integration event (optional)"]
  PUB --> RES["IResult (200/201/202/4xx)"]
  classDef m fill:#ffffff,stroke:#94a3b8,color:#0f172a;
  class C,GW,EXC,LOG,AUTHN,AUTHZ,EP,V,P400,BL,SAVE,PUB,RES m;
```

---

## 5. Sequence — delivery completion & wallet settlement (LLD)

The most instructive flow: OTP verification in Redis, a state transition in SQL, then an event that credits the partner exactly once.

```mermaid
sequenceDiagram
  autonumber
  actor P as Delivery Partner
  participant GW as Gateway
  participant D as Delivery svc
  participant RD as Redis
  participant DDB as Delivery DB
  participant MQ as RabbitMQ
  participant W as Wallet svc
  participant WDB as Wallet DB
  participant AN as Analytics
  participant NT as Notification

  P->>GW: POST /deliveries/{id}/complete { otp }
  GW->>D: forward (JWT valid, role=DeliveryPartner)
  D->>RD: GET delivery:otp:{id}
  RD-->>D: expected otp
  D->>D: compare otp; check status == PickedUp
  D->>DDB: status = Delivered
  D->>RD: DEL delivery:otp:{id}
  D->>MQ: publish DeliveryCompletedEvent (earning = 80%)
  D-->>P: 200 OK
  MQ-->>W: DeliveryCompletedEvent
  W->>WDB: exists ledger where ReferenceId == orderId ?
  alt already credited
    W-->>W: skip (idempotent)
  else first time
    W->>WDB: wallet.Credit(earning) + append LedgerEntry
    W->>MQ: publish WalletUpdatedEvent
  end
  MQ-->>AN: DeliveryCompletedEvent → +DeliveriesCompleted
  MQ-->>NT: WalletUpdatedEvent → push notification
```

---

## 6. Cross-service data ownership map (LLD)

There are **no foreign keys across services**. Services are linked only by IDs carried in events (dashed = logical reference via an ID, resolved through events/APIs, never a DB join).

```mermaid
flowchart LR
  subgraph Identity_DB
    AU["ApplicationUser"]
  end
  subgraph User_DB
    CP["CustomerProfile"]
  end
  subgraph Delivery_DB
    DO["DeliveryOrder"]
  end
  subgraph Medicine_DB
    MO["MedicineOrder"]
  end
  subgraph Payment_DB
    PMT["Payment"]
  end
  subgraph Wallet_DB
    WA["Wallet / LedgerEntry"]
  end
  subgraph StoreOrders_DB
    SO["StoreOrder / StoreWallet"]
  end
  subgraph Partner_DB
    PA["Partner / AvailableOrder"]
  end
  subgraph Analytics_DB
    DM["DailyMetric"]
  end

  AU -. "UserRegisteredEvent (UserId)" .-> CP
  AU -. UserId .-> WA
  AU -. UserId .-> PA
  DO -. "OrderCreatedEvent (OrderId)" .-> PA
  DO -. OrderId .-> PMT
  DO -. "DeliveryCompletedEvent (PartnerId)" .-> WA
  MO -. OrderId .-> PMT
  SO -. StoreId .-> SO
  DO -. events .-> DM
  PMT -. events .-> DM
```

> **Interview point:** "How do two services share data without a join?" → They don't join. The owning service publishes an event carrying the ID; the consumer keeps its own copy of just what it needs (e.g. Partner's `AvailableOrder` is a projection of Delivery's order). Sharing tables would create a distributed monolith.

---

## Identity DB — ER

`IdentityDbContext` · one user has many rotating refresh tokens.

```mermaid
erDiagram
  ApplicationUser ||--o{ RefreshToken : "has"
  ApplicationUser {
    Guid Id PK
    string Email UK
    string PhoneNumber UK
    string FullName
    string PasswordHash
    string Role
    enum ApprovalStatus
    string GoogleSubject
    int FailedLoginAttempts
    bool IsLocked
    bool EmailVerified
    datetime LastLoginAtUtc
  }
  RefreshToken {
    Guid Id PK
    Guid UserId FK
    string Token UK
    datetime ExpiresAtUtc
    datetime RevokedAtUtc
    string ReplacedByToken
  }
```

## User DB — ER

`UserDbContext` · profile has many addresses; reviews are standalone and indexed by (TargetType, TargetId).

```mermaid
erDiagram
  CustomerProfile ||--o{ Address : "has"
  CustomerProfile {
    Guid Id PK
    string Email UK
    string FullName
    string AvatarUrl
    string PreferredLanguage
  }
  Address {
    Guid Id PK
    Guid ProfileId FK
    string Label
    string Line1
    string City
    string State
    string PostalCode
    double Latitude
    double Longitude
    bool IsDefault
  }
  Review {
    Guid Id PK
    Guid CustomerId
    Guid OrderId
    string TargetType
    Guid TargetId
    int Rating
    string Comment
  }
```

## Delivery DB — ER

`DeliveryDbContext` · TrackingEvent references an order by `OrderId` (logical, no hard FK); GroceryItem is an independent seeded catalog.

```mermaid
erDiagram
  DeliveryOrder ||..o{ TrackingEvent : "OrderId (logical)"
  DeliveryOrder {
    Guid Id PK
    Guid CustomerId
    Guid PartnerId
    enum OrderType
    enum Status
    string PickupAddress
    string DropAddress
    double DistanceKm
    double WeightKg
    decimal Charge
    datetime DeliveredAtUtc
  }
  TrackingEvent {
    Guid Id PK
    Guid OrderId
    string Status
    double Latitude
    double Longitude
  }
  GroceryItem {
    Guid Id PK
    string Name
    string Category
    string UnitLabel
    decimal Price
    double WeightKg
    bool InStock
  }
```

## Medicine DB — ER

`MedicineDbContext` · an order has many line items; Medicine is the seeded catalog referenced by `MedicineId`.

```mermaid
erDiagram
  MedicineOrder ||--o{ MedicineOrderItem : "has"
  Medicine ||..o{ MedicineOrderItem : "MedicineId (logical)"
  Medicine {
    Guid Id PK
    string Name
    string GenericName
    string Manufacturer
    decimal Price
    bool RequiresPrescription
    string Category
  }
  MedicineOrder {
    Guid Id PK
    Guid CustomerId
    Guid PharmacyId
    enum Status
    string DeliveryAddress
    string PrescriptionUrl
    decimal TotalAmount
  }
  MedicineOrderItem {
    Guid Id PK
    Guid OrderId FK
    Guid MedicineId
    string MedicineName
    int Quantity
    decimal UnitPrice
  }
```

## Payment DB — ER

`PaymentDbContext` · single aggregate; `IdempotencyKey` is unique so retried requests can't double-charge.

```mermaid
erDiagram
  Payment {
    Guid Id PK
    Guid OrderId
    Guid CustomerId
    decimal Amount
    string Method
    enum Status
    string IdempotencyKey UK
    string ProviderReference
    datetime CompletedAtUtc
    datetime RefundedAtUtc
  }
```

## Wallet DB — ER

`WalletDbContext` · append-only ledger. Balance is a projection over immutable entries; each entry records the running `BalanceAfter`.

```mermaid
erDiagram
  Wallet ||--o{ LedgerEntry : "has"
  Wallet {
    Guid Id PK
    Guid OwnerId
    string OwnerType
    decimal Balance
  }
  LedgerEntry {
    Guid Id PK
    Guid WalletId FK
    enum Type
    decimal Amount
    decimal BalanceAfter
    string Reason
    Guid ReferenceId
  }
```

The `ReferenceId` (order/withdrawal id) is what makes crediting idempotent — one credit per source event, ever.

## StoreOrders DB — ER

`StoreOrdersDbContext` · the richest schema — catalog (Store → StoreProduct), orders (StoreOrder → StoreOrderItem), and a per-store settlement ledger (StoreWallet → StoreWalletEntry).

```mermaid
erDiagram
  Store ||--o{ StoreProduct : "sells"
  StoreOrder ||--o{ StoreOrderItem : "has"
  StoreWallet ||--o{ StoreWalletEntry : "has"
  Store ||..o{ StoreOrder : "StoreId (logical)"
  Store ||..|| StoreWallet : "StoreId (unique)"
  Store {
    Guid Id PK
    string Name
    string Address
    string City
    string UpiId
    Guid OwnerUserId
  }
  StoreProduct {
    Guid Id PK
    Guid StoreId FK
    string Name
    string Category
    decimal Price
    bool InStock
  }
  StoreOrder {
    Guid Id PK
    Guid CustomerId
    Guid StoreId
    enum Status
    enum PaymentMethod
    string PaymentScreenshotUrl
    decimal ItemsTotal
    decimal GrandTotal
    decimal PlatformCommission
    decimal StorePayout
    Guid DeliveryPartnerId
  }
  StoreOrderItem {
    Guid Id PK
    Guid OrderId FK
    Guid ProductId
    string ProductName
    int Quantity
    decimal UnitPrice
  }
  StoreWallet {
    Guid Id PK
    Guid StoreId UK
    decimal Balance
  }
  StoreWalletEntry {
    Guid Id PK
    Guid WalletId FK
    enum Type
    decimal Amount
    decimal BalanceAfter
    string Reason
    Guid OrderId
  }
```

## Partner DB — ER

`PartnerDbContext` · Partner (unique UserId), pharmacy Inventory, and AvailableOrder — a projection of open orders fed by `OrderCreatedEvent` and claimed atomically.

```mermaid
erDiagram
  Partner ||..o{ InventoryItem : "PharmacyId (logical)"
  Partner {
    Guid Id PK
    Guid UserId UK
    enum Type
    enum Status
    string Name
    string LicenseNumber
    string VehicleNumber
    string City
    bool IsOnline
    decimal TotalEarnings
  }
  InventoryItem {
    Guid Id PK
    Guid PharmacyId
    string MedicineName
    decimal Price
    int StockQuantity
  }
  AvailableOrder {
    Guid Id PK
    Guid OrderId UK
    string OrderType
    decimal Amount
    string PickupAddress
    string DropAddress
    Guid AcceptedByPartnerId
    datetime AcceptedAtUtc
  }
```

## CyberCafe DB — ER

`CyberCafeDbContext` · an appointment has many session files (metadata only — blobs live in object storage, referenced by URL).

```mermaid
erDiagram
  Appointment ||--o{ SessionFile : "has"
  Appointment {
    Guid Id PK
    Guid CustomerId
    string ServiceType
    datetime ScheduledAtUtc
    enum Status
    Guid OperatorId
    string VideoSessionId
    string Notes
  }
  SessionFile {
    Guid Id PK
    Guid AppointmentId FK
    string FileName
    string StorageUrl
    string ContentType
    long SizeBytes
  }
```

## Analytics DB — ER

`AnalyticsDbContext` · a single read-optimized rollup table, unique on (Date, Metric), incremented via a concurrency-safe upsert.

```mermaid
erDiagram
  DailyMetric {
    Guid Id PK
    date Date
    string Metric
    decimal Value
  }
```

**Notification service has no database** — it is stateless. It consumes `NotificationRequestedEvent` and dispatches through a Strategy of channels (Email/SMS/Push/WhatsApp), so it has no ER diagram.

---

*Generated from the actual `ojas2005/Localllll` source · diagrams render natively on GitHub via Mermaid.*

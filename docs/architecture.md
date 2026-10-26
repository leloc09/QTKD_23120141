# Thành phần chính của solution (Tuần 05)

```mermaid
flowchart LR
    Client[Swagger / Postman] -->|HTTP| Ctrl[Controllers<br/>CustomersController<br/>ContractsController]
    Ctrl --> Db[QtkdDbContext<br/>EF Core]
    Db -->|Npgsql| PG[(PostgreSQL<br/>qtkd)]
    Dom[Domain entities] -.-> Db
    Svc[Services - Tuần 06] -.-> Ctrl
```

```mermaid
erDiagram
    Customer ||--o{ Contract : "có"
    PriceList ||--|{ PriceItem : "gồm"
    Customer { guid Id PK  string Name  string Status }
    Contract { guid Id PK  string ContractNo  guid CustomerId FK  date StartDate  date EndDate  string Status }
    PriceList { guid Id PK  string Name  date EffectiveDate }
    PriceItem { guid PriceListId PK_FK  string ServiceCode PK  decimal UnitPrice }
```

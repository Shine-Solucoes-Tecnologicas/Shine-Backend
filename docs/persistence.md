# Persistência

## Banco

O ambiente local usa PostgreSQL via `docker compose up -d postgres`, com banco `shine` na porta `5432`.

## Migrations

As migrations são geradas e aplicadas pelo EF Core:

```bash
dotnet ef migrations add InitialPersistence --project platform/Core/src/Shine.Infrastructure --startup-project host/Shine.Api --output-dir Persistence/Migrations
dotnet ef database update --project platform/Core/src/Shine.Infrastructure --startup-project host/Shine.Api
```

## Seed

O seed deve ser determinístico e idempotente, executado apenas quando explicitamente habilitado. Como ainda não existem módulos funcionais ou entidades de negócio, a migration inicial não insere dados.

# Configuração

As configurações técnicas são carregadas por ambiente usando o sistema de configuração do ASP.NET Core.

## Obrigatória

`ConnectionStrings__ShineDb` deve ser fornecida por variável de ambiente ou por um provedor local de segredos. A aplicação falha na inicialização quando esse valor está ausente ou vazio.

Exemplo para desenvolvimento (não use uma senha real no repositório):

## Ambiente local com Docker

O PostgreSQL do projeto roda via Docker Compose na porta `5433` do Windows,
evitando conflito com uma instalaÃ§Ã£o local na porta `5432`.

```powershell
docker compose up -d
docker compose ps
docker exec shine-backend-postgres-1 pg_isready -U shine -d shine
$env:ConnectionStrings__ShineDb = 'Host=localhost;Port=5433;Database=shine;Username=shine;Password=shine'
dotnet ef database update --project src/Shine.Infrastructure --startup-project src/Shine.Api
dotnet run --project src/Shine.Api
```

O arquivo `.env.example` contém apenas nomes e valores ilustrativos. Segredos locais devem permanecer fora do controle de versão.

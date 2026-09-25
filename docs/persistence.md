# Persistência

O backend usa um PostgreSQL compartilhado por quatro `DbContext`, mantendo ownership e migrations independentes por módulo. O inventário, a ordem de aplicação, os comandos e os procedimentos de diagnóstico estão em [`runtime-and-operations.md`](runtime-and-operations.md).

No ambiente local, `docker compose up -d postgres` publica o banco `shine` em `localhost:5433`. A aplicação não executa migrations automaticamente ao iniciar.

Seeds devem ser determinísticos, idempotentes e executados somente quando explicitamente habilitados. Dados de autorização versionados pertencem ao Core; dados funcionais não devem ser introduzidos implicitamente por inicialização do host.

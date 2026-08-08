# Configuração

As configurações técnicas são carregadas por ambiente usando o sistema de configuração do ASP.NET Core.

## Obrigatória

`ConnectionStrings__ShineDb` deve ser fornecida por variável de ambiente ou por um provedor local de segredos. A aplicação falha na inicialização quando esse valor está ausente ou vazio.

Exemplo para desenvolvimento (não use uma senha real no repositório):

```powershell
$env:ConnectionStrings__ShineDb = 'Host=localhost;Port=5432;Database=shine;Username=shine;Password=change-me'
dotnet run --project src/Shine.Api
```

O arquivo `.env.example` contém apenas nomes e valores ilustrativos. Segredos locais devem permanecer fora do controle de versão.

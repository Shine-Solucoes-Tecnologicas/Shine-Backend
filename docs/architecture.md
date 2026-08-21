# Organização modular

O backend é organizado por fronteiras técnicas explícitas:

- `host`: processo executável e composition root HTTP.
- `platform/Core`: capacidades centrais atuais, incluindo identidade, autorização, planos e clientes.
- `platform/Shared`: building blocks mínimos sem dependências de projetos.
- `modules/<Module>/src`: Domain, Application e Infrastructure do módulo.
- `modules/<Module>/tests`: testes unitários e de integração pertencentes ao módulo.
- `tests/Shine.TestKit`: fixture de banco e utilitários técnicos compartilhados pelos testes.
- `tests/Shine.ArchitectureTests`: regras automatizadas de dependência.

## Regras de dependência

1. Módulos funcionais não referenciam outros módulos diretamente.
2. Domain e Application não referenciam Infrastructure.
3. Shared não referencia qualquer outro projeto.
4. O host compõe as infrastructures e não contém regras de domínio.
5. Integrações entre módulos usam contratos, eventos ou portas explícitas.
6. TestKit não contém regras de negócio nem expectativas específicas de módulos.

Clientes/CRM permanece temporariamente no Core porque ainda compartilha o mesmo contexto de persistência e autorização. Sua extração para `modules/Customers` deve ocorrer como mudança arquitetural própria, sem criar referência de Infrastructure entre módulos.

# ADR 0006: MassTransit 8.x e Docker como única dependência

**Status:** aceita

## Contexto

Ao escolher as versões, a última versão do MassTransit no NuGet era a 9.x, publicada sob licença comercial (`licenseUrl` para massient.com). A linha 8.x continua Apache 2.0 e já tem build para `net10.0`.

O projeto também precisa rodar em qualquer máquina sem instalar .NET, Node, SQL Server, RabbitMQ ou Kafka.

## Decisão

- **MassTransit 8.5.12**, Apache 2.0, fixado em `Directory.Packages.props` com *Central Package Management*. Subir para a 9 exige uma decisão de licença, não só técnica.
- **Um único Dockerfile multi-stage para todos os serviços .NET**, com `ARG PROJECT`:
  - `restore`: copia só os `.csproj` para aproveitar o cache;
  - `test`: build + `dotnet format --verify-no-changes` + `dotnet test`;
  - `publish` e `runtime`: imagem `aspnet:10.0` com usuário sem privilégios (`$APP_UID`).
- **Compose com profiles:** padrão (stack completa), `test`, `local-llm` e `evals`.
- O scaffold do Angular, o `package-lock.json` e as migrations do EF Core foram gerados dentro de containers (`node:22-alpine` e `dotnet/sdk:10.0`).

## Consequências

- `docker compose up` é tudo o que alguém precisa para ver o sistema funcionando.
- A pipeline roda exatamente os mesmos comandos que rodo localmente.
- A imagem de teste demora mais que um `dotnet test` local, mas a camada de restore fica em cache.

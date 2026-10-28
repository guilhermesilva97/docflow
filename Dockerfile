FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props DocFlow.slnx .editorconfig ./
COPY src/DocFlow.Domain/DocFlow.Domain.csproj src/DocFlow.Domain/
COPY src/DocFlow.Contracts/DocFlow.Contracts.csproj src/DocFlow.Contracts/
COPY src/DocFlow.Application/DocFlow.Application.csproj src/DocFlow.Application/
COPY src/DocFlow.Infrastructure/DocFlow.Infrastructure.csproj src/DocFlow.Infrastructure/
COPY src/DocFlow.Samples/DocFlow.Samples.csproj src/DocFlow.Samples/
COPY src/DocFlow.Api/DocFlow.Api.csproj src/DocFlow.Api/
COPY src/DocFlow.Extraction.Worker/DocFlow.Extraction.Worker.csproj src/DocFlow.Extraction.Worker/
COPY src/DocFlow.Validation.Worker/DocFlow.Validation.Worker.csproj src/DocFlow.Validation.Worker/
COPY src/DocFlow.Audit.Worker/DocFlow.Audit.Worker.csproj src/DocFlow.Audit.Worker/
COPY src/DocFlow.Evals/DocFlow.Evals.csproj src/DocFlow.Evals/
COPY tests/DocFlow.UnitTests/DocFlow.UnitTests.csproj tests/DocFlow.UnitTests/
COPY tests/DocFlow.IntegrationTests/DocFlow.IntegrationTests.csproj tests/DocFlow.IntegrationTests/
RUN dotnet restore DocFlow.slnx
COPY src ./src
COPY tests ./tests

FROM restore AS test
RUN dotnet build DocFlow.slnx --no-restore -c Release
CMD ["sh", "-c", "dotnet format DocFlow.slnx --verify-no-changes --no-restore && dotnet test DocFlow.slnx --no-build -c Release --logger 'console;verbosity=normal'"]

FROM restore AS publish
ARG PROJECT
RUN dotnet publish src/${PROJECT}/${PROJECT}.csproj --no-restore -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG PROJECT
ENV APP_DLL=${PROJECT}.dll
WORKDIR /app
COPY --from=publish /app .
USER $APP_UID
ENTRYPOINT ["sh", "-c", "exec dotnet $APP_DLL"]

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/SharedKernel/StatsTid.SharedKernel/StatsTid.SharedKernel.csproj src/SharedKernel/StatsTid.SharedKernel/
COPY src/Infrastructure/StatsTid.Infrastructure/StatsTid.Infrastructure.csproj src/Infrastructure/StatsTid.Infrastructure/
COPY src/Orchestrator/StatsTid.Orchestrator/StatsTid.Orchestrator.csproj src/Orchestrator/StatsTid.Orchestrator/
RUN dotnet restore src/Orchestrator/StatsTid.Orchestrator/StatsTid.Orchestrator.csproj
COPY src/SharedKernel/ src/SharedKernel/
COPY src/Infrastructure/ src/Infrastructure/
COPY src/Orchestrator/ src/Orchestrator/
RUN dotnet publish src/Orchestrator/StatsTid.Orchestrator/StatsTid.Orchestrator.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "StatsTid.Orchestrator.dll"]

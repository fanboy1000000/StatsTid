FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/SharedKernel/StatsTid.SharedKernel/StatsTid.SharedKernel.csproj src/SharedKernel/StatsTid.SharedKernel/
COPY src/Auth/StatsTid.Auth/StatsTid.Auth.csproj src/Auth/StatsTid.Auth/
COPY src/Infrastructure/StatsTid.Infrastructure/StatsTid.Infrastructure.csproj src/Infrastructure/StatsTid.Infrastructure/
COPY src/Integrations/StatsTid.Integrations.Payroll/StatsTid.Integrations.Payroll.csproj src/Integrations/StatsTid.Integrations.Payroll/
RUN dotnet restore src/Integrations/StatsTid.Integrations.Payroll/StatsTid.Integrations.Payroll.csproj
COPY src/SharedKernel/ src/SharedKernel/
COPY src/Auth/ src/Auth/
COPY src/Infrastructure/ src/Infrastructure/
COPY src/Integrations/StatsTid.Integrations.Payroll/ src/Integrations/StatsTid.Integrations.Payroll/
RUN dotnet publish src/Integrations/StatsTid.Integrations.Payroll/StatsTid.Integrations.Payroll.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "StatsTid.Integrations.Payroll.dll"]

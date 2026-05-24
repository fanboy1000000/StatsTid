FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/SharedKernel/StatsTid.SharedKernel/StatsTid.SharedKernel.csproj src/SharedKernel/StatsTid.SharedKernel/
COPY src/Auth/StatsTid.Auth/StatsTid.Auth.csproj src/Auth/StatsTid.Auth/
COPY src/Infrastructure/StatsTid.Infrastructure/StatsTid.Infrastructure.csproj src/Infrastructure/StatsTid.Infrastructure/
COPY src/Backend/StatsTid.Backend.Api/StatsTid.Backend.Api.csproj src/Backend/StatsTid.Backend.Api/
RUN dotnet restore src/Backend/StatsTid.Backend.Api/StatsTid.Backend.Api.csproj
COPY src/SharedKernel/ src/SharedKernel/
COPY src/Auth/ src/Auth/
COPY src/Infrastructure/ src/Infrastructure/
COPY src/Backend/ src/Backend/
RUN dotnet publish src/Backend/StatsTid.Backend.Api/StatsTid.Backend.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "StatsTid.Backend.Api.dll"]

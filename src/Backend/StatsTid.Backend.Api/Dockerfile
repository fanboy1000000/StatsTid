FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/SharedKernel/StatsTid.SharedKernel/StatsTid.SharedKernel.csproj src/SharedKernel/StatsTid.SharedKernel/
COPY src/Infrastructure/StatsTid.Infrastructure/StatsTid.Infrastructure.csproj src/Infrastructure/StatsTid.Infrastructure/
COPY src/Backend/StatsTid.Backend.Api/StatsTid.Backend.Api.csproj src/Backend/StatsTid.Backend.Api/
RUN dotnet restore src/Backend/StatsTid.Backend.Api/StatsTid.Backend.Api.csproj
COPY src/SharedKernel/ src/SharedKernel/
COPY src/Infrastructure/ src/Infrastructure/
COPY src/Backend/ src/Backend/
RUN dotnet publish src/Backend/StatsTid.Backend.Api/StatsTid.Backend.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "StatsTid.Backend.Api.dll"]

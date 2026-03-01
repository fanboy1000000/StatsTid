FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/SharedKernel/StatsTid.SharedKernel/StatsTid.SharedKernel.csproj src/SharedKernel/StatsTid.SharedKernel/
COPY src/RuleEngine/StatsTid.RuleEngine.Api/StatsTid.RuleEngine.Api.csproj src/RuleEngine/StatsTid.RuleEngine.Api/
RUN dotnet restore src/RuleEngine/StatsTid.RuleEngine.Api/StatsTid.RuleEngine.Api.csproj
COPY src/SharedKernel/ src/SharedKernel/
COPY src/RuleEngine/ src/RuleEngine/
RUN dotnet publish src/RuleEngine/StatsTid.RuleEngine.Api/StatsTid.RuleEngine.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "StatsTid.RuleEngine.Api.dll"]

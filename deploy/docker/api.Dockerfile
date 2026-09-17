FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json TradingPlatform.slnx ./
COPY src/ ./src/
RUN dotnet restore src/TradingPlatform.Api/TradingPlatform.Api.csproj
RUN dotnet publish src/TradingPlatform.Api/TradingPlatform.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5080
ENV Trading__LiveTradingEnabled=false
EXPOSE 5080
ENTRYPOINT ["dotnet", "TradingPlatform.Api.dll"]

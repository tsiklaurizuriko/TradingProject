FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json TradingPlatform.slnx ./
COPY src/ ./src/
RUN dotnet restore src/TradingPlatform.Workers/TradingPlatform.Workers.csproj
RUN dotnet publish src/TradingPlatform.Workers/TradingPlatform.Workers.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV Trading__LiveTradingEnabled=false
ENTRYPOINT ["dotnet", "TradingPlatform.Workers.dll"]

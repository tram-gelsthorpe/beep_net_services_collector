FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/WebsiteCollector.Worker/*.csproj ./
RUN dotnet restore
COPY src/WebsiteCollector.Worker/. ./
RUN dotnet publish -c Release -o /app/publish --no-restore
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "WebsiteCollector.Worker.dll"]

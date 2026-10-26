FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# global.json stays out: the image's SDK feature band may be lower than the local one.
COPY Directory.Build.props Directory.Packages.props CashFlow.slnx ./
COPY src/ src/
RUN dotnet publish src/Worker/CashFlow.Worker/CashFlow.Worker.csproj -c Release -o /app/publish

# aspnet, not runtime: integration event handlers live in each module's
# Presentation assembly, which references the ASP.NET Core shared framework.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "CashFlow.Worker.dll"]

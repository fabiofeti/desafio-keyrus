FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# global.json stays out: the image's SDK feature band may be lower than the local one.
COPY Directory.Build.props Directory.Packages.props CashFlow.slnx ./
COPY src/ src/
RUN dotnet publish src/Api/CashFlow.Api/CashFlow.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
# tzdata: the business date is computed in the America/Sao_Paulo time zone.
RUN apk add --no-cache tzdata
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "CashFlow.Api.dll"]

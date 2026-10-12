FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/AtmCashManagement.Api/AtmCashManagement.Api.csproj ./AtmCashManagement.Api.csproj
RUN dotnet restore ./AtmCashManagement.Api.csproj

COPY src/AtmCashManagement.Api/. ./
RUN dotnet publish ./AtmCashManagement.Api.csproj --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
ENTRYPOINT ["dotnet", "AtmCashManagement.Api.dll"]

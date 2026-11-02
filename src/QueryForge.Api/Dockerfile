FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/QueryForge.Api/QueryForge.Api.csproj src/QueryForge.Api/
RUN dotnet restore src/QueryForge.Api/QueryForge.Api.csproj
COPY src/QueryForge.Api/ src/QueryForge.Api/
RUN dotnet publish src/QueryForge.Api/QueryForge.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "QueryForge.Api.dll"]

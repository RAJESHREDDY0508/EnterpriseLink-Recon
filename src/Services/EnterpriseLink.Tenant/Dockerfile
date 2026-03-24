FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/Services/EnterpriseLink.Tenant/EnterpriseLink.Tenant.csproj", "src/Services/EnterpriseLink.Tenant/"]

RUN dotnet restore "src/Services/EnterpriseLink.Tenant/EnterpriseLink.Tenant.csproj"

COPY . .
WORKDIR "/src/src/Services/EnterpriseLink.Tenant"
RUN dotnet build "EnterpriseLink.Tenant.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "EnterpriseLink.Tenant.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EnterpriseLink.Tenant.dll"]

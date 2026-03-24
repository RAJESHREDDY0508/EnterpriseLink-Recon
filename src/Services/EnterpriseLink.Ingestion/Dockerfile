FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/Services/EnterpriseLink.Ingestion/EnterpriseLink.Ingestion.csproj", "src/Services/EnterpriseLink.Ingestion/"]
COPY ["src/Shared/EnterpriseLink.Shared.Contracts/EnterpriseLink.Shared.Contracts.csproj", "src/Shared/EnterpriseLink.Shared.Contracts/"]

RUN dotnet restore "src/Services/EnterpriseLink.Ingestion/EnterpriseLink.Ingestion.csproj"

COPY . .
WORKDIR "/src/src/Services/EnterpriseLink.Ingestion"
RUN dotnet build "EnterpriseLink.Ingestion.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "EnterpriseLink.Ingestion.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EnterpriseLink.Ingestion.dll"]

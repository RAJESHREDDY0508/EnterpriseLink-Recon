FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/Services/EnterpriseLink.Worker/EnterpriseLink.Worker.csproj", "src/Services/EnterpriseLink.Worker/"]
COPY ["src/Shared/EnterpriseLink.Shared.Contracts/EnterpriseLink.Shared.Contracts.csproj", "src/Shared/EnterpriseLink.Shared.Contracts/"]
COPY ["src/Shared/EnterpriseLink.Shared.Domain/EnterpriseLink.Shared.Domain.csproj", "src/Shared/EnterpriseLink.Shared.Domain/"]
COPY ["src/Shared/EnterpriseLink.Shared.Infrastructure/EnterpriseLink.Shared.Infrastructure.csproj", "src/Shared/EnterpriseLink.Shared.Infrastructure/"]

RUN dotnet restore "src/Services/EnterpriseLink.Worker/EnterpriseLink.Worker.csproj"

COPY . .
WORKDIR "/src/src/Services/EnterpriseLink.Worker"
RUN dotnet build "EnterpriseLink.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "EnterpriseLink.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EnterpriseLink.Worker.dll"]

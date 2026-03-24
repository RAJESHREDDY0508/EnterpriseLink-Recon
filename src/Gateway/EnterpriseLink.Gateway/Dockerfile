FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/Gateway/EnterpriseLink.Gateway/EnterpriseLink.Gateway.csproj", "src/Gateway/EnterpriseLink.Gateway/"]

RUN dotnet restore "src/Gateway/EnterpriseLink.Gateway/EnterpriseLink.Gateway.csproj"

COPY . .
WORKDIR "/src/src/Gateway/EnterpriseLink.Gateway"
RUN dotnet build "EnterpriseLink.Gateway.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "EnterpriseLink.Gateway.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EnterpriseLink.Gateway.dll"]

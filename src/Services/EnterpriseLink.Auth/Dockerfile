FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/Services/EnterpriseLink.Auth/EnterpriseLink.Auth.csproj", "src/Services/EnterpriseLink.Auth/"]

RUN dotnet restore "src/Services/EnterpriseLink.Auth/EnterpriseLink.Auth.csproj"

COPY . .
WORKDIR "/src/src/Services/EnterpriseLink.Auth"
RUN dotnet build "EnterpriseLink.Auth.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "EnterpriseLink.Auth.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EnterpriseLink.Auth.dll"]

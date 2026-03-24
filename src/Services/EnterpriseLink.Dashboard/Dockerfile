FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/Services/EnterpriseLink.Dashboard/EnterpriseLink.Dashboard.csproj", "src/Services/EnterpriseLink.Dashboard/"]

RUN dotnet restore "src/Services/EnterpriseLink.Dashboard/EnterpriseLink.Dashboard.csproj"

COPY . .
WORKDIR "/src/src/Services/EnterpriseLink.Dashboard"
RUN dotnet build "EnterpriseLink.Dashboard.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "EnterpriseLink.Dashboard.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EnterpriseLink.Dashboard.dll"]

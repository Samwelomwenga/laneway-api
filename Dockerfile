FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
ENV HUSKY=0
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json Laneway.Api.csproj ./
RUN dotnet restore Laneway.Api.csproj
COPY . .
ARG VERSION=0.0.0-pr
RUN dotnet publish Laneway.Api.csproj --no-restore -c Release -o /app/publish -p:UseAppHost=false -p:Version=$VERSION

FROM build AS migrations
RUN dotnet tool restore \
    && CONNECTIONSTRINGS__DEFAULTCONNECTION='Host=localhost;Database=laneway' \
       dotnet ef migrations bundle --project Laneway.Api.csproj --configuration Release --output /app/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:cae5b962bfb786606def03d250c6a645cc98839095c89e806e4bd8978a68115b
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=migrations /app/efbundle /app/efbundle
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Laneway.Api.dll"]

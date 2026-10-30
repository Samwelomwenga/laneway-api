FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
ENV HUSKY=0
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json Laneway.Api.csproj ./
RUN dotnet restore Laneway.Api.csproj
COPY . .
RUN dotnet publish Laneway.Api.csproj --no-restore -c Release -o /app/publish -p:UseAppHost=false

FROM build AS migrations
RUN dotnet tool restore \
    && CONNECTIONSTRINGS__DEFAULTCONNECTION='Host=localhost;Database=laneway' \
       dotnet ef migrations bundle --project Laneway.Api.csproj --configuration Release --output /app/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0.0-noble-chiseled-extra@sha256:64f42416803e32bee1f5d2d3eab5825581abd45b2e9c6f888fc873ff2c4cc378
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=migrations /app/efbundle /app/efbundle
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Laneway.Api.dll"]

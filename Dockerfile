FROM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e AS build
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

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:a9298ccc39c6258ae71228479e2f3f8d2ceb6639a9927262a4b0ff7d319a0e7e
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=migrations /app/efbundle /app/efbundle
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Laneway.Api.dll"]

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props ./
COPY src/Petty.Server.Domain/Petty.Server.Domain.csproj src/Petty.Server.Domain/
COPY src/Petty.Server.Application/Petty.Server.Application.csproj src/Petty.Server.Application/
COPY src/Petty.Server.Infrastructure/Petty.Server.Infrastructure.csproj src/Petty.Server.Infrastructure/
COPY src/Petty.Server.Api/Petty.Server.Api.csproj src/Petty.Server.Api/
RUN dotnet restore src/Petty.Server.Api/Petty.Server.Api.csproj
COPY src/Petty.Server.Domain/ src/Petty.Server.Domain/
COPY src/Petty.Server.Application/ src/Petty.Server.Application/
COPY src/Petty.Server.Infrastructure/ src/Petty.Server.Infrastructure/
COPY src/Petty.Server.Api/ src/Petty.Server.Api/
RUN dotnet publish src/Petty.Server.Api -c Release --no-restore -p:UseAppHost=false -p:DebugType=None -p:DebugSymbols=false -o /out/api
RUN dotnet tool install --tool-path /tools dotnet-ef --version 10.0.14
ARG TARGETARCH
RUN case "$TARGETARCH" in amd64) rid=linux-x64 ;; arm64) rid=linux-arm64 ;; *) exit 1 ;; esac \
    && /tools/dotnet-ef migrations bundle --project src/Petty.Server.Infrastructure --startup-project src/Petty.Server.Api \
       --configuration Release --target-runtime "$rid" --output /out/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/.net
COPY --from=build /out/api/ ./
COPY --from=build --chmod=755 /out/efbundle ./efbundle
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Petty.Server.Api.dll"]

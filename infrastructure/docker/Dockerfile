# Shared Dockerfile for every Localll service.
# Build from repo root:
#   docker build -f infrastructure/docker/Dockerfile \
#     --build-arg SERVICE_PROJECT=src/Services/Identity/Localll.Identity.API \
#     --build-arg SERVICE_DLL=Localll.Identity.API.dll -t localll/identity .
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG SERVICE_PROJECT
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish ${SERVICE_PROJECT} -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
ARG SERVICE_DLL
ENV SERVICE_DLL=${SERVICE_DLL} \
    ASPNETCORE_URLS=http://+:8080 \
    Urls=http://+:8080
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["/bin/sh", "-c", "dotnet ${SERVICE_DLL}"]

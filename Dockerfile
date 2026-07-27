# Stage 1: Build Frontend
FROM node:22-alpine AS frontend-build
WORKDIR /app/frontend
COPY frontend/package.json frontend/package-lock.json* ./
RUN npm ci || npm install
COPY frontend/ ./
RUN npm run build

# Stage 2: Build Backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /app/backend

# Copy solution and project files first for layer caching
COPY backend/SchulerPark.sln ./
COPY backend/SchulerPark.Api/SchulerPark.Api.csproj ./SchulerPark.Api/
COPY backend/SchulerPark.Core/SchulerPark.Core.csproj ./SchulerPark.Core/
COPY backend/SchulerPark.Infrastructure/SchulerPark.Infrastructure.csproj ./SchulerPark.Infrastructure/
COPY backend/SchulerPark.Tests/SchulerPark.Tests.csproj ./SchulerPark.Tests/
RUN dotnet restore

# Copy everything else and publish
COPY backend/ ./
RUN dotnet publish SchulerPark.Api -c Release -o /app/publish --no-restore

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Copy published backend
COPY --from=backend-build /app/publish ./

# Copy frontend build output to wwwroot
COPY --from=frontend-build /app/frontend/dist ./wwwroot/

# Run as the non-root `app` user (built into the .NET base images, UID 1654):
# an RCE in the app must not get root inside the container. /app stays
# root-owned (read-only for the app); /keys holds the DataProtection key ring
# and must be writable. NOTE for existing deployments: a dataprotection_keys
# volume created by an older root image needs a one-time
#   docker run --rm -v <project>_dataprotection_keys:/keys alpine chown -R 1654:1654 /keys
RUN mkdir -p /keys /bootstrap && chown app:app /keys /bootstrap

ENV ASPNETCORE_URLS=http://+:8080
# Writable location for the first-boot admin.yml (see BootstrapAdmin); /app is
# deliberately not writable by the app user.
ENV BOOTSTRAP_ADMIN_DIR=/bootstrap
EXPOSE 8080

USER app

ENTRYPOINT ["dotnet", "SchulerPark.Api.dll"]

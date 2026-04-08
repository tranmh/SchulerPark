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

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SchulerPark.Api.dll"]

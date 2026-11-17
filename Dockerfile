# syntax=docker/dockerfile:1
# The API, as one image for both jobs (#122):
#   docker run <image>            serves on :8080
#   docker run <image> migrate    applies migrations, then exits
# Built from the repo root: docker build -t personalfinance-api .

# ── Build ─────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so the restore layer is cached until dependencies change.
COPY Directory.Build.props ./
COPY src/PersonalFinance.Shared/PersonalFinance.Shared.csproj src/PersonalFinance.Shared/
COPY src/PersonalFinance.Api/PersonalFinance.Api.csproj src/PersonalFinance.Api/
RUN dotnet restore src/PersonalFinance.Api/PersonalFinance.Api.csproj

COPY src/PersonalFinance.Shared/ src/PersonalFinance.Shared/
COPY src/PersonalFinance.Api/ src/PersonalFinance.Api/
RUN dotnet publish src/PersonalFinance.Api/PersonalFinance.Api.csproj \
      --configuration Release --no-restore --output /app /p:UseAppHost=false

# ── Runtime ───────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# The base image already listens on 8080 (ASPNETCORE_HTTP_PORTS).
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Data Protection's key ring (only needed to read pre-#120 Plaid tokens),
# created here owned by the app user so a volume mounted on it is writable.
RUN mkdir -p /var/lib/personalfinance/keys && chown app:app /var/lib/personalfinance/keys
ENV DataProtection__KeysPath=/var/lib/personalfinance/keys

# The image's built-in unprivileged user (uid 1654), never root.
USER app

# No curl in the base image; bash's /dev/tcp is enough for a local probe.
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=3 \
  CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /health/ready HTTP/1.0\r\nHost: localhost\r\n\r\n" >&3 && grep -q " 200 " <&3'

ENTRYPOINT ["dotnet", "PersonalFinance.Api.dll"]

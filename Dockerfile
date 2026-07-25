# ============================================================================
# Dockerfile — builds the ROCloud API into a Linux container.
#
# Render has no built-in .NET runtime, so this file IS the build instructions:
# Render reads it, runs it, and ships the result. Nothing here needs to be run
# on your PC — see docs/deploy-render.md.
# ============================================================================

# ─── Stage 1: compile the app (throw-away, never shipped) ────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy ONLY the project files first and restore. Docker caches this layer, so a
# deploy that changed nothing but C# code skips the slow NuGet download.
COPY src/ROCloud.Domain/ROCloud.Domain.csproj                 src/ROCloud.Domain/
COPY src/ROCloud.Application/ROCloud.Application.csproj       src/ROCloud.Application/
COPY src/ROCloud.Infrastructure/ROCloud.Infrastructure.csproj src/ROCloud.Infrastructure/
COPY src/ROCloud.API/ROCloud.API.csproj                       src/ROCloud.API/
RUN dotnet restore src/ROCloud.API/ROCloud.API.csproj

COPY src/ src/
RUN dotnet publish src/ROCloud.API/ROCloud.API.csproj -c Release -o /app --no-restore

# ─── Stage 2: the image that actually runs ───────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# fonts  — QuestPDF draws the invoice PDFs through the OS font system. This base
#          image ships no Indian fonts, so without these your Hindi and Gujarati
#          invoices come out as rows of empty boxes (□□□□).
# tzdata — App:TimeZone is Asia/Kolkata; without the timezone database .NET
#          cannot resolve that name and the app fails at startup.
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      fontconfig fonts-noto-core fonts-noto-ui-core fonts-indic tzdata \
 && fc-cache -f \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# Render sends public traffic to whatever port the container listens on, and
# defaults to 10000. Do NOT also set ASPNETCORE_URLS in the Render dashboard.
ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000

# Deliberately root. Render mounts the persistent disk owned by root; a non-root
# process could not write delivery photos or Data Protection keys to it.
USER root

ENTRYPOINT ["dotnet", "ROCloud.API.dll"]

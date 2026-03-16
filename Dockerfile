# =============================================================
# Multi-target Dockerfile for FabCopilot .NET microservices
# Usage:  docker build --target <service> .
# =============================================================

# --- Shared build stage ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props FabCopilot.sln global.json ./
COPY src/ src/
# Remove test project references from sln (tests excluded by .dockerignore)
RUN dotnet sln FabCopilot.sln remove \
      tests/FabCopilot.RagPipeline.Tests/FabCopilot.RagPipeline.Tests.csproj \
      tests/FabCopilot.Integration.Tests/FabCopilot.Integration.Tests.csproj \
      tests/FabCopilot.ServiceDashboard.Tests/FabCopilot.ServiceDashboard.Tests.csproj \
      2>/dev/null; true
RUN dotnet restore FabCopilot.sln

# --- Publish each service ---
FROM build AS publish-chatgateway
RUN dotnet publish src/Services/FabCopilot.ChatGateway -c Release -o /publish --no-restore

FROM build AS publish-llmservice
RUN dotnet publish src/Services/FabCopilot.LlmService -c Release -o /publish --no-restore

FROM build AS publish-knowledgeservice
RUN dotnet publish src/Services/FabCopilot.KnowledgeService -c Release -o /publish --no-restore

FROM build AS publish-ragservice
RUN dotnet publish src/Services/FabCopilot.RagService -c Release -o /publish --no-restore

FROM build AS publish-alarmcopilot
RUN dotnet publish src/Services/FabCopilot.AlarmCopilot -c Release -o /publish --no-restore

FROM build AS publish-mcplogserver
RUN dotnet publish src/Services/FabCopilot.McpLogServer -c Release -o /publish --no-restore

FROM build AS publish-rcaagent
RUN dotnet publish src/Services/FabCopilot.RcaAgent -c Release -o /publish --no-restore

# --- Voice Panel (React MFE) build stage ---
FROM node:20-alpine AS voice-panel-build
WORKDIR /app/voice-panel
COPY src/Client/voice-panel/package*.json ./
RUN npm ci --ignore-scripts
COPY src/Client/voice-panel/ ./
# Vite outDir resolves to /app/FabCopilot.WebClient/wwwroot/voice-panel/
RUN npm run build

FROM build AS publish-webclient
# Copy React Voice Panel build output to wwwroot before .NET publish
COPY --from=voice-panel-build /app/FabCopilot.WebClient/wwwroot/voice-panel/ src/Client/FabCopilot.WebClient/wwwroot/voice-panel/
RUN dotnet publish src/Client/FabCopilot.WebClient -c Release -o /publish --no-restore

FROM build AS publish-dashboard
RUN dotnet publish src/Client/FabCopilot.ServiceDashboard -c Release -o /publish --no-restore

# --- Runtime images ---
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS chatgateway
WORKDIR /app
COPY --from=publish-chatgateway /publish .
EXPOSE 5000
ENTRYPOINT ["dotnet", "FabCopilot.ChatGateway.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS llmservice
WORKDIR /app
COPY --from=publish-llmservice /publish .
EXPOSE 5001
ENTRYPOINT ["dotnet", "FabCopilot.LlmService.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS knowledgeservice
WORKDIR /app
COPY --from=publish-knowledgeservice /publish .
EXPOSE 5002
ENTRYPOINT ["dotnet", "FabCopilot.KnowledgeService.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS ragservice
WORKDIR /app
COPY --from=publish-ragservice /publish .
EXPOSE 5003
ENTRYPOINT ["dotnet", "FabCopilot.RagService.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS alarmcopilot
WORKDIR /app
COPY --from=publish-alarmcopilot /publish .
EXPOSE 5004
ENTRYPOINT ["dotnet", "FabCopilot.AlarmCopilot.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS mcplogserver
WORKDIR /app
COPY --from=publish-mcplogserver /publish .
EXPOSE 5005
ENTRYPOINT ["dotnet", "FabCopilot.McpLogServer.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS rcaagent
WORKDIR /app
COPY --from=publish-rcaagent /publish .
EXPOSE 5006
ENTRYPOINT ["dotnet", "FabCopilot.RcaAgent.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS webclient
WORKDIR /app
COPY --from=publish-webclient /publish .
EXPOSE 5010
ENTRYPOINT ["dotnet", "FabCopilot.WebClient.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS dashboard
WORKDIR /app
COPY --from=publish-dashboard /publish .
EXPOSE 5020
ENTRYPOINT ["dotnet", "FabCopilot.ServiceDashboard.dll"]

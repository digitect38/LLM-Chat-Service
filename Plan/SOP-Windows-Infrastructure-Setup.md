# FabCopilot Windows 환경 인프라 구축 SOP

> **버전**: 1.3
> **작성일**: 2026-02-20
> **대상**: Windows 10/11 환경에서 FabCopilot 시스템 개발/운영

---

## 목차

1. [시스템 요구사항](#1-시스템-요구사항)
2. [아키텍처 개요](#2-아키텍처-개요)
3. [인프라 구성요소 설치](#3-인프라-구성요소-설치)
   - 3.1 [방법 A: Docker Desktop (Linux 컨테이너)](#31-방법-a-docker-desktop-linux-컨테이너--권장)
   - 3.2 [방법 B: Windows 네이티브 설치](#32-방법-b-windows-네이티브-설치)
4. [Ollama 모델 다운로드](#4-ollama-모델-다운로드)
5. [.NET 서비스 빌드 및 실행](#5-net-서비스-빌드-및-실행)
6. [서비스 상태 확인](#6-서비스-상태-확인)
7. [트러블슈팅](#7-트러블슈팅)
8. [부록](#부록-a-git-저장소-정보)
   - A. [Git 저장소 정보](#부록-a-git-저장소-정보)
   - B. [주요 설정 파일 경로](#부록-b-주요-설정-파일-경로)
   - C. [네이티브 설치 바이너리 경로](#부록-c-네이티브-설치-바이너리-경로-방법-b)
   - D. [운영 스크립트 경로](#부록-d-운영-스크립트-경로)
   - E. [Ollama 모델 목록](#부록-e-ollama-모델-목록)

---

## 1. 시스템 요구사항

| 항목 | 최소 사양 | 권장 사양 |
|------|-----------|-----------|
| OS | Windows 10 Pro 21H2+ | Windows 10/11 Pro |
| RAM | 16GB | 32GB+ |
| 디스크 | 50GB 여유 | 100GB+ SSD |
| CPU | 8코어 | 16코어+ |
| GPU | - | NVIDIA GPU (Ollama 가속) |
| .NET SDK | 9.0.300 | 9.0.300 |
| Docker Desktop | 4.x (Linux 컨테이너 모드) | 최신 버전 |

### 사전 설치 소프트웨어

```powershell
# .NET SDK 확인
dotnet --version
# 출력: 9.0.300 이상

# Docker Desktop 확인 (방법 A 사용 시)
docker --version

# Git 확인
git --version
```

---

## 2. 아키텍처 개요

```
┌─────────────────────────────────────────────────────────────┐
│                     WebClient (:5010)                        │
│                     (Blazor Web UI)                          │
└─────────────┬───────────────────────────────────────────────┘
              │ WebSocket (ws://localhost:5000/ws/chat)
┌─────────────▼───────────────────────────────────────────────┐
│                  ChatGateway (:5000)                         │
│              (WebSocket Relay + REST API)                    │
└─────────────┬───────────────────────────────────────────────┘
              │ NATS Pub/Sub
┌─────────────▼───────────────────────────────────────────────┐
│                    NATS (:4222)                              │
│              (메시지 브로커 + JetStream)                      │
├─────────────────────────────────────────────────────────────┤
│  LlmService      │ KnowledgeService │ RagService            │
│  AlarmCopilot     │ RcaAgent         │ McpLogServer          │
└──────┬────────────┴──────┬───────────┴──────┬───────────────┘
       │                   │                  │
  ┌────▼────┐       ┌──────▼──────┐    ┌──────▼──────┐
  │  Redis  │       │   Qdrant    │    │   Ollama    │
  │ (:6379) │       │ (:6333/6334)│    │  (:11434)   │
  └─────────┘       └─────────────┘    └─────────────┘
```

### 인프라 구성요소 요약

| 구성요소 | 버전 | 포트 | 용도 |
|----------|------|------|------|
| **NATS** | 2.10 | 4222 (클라이언트), 8222 (모니터링) | 서비스 간 메시징 (Pub/Sub) |
| **Redis** | 7.x | 6379 | 대화 이력 저장, 세션 관리 |
| **Qdrant** | 1.12.0 | 6333 (HTTP), 6334 (gRPC) | 벡터 데이터베이스 (RAG) |
| **Ollama** | latest | 11434 | 로컬 LLM 추론 엔진 |

### .NET 서비스 요약

| 서비스 | 포트 | 호스트 타입 | NATS 주제 | 의존 인프라 |
|--------|------|-------------|-----------|-------------|
| **ChatGateway** | 5000 | WebApplication | `chat.stream.>` | NATS, Redis |
| **LlmService** | 5001 | Worker | `chat.request` | NATS, Redis, Ollama |
| **KnowledgeService** | 5002 | WebApplication | `knowledge.extract.request` | NATS, Redis, Qdrant, Ollama |
| **RagService** | 5003 | Worker | `rag.request` | NATS, Qdrant, Ollama |
| **AlarmCopilot** | 5004 | Worker | `equipment.*.alarm.triggered` | NATS, Redis |
| **McpLogServer** | 5005 | Worker | `mcp.log.query.request` | NATS |
| **RcaAgent** | 5006 | Worker | `rca.run.request` | NATS |
| **WebClient** | 5010 | WebApplication | - | ChatGateway |

> **참고**: Worker 서비스(Host.CreateDefaultBuilder)는 웹 서버 없이 NATS 메시지만 처리합니다.
> WebApplication 서비스만 HTTP 포트를 바인딩합니다. 모든 서비스의 `appsettings.json`에
> `"Urls": "http://0.0.0.0:<port>"` 설정이 포함되어 있어야 포트 충돌을 방지할 수 있습니다.

---

## 3. 인프라 구성요소 설치

### 3.1 방법 A: Docker Desktop (Linux 컨테이너) - 권장

#### Step 1: Docker Desktop 설치 및 Linux 컨테이너 모드 전환

1. [Docker Desktop](https://www.docker.com/products/docker-desktop/) 설치
2. Docker Desktop 실행
3. **중요**: 시스템 트레이의 Docker 아이콘 → 우클릭 → **"Switch to Linux containers..."** 클릭
4. 전환 완료 확인:
   ```powershell
   docker info | findstr "OSType"
   # 출력: OSType: linux
   ```

> **주의**: Windows 컨테이너 모드에서는 NATS, Redis, Qdrant, Ollama 이미지를 사용할 수 없습니다.
> 반드시 **Linux 컨테이너 모드**로 전환하세요.

#### Step 2: Docker Compose로 인프라 실행

```powershell
cd D:\__WORK2__\LLM-Chat-Service-master\infra
docker compose up -d
```

#### Step 3: 실행 확인

```powershell
docker compose ps
```

예상 출력:
```
NAME       IMAGE                    STATUS          PORTS
nats       nats:2.10-alpine         Up              0.0.0.0:4222->4222/tcp, 0.0.0.0:8222->8222/tcp
redis      redis:7-alpine           Up              0.0.0.0:6379->6379/tcp
qdrant     qdrant/qdrant:v1.12.0    Up              0.0.0.0:6333->6333/tcp, 0.0.0.0:6334->6334/tcp
ollama     ollama/ollama:latest     Up              0.0.0.0:11434->11434/tcp
```

---

### 3.2 방법 B: Windows 네이티브 설치 - 권장

Docker Desktop의 Linux 컨테이너 모드 전환이 불가능하거나, Docker를 사용할 수 없는 환경에서
GitHub Releases에서 바이너리를 직접 다운로드하여 설치합니다.

> **검증 환경**: Windows 10 Pro 22H2 (Build 19045) 에서 아래 절차로 정상 동작 확인됨

#### 3.2.1 설치 디렉토리 구조

설치 완료 후 다음과 같은 구조가 됩니다:

```
D:\
├── nats-server-v2.10.24-windows-amd64\
│   └── nats-server.exe
├── nats-data\
│   └── jetstream\          (JetStream 데이터)
├── Redis\
│   └── Redis-8.6.0-Windows-x64-msys2\
│       ├── redis-server.exe
│       └── redis-cli.exe
└── Qdrant\
    └── qdrant.exe
```

#### 3.2.2 NATS Server 설치

GitHub Releases에서 Windows AMD64 바이너리를 다운로드합니다.

```powershell
# 1. 다운로드 및 압축 해제
$natsVersion = "2.10.24"
$natsUrl = "https://github.com/nats-io/nats-server/releases/download/v${natsVersion}/nats-server-v${natsVersion}-windows-amd64.zip"
Invoke-WebRequest -Uri $natsUrl -OutFile "D:\nats-server.zip" -UseBasicParsing
Expand-Archive -Path "D:\nats-server.zip" -DestinationPath "D:\" -Force
Remove-Item "D:\nats-server.zip"

# 2. JetStream 데이터 디렉토리 생성
New-Item -ItemType Directory -Force -Path "D:\nats-data\jetstream"

# 3. 버전 확인
& "D:\nats-server-v${natsVersion}-windows-amd64\nats-server.exe" --version
# 출력: nats-server: v2.10.24
```

NATS 설정 파일은 프로젝트에 포함되어 있습니다 (`infra\nats\nats-server-windows.conf`):

```conf
server_name: fab-copilot-nats

listen: 0.0.0.0:4222
http: 0.0.0.0:8222

jetstream {
  store_dir: "D:\\nats-data\\jetstream"
  max_mem: 1G
  max_file: 10G
}
```

> **주의**: `store_dir` 경로에서 백슬래시는 반드시 **이중 백슬래시(`\\`)**로 이스케이프하고,
> 경로 전체를 **큰따옴표(`"`)** 로 감싸야 합니다. 그렇지 않으면 파싱 오류가 발생합니다.

NATS 실행:
```powershell
Start-Process -FilePath "D:\nats-server-v2.10.24-windows-amd64\nats-server.exe" `
  -ArgumentList "-c", "D:\__WORK2__\LLM-Chat-Service-master\infra\nats\nats-server-windows.conf" `
  -WindowStyle Minimized
```

#### 3.2.3 Redis 설치

[redis-windows](https://github.com/redis-windows/redis-windows) 프로젝트에서 Windows 빌드를 다운로드합니다.

```powershell
# 1. 다운로드 및 압축 해제
$redisVersion = "8.6.0"
$redisUrl = "https://github.com/redis-windows/redis-windows/releases/download/${redisVersion}/Redis-${redisVersion}-Windows-x64-msys2.zip"
Invoke-WebRequest -Uri $redisUrl -OutFile "D:\redis.zip" -UseBasicParsing
Expand-Archive -Path "D:\redis.zip" -DestinationPath "D:\Redis" -Force
Remove-Item "D:\redis.zip"

# 2. 확인
& "D:\Redis\Redis-${redisVersion}-Windows-x64-msys2\redis-server.exe" --version
```

Redis 실행:
```powershell
Start-Process -FilePath "D:\Redis\Redis-8.6.0-Windows-x64-msys2\redis-server.exe" `
  -ArgumentList "--appendonly", "yes", "--maxmemory", "2gb", "--maxmemory-policy", "allkeys-lru" `
  -WindowStyle Minimized
```

연결 확인:
```powershell
& "D:\Redis\Redis-8.6.0-Windows-x64-msys2\redis-cli.exe" ping
# 출력: PONG
```

#### 3.2.4 Qdrant 설치

GitHub Releases에서 Windows MSVC 바이너리를 다운로드합니다.

```powershell
# 1. 다운로드 및 압축 해제
$qdrantVersion = "1.12.0"
$qdrantUrl = "https://github.com/qdrant/qdrant/releases/download/v${qdrantVersion}/qdrant-x86_64-pc-windows-msvc.zip"
Invoke-WebRequest -Uri $qdrantUrl -OutFile "D:\qdrant.zip" -UseBasicParsing
Expand-Archive -Path "D:\qdrant.zip" -DestinationPath "D:\Qdrant" -Force
Remove-Item "D:\qdrant.zip"

# 2. 확인
& "D:\Qdrant\qdrant.exe" --version
```

Qdrant 실행:
```powershell
Start-Process -FilePath "D:\Qdrant\qdrant.exe" -WindowStyle Minimized
# 기본 포트: HTTP 6333, gRPC 6334
```

헬스체크:
```powershell
Invoke-RestMethod http://localhost:6333/healthz
```

#### 3.2.5 Ollama 설치

Ollama는 공식 Windows 설치 프로그램을 사용합니다.

```powershell
# winget으로 설치
winget install Ollama.Ollama --source winget

# 설치 후 자동으로 시스템 트레이에서 실행됨
# 기본 포트: http://localhost:11434
```

확인:
```powershell
ollama --version
Invoke-RestMethod http://localhost:11434/api/tags
```

#### 3.2.6 인프라 일괄 실행/중지 스크립트

프로젝트의 `scripts/` 디렉토리에 인프라 관리 스크립트가 준비되어 있습니다:

```powershell
# 인프라 시작 (NATS, Redis, Qdrant + Ollama 상태 확인)
.\scripts\start-infra.ps1

# 인프라 중지 (NATS, Redis, Qdrant 프로세스 종료)
.\scripts\stop-infra.ps1
```

- 이미 실행 중인 서비스는 자동으로 건너뜀
- 시작/중지 후 포트 헬스체크 수행
- Ollama는 시스템 트레이에서 관리하므로 중지 스크립트에서 제외

---

## 4. Ollama 모델 다운로드

FabCopilot이 사용하는 모델을 미리 다운로드합니다.

```powershell
# 채팅 모델 (기본 모델)
ollama pull exaone3.5:7.8b

# 대체 채팅 모델
ollama pull qwen2.5:7b
ollama pull llama3.1:8b

# 임베딩 모델 (RAG/Knowledge 필수)
ollama pull nomic-embed-text
```

다운로드 확인:
```powershell
ollama list
```

예상 출력:
```
NAME                    SIZE      MODIFIED
exaone3.5:7.8b          4.9 GB    ...
qwen2.5:7b              4.7 GB    ...
llama3.1:8b             4.7 GB    ...
nomic-embed-text        274 MB    ...
```

> **참고**: 모델 다운로드에는 총 약 15GB의 디스크 공간과 네트워크 대역폭이 필요합니다.

---

## 5. .NET 서비스 빌드 및 실행

### Step 1: 솔루션 빌드

```powershell
cd D:\__WORK2__\LLM-Chat-Service-master
dotnet build FabCopilot.sln
```

### Step 2: 서비스 실행 (권장 순서)

각 서비스를 별도 터미널(PowerShell/CMD)에서 실행합니다.

**터미널 1 - ChatGateway** (가장 먼저):
```powershell
dotnet run --project src\Services\FabCopilot.ChatGateway
```

**터미널 2 - LlmService**:
```powershell
dotnet run --project src\Services\FabCopilot.LlmService
```

**터미널 3 - KnowledgeService**:
```powershell
dotnet run --project src\Services\FabCopilot.KnowledgeService
```

**터미널 4 - RagService**:
```powershell
dotnet run --project src\Services\FabCopilot.RagService
```

**터미널 5 - AlarmCopilot**:
```powershell
dotnet run --project src\Services\FabCopilot.AlarmCopilot
```

**터미널 6 - McpLogServer**:
```powershell
dotnet run --project src\Services\FabCopilot.McpLogServer
```

**터미널 7 - RcaAgent**:
```powershell
dotnet run --project src\Services\FabCopilot.RcaAgent
```

**터미널 8 - WebClient** (마지막):
```powershell
dotnet run --project src\Client\FabCopilot.WebClient
```

### Step 3: 전체 실행/중지 스크립트 (일괄 관리)

프로젝트의 `scripts/` 디렉토리에 전체 시스템 관리 스크립트가 준비되어 있습니다:

```powershell
# 전체 시작 (인프라 → 빌드 → 서비스 → 헬스체크)
.\scripts\start-all.ps1

# 전체 중지 (.NET 서비스 → 인프라 → 포트 확인)
.\scripts\stop-all.ps1
```

**`start-all.ps1` 동작 순서**:
1. Phase 1: 인프라 시작 (`start-infra.ps1` 호출)
2. Phase 2: 솔루션 빌드 (`dotnet build`)
3. Phase 3: 8개 .NET 서비스를 별도 PowerShell 창에서 실행
4. Phase 4: 전체 포트 헬스체크 (NATS, Redis, Qdrant, Ollama, ChatGateway, WebClient)

**`stop-all.ps1` 동작 순서**:
1. Phase 1: 8개 .NET 서비스 프로세스 종료
2. Phase 2: 인프라 중지 (`stop-infra.ps1` 호출)
3. Phase 3: 포트 닫힘 확인

> **참고**: 인프라가 정상 실행되지 않으면 `start-all.ps1`이 자동으로 중단됩니다.

---

## 6. 서비스 상태 확인

### 인프라 확인

```powershell
# NATS 상태 (모니터링 포트)
curl http://localhost:8222/varz

# Redis 연결 확인
redis-cli ping

# Qdrant 상태
curl http://localhost:6333/healthz

# Ollama 상태 및 모델 목록
curl http://localhost:11434/api/tags
```

### .NET 서비스 확인

```powershell
# ChatGateway 상태 (WebApplication - /health 엔드포인트)
curl http://localhost:5000/health

# KnowledgeService 상태 (WebApplication - /health 엔드포인트)
curl http://localhost:5002/health

# WebClient 접속
# 브라우저에서 http://localhost:5010 접속

# 전체 서비스 포트 일괄 헬스체크
.\scripts\health-check.ps1
```

### WebSocket 채팅 테스트

```powershell
# 영어 채팅 테스트
.\scripts\test-chat2.ps1

# 한국어 채팅 테스트
.\scripts\test-chat-kr.ps1
```

---

## 7. 트러블슈팅

### 문제 1: Docker에서 "no matching manifest for windows/amd64" 오류

**원인**: Docker가 Windows 컨테이너 모드로 실행 중
**해결**:
1. 시스템 트레이 → Docker 아이콘 우클릭
2. "Switch to Linux containers..." 클릭
3. Docker Desktop Settings → General → **"Use the WSL 2 based engine"** 체크
4. Apply & Restart
5. `docker info | findstr "OSType"` 에서 `linux` 확인 후 재시도

> **참고**: WSL2 백엔드가 활성화되지 않으면 Linux 컨테이너 전환이 불가능합니다.
> 이 경우 **방법 B (네이티브 설치)** 를 사용하세요.

### 문제 2: NATS 설정 파일 파싱 오류 (Invalid escape character)

**원인**: `nats-server.conf` 파일에서 Windows 경로의 백슬래시가 이스케이프 문자로 해석됨
**에러 메시지**: `Parse error: Invalid escape character 'j'. Only the following escape characters are allowed: \xXX, \t, \n, \r, \", \\.`
**해결**: 경로를 큰따옴표로 감싸고 백슬래시를 이중으로 작성
```conf
# 잘못된 예
jetstream {
  store_dir: D:\nats-data\jetstream    # 오류 발생!
}

# 올바른 예
jetstream {
  store_dir: "D:\\nats-data\\jetstream"  # 정상
}
```

### 문제 3: NATS 연결 실패 (`can not connect uris: nats://localhost:4222`)

**원인**: NATS 서버가 실행되지 않음
**해결**:
```powershell
# 프로세스 확인
Get-Process -Name "nats-server" -ErrorAction SilentlyContinue

# 네이티브 방식으로 재시작
Start-Process -FilePath "D:\nats-server-v2.10.24-windows-amd64\nats-server.exe" `
  -ArgumentList "-c", "D:\__WORK2__\LLM-Chat-Service-master\infra\nats\nats-server-windows.conf" `
  -WindowStyle Minimized

# 포트 확인
Test-NetConnection localhost -Port 4222
```

### 문제 4: Ollama 임베딩 API 404 오류

**원인**: 임베딩 모델(`nomic-embed-text`)이 다운로드되지 않음
**에러 메시지**: `Response status code does not indicate success: 404 (Not Found)` at `/api/embed`
**해결**:
```powershell
ollama list                    # 설치된 모델 확인
ollama pull nomic-embed-text   # 임베딩 모델 다운로드 (274MB)
ollama pull exaone3.5:7.8b     # 기본 채팅 모델 (4.8GB)
ollama pull llama3.1:8b        # 대체 채팅 모델 (4.9GB)
ollama pull qwen2.5:7b         # 대체 채팅 모델 (4.7GB)
```

### 문제 5: BackgroundService가 StopHost로 종료

**원인**: 인프라 서비스 미실행 시 BackgroundService가 예외를 발생시키고 호스트가 종료됨
**에러 메시지**: `The HostOptions.BackgroundServiceExceptionBehavior is configured to StopHost`
**해결**:
1. 모든 인프라 서비스(NATS, Redis, Qdrant, Ollama)가 실행 중인지 확인
2. 반드시 **인프라를 먼저 시작**한 후 .NET 서비스를 실행
3. 포트 확인 스크립트 실행:
```powershell
@(4222, 6379, 6333, 11434) | ForEach-Object {
  $port = $_
  try {
    $tcp = New-Object Net.Sockets.TcpClient("localhost", $port)
    $tcp.Close()
    Write-Host "Port ${port}: OK" -ForegroundColor Green
  } catch {
    Write-Host "Port ${port}: FAIL" -ForegroundColor Red
  }
}
```

### 문제 6: WebClient 포트 충돌 (port 5010 already in use)

**원인**: 이전 WebClient 프로세스가 종료되지 않은 상태에서 재실행
**해결**:
```powershell
# 기존 프로세스 종료
Get-Process -Name "FabCopilot.WebClient" -ErrorAction SilentlyContinue | Stop-Process -Force

# 또는 포트를 사용하는 프로세스 확인 후 종료
netstat -ano | findstr ":5010"
# PID 확인 후
Stop-Process -Id <PID> -Force
```

### 문제 7: 포트 충돌 일반

**원인**: `WebApplication.CreateBuilder`를 사용하는 서비스(`ChatGateway`, `KnowledgeService`, `WebClient`)에
`Urls` 설정이 없으면 ASP.NET Core 기본값 `http://localhost:5000`을 사용하여 충돌이 발생합니다.

**해결**: 각 서비스의 `appsettings.json`에 고유 포트를 명시합니다:
```json
{ "Urls": "http://0.0.0.0:<port>" }
```

**현재 포트 할당**:
```
ChatGateway:      5000    KnowledgeService: 5002
LlmService:       5001    RagService:       5003
AlarmCopilot:     5004    McpLogServer:     5005
RcaAgent:         5006    WebClient:        5010
```

**확인 방법**:
```powershell
# 전체 헬스체크 스크립트 실행
.\scripts\health-check.ps1

# 개별 포트 확인
netstat -ano | findstr ":4222"   # NATS
netstat -ano | findstr ":6379"   # Redis
netstat -ano | findstr ":6333"   # Qdrant
netstat -ano | findstr ":11434"  # Ollama
netstat -ano | findstr ":5000"   # ChatGateway
netstat -ano | findstr ":5002"   # KnowledgeService
netstat -ano | findstr ":5010"   # WebClient
```

---

## 부록 A: Git 저장소 정보

### 저장소 경로

```
D:\__WORK2__\LLM-Chat-Service-master
```

### 디렉토리 구조

```
LLM-Chat-Service-master/
├── FabCopilot.sln                    # .NET 솔루션 파일
├── global.json                       # .NET SDK 버전 지정
├── Directory.Build.props             # 공통 빌드 설정
├── Directory.Packages.props          # NuGet 패키지 버전 중앙 관리
├── .gitignore                        # Git 제외 규칙
├── Plan/                             # 설계 문서 및 SOP
│   ├── SOP-Windows-Infrastructure-Setup.md
│   ├── Fab_Copilot_User_Manual.md
│   └── Fab_OnPrem_LLM_Copilot_Design_MCP_Extended.md
├── infra/                            # 인프라 설정
│   ├── docker-compose.yml
│   └── nats/
│       ├── nats-server.conf          # Docker/Linux용
│       └── nats-server-windows.conf  # Windows 네이티브용
├── scripts/                          # 운영 스크립트
│   ├── start-all.ps1
│   ├── stop-all.ps1
│   ├── start-infra.ps1
│   ├── stop-infra.ps1
│   ├── health-check.ps1
│   ├── test-chat2.ps1
│   └── test-chat-kr.ps1
├── src/
│   ├── Client/
│   │   └── FabCopilot.WebClient/     # Blazor Web UI (:5010)
│   ├── Services/
│   │   ├── FabCopilot.ChatGateway/   # WebSocket Gateway (:5000)
│   │   ├── FabCopilot.LlmService/    # LLM Worker (:5001)
│   │   ├── FabCopilot.KnowledgeService/ # Knowledge API (:5002)
│   │   ├── FabCopilot.RagService/    # RAG Worker (:5003)
│   │   ├── FabCopilot.AlarmCopilot/  # Alarm Worker (:5004)
│   │   ├── FabCopilot.McpLogServer/  # MCP Log Worker (:5005)
│   │   └── FabCopilot.RcaAgent/      # RCA Worker (:5006)
│   └── Shared/
│       ├── FabCopilot.Contracts/     # 공유 메시지/모델/상수
│       ├── FabCopilot.Messaging/     # NATS 메시지 버스
│       ├── FabCopilot.Redis/         # Redis 대화 저장소
│       ├── FabCopilot.Llm/           # Ollama LLM 클라이언트
│       ├── FabCopilot.VectorStore/   # Qdrant 벡터 저장소
│       └── FabCopilot.Observability/ # 로깅/텔레메트리
└── tests/
    └── FabCopilot.RagPipeline.Tests/ # RAG 파이프라인 테스트
```

### .gitignore 주요 제외 항목

| 카테고리 | 제외 패턴 | 설명 |
|----------|-----------|------|
| .NET 빌드 | `bin/`, `obj/`, `[Dd]ebug/`, `[Rr]elease/` | 빌드 출력물 |
| NuGet | `**/packages/`, `*.nupkg` | 패키지 캐시 |
| IDE | `.vs/`, `.idea/` | IDE 설정 |
| 런타임 데이터 | `storage/`, `appendonlydir/`, `.qdrant-initialized`, `dump.rdb` | Qdrant/Redis 런타임 데이터 |
| 보안 | `*.pfx`, `*.key`, `*.pem`, `infra/certs/` | 인증서/키 |
| 로그 | `logs/`, `*.log` | 로그 파일 |
| Claude Code | `.claude/` | AI 도구 설정 |

## 부록 B: 주요 설정 파일 경로

| 파일 | 위치 | 용도 |
|------|------|------|
| docker-compose.yml | `infra/docker-compose.yml` | 인프라 컨테이너 정의 (방법 A) |
| nats-server.conf | `infra/nats/nats-server.conf` | NATS 브로커 설정 (Docker/Linux용) |
| nats-server-windows.conf | `infra/nats/nats-server-windows.conf` | NATS 브로커 설정 (Windows 네이티브용) |
| global.json | `global.json` | .NET SDK 버전 지정 |
| Directory.Packages.props | `Directory.Packages.props` | NuGet 패키지 버전 중앙 관리 |
| .gitignore | `.gitignore` | Git 제외 규칙 |
| appsettings.json | 각 서비스의 프로젝트 폴더 | 서비스별 설정 (NATS, Redis, Qdrant, Ollama URL, 포트 등) |
| launchSettings.json | 각 서비스의 `Properties/` 폴더 | 개발 환경 실행 프로필 |

## 부록 C: 네이티브 설치 바이너리 경로 (방법 B)

| 구성요소 | 바이너리 경로 | 버전 | 다운로드 출처 |
|----------|---------------|------|---------------|
| NATS Server | `D:\nats-server-v2.10.24-windows-amd64\nats-server.exe` | v2.10.24 | [GitHub](https://github.com/nats-io/nats-server/releases) |
| NATS Data | `D:\nats-data\jetstream\` | - | 자동 생성 |
| Redis | `D:\Redis\Redis-8.6.0-Windows-x64-msys2\redis-server.exe` | v8.6.0 | [GitHub](https://github.com/redis-windows/redis-windows/releases) |
| Qdrant | `D:\Qdrant\qdrant.exe` | v1.12.0 | [GitHub](https://github.com/qdrant/qdrant/releases) |
| Ollama | 시스템 PATH (`winget install`) | v0.15.4+ | [ollama.com](https://ollama.com) |

## 부록 D: 운영 스크립트 경로

| 스크립트 | 위치 | 용도 |
|----------|------|------|
| start-all.ps1 | `scripts/start-all.ps1` | 인프라 + 빌드 + .NET 서비스 전체 일괄 시작 |
| stop-all.ps1 | `scripts/stop-all.ps1` | .NET 서비스 + 인프라 전체 일괄 중지 |
| start-infra.ps1 | `scripts/start-infra.ps1` | 인프라(NATS, Redis, Qdrant) 일괄 시작 |
| stop-infra.ps1 | `scripts/stop-infra.ps1` | 인프라 일괄 중지 |
| health-check.ps1 | `scripts/health-check.ps1` | 인프라 + .NET 서비스 전체 포트 헬스체크 (12개 엔드포인트) |
| test-chat2.ps1 | `scripts/test-chat2.ps1` | WebSocket 영어 채팅 테스트 |
| test-chat-kr.ps1 | `scripts/test-chat-kr.ps1` | WebSocket 한국어 채팅 테스트 |

## 부록 E: Ollama 모델 목록

| 모델 | 용도 | 크기 | 사용 서비스 |
|------|------|------|-------------|
| `exaone3.5:7.8b` | 기본 채팅 모델 | ~4.8 GB | LlmService |
| `llama3.1:8b` | 대체 채팅 모델 | ~4.9 GB | LlmService, KnowledgeService |
| `qwen2.5:7b` | 대체 채팅 모델 | ~4.7 GB | LlmService |
| `nomic-embed-text` | 임베딩 모델 (RAG 필수) | ~274 MB | RagService, KnowledgeService |

> **총 디스크 사용량**: 약 15GB (모든 모델 다운로드 시)

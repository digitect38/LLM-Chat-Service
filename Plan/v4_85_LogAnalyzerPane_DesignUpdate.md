# v4.85 Design Spec Update — Log Analyzer Right Panel

> **Purpose:** v4_84.docx 설계서에 역반영할 내용 초안 (복사/붙여넣기 용도)
> **Date:** 2026-03-06
> **Scope:** Section 9.6, 9.7, Chapter 7 보완, Revision History 추가

---

## 1. Revision History 추가 항목

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| v4.85 | 2025-03 | Log Analyzer Right Panel in Chat UI (Section 9.6.2): Dashboard LogAnalyzer ported to WebClient as sliding right panel (380px), independent of Citation Pane, 4 sub-tabs (Search Results, Top Errors, Error Dashboard, Request Flow), resize handle, mobile fullscreen overlay. Section 9.7 Top Bar updated with Log Analyzer toggle icon. Chapter 7 supplemented with WebClient-embedded log analysis UI. | |

---

## 2. Section 9.6 Right Panel (Context-Aware Switching) — 추가/수정

### 현재 내용 (v4.84)

```
9.6 Right Panel (Context-Aware Switching)
  9.6.1 Chat Mode: Citation Pane
  9.6.2 Dashboard Mode: Sensor Detail Panel
```

### 수정 후

```
9.6 Right Panel (Context-Aware Switching)
  9.6.1 Chat Mode: Citation Pane
  9.6.2 Chat Mode: Log Analyzer Pane   ← NEW
  9.6.3 Dashboard Mode: Sensor Detail Panel
```

### 9.6.2 Chat Mode: Log Analyzer Pane (신규 내용)

The Log Analyzer Pane is a sliding right panel embedded in the WebClient Chat UI, providing real-time log search, error analysis, and cross-service request flow tracing without requiring the separate Service Dashboard (port 5020).

**Panel Layout:**

```
┌──────────┬──────────────┬────────────┐
│ Sidebar  │  Chat Area   │ Log Panel  │
│ (260px)  │  (flex:1)    │  (380px)   │
│          │              │            │
│ History  │  Messages    │ Search Bar │
│          │              │ Stats      │
│          │              │ Sub-tabs   │
│          │ Input Box    │ Results    │
└──────────┴──────────────┴────────────┘
```

**Key Properties:**

| Property | Value |
|----------|-------|
| Default width | 380px |
| Min width | 300px |
| Max width | 60vw |
| Resize | Draggable left-edge handle (4px) |
| Toggle | Top Bar chart icon |
| Mobile (<768px) | `position: fixed; width: 100vw; z-index: 150` (fullscreen overlay) |
| Independence | Opens/closes independently of Citation Pane; both can be open simultaneously |

**Architecture:**

- **Models** (`WebClient/Models/`): JsonLogEntry, LogSearchQuery, LogSearchResult, LogAnalytics (ErrorRateBucket, ErrorTemplateGroup, ServiceHealthBucket), RequestFlowEvent (RequestFlow, FlowEvent)
- **Services** (`WebClient/Services/`): LogReaderService (Singleton), LogAnalyzerService (Singleton)
- **Component** (`WebClient/Shared/LogAnalyzerPane.razor`): Blazor component following CitationPane pattern
- **Log Source**: Reads CLEF JSON log files from `{AppBase}/../../../../../../logs/*.json` (shared file access)
- **No external dependency**: Does not require Dashboard (5020) to be running

**Search Bar (vertical stack, panel-width optimized):**

| Control | Description |
|---------|-------------|
| Keyword input | Full-text search on RenderedMessage + Exception (full width) |
| Service dropdown | Filter by ServiceName extracted from log file index |
| Level toggles | ERR (red), WRN (yellow), INF (blue), DBG (gray) — multi-select |
| Time presets | 1h / 6h / 24h / 7d quick buttons |
| CorrelationId input | Filter by exact CorrelationId match |
| Search button | Execute search (max 10,000 entries scanned) |

**Stats Summary (2x2 compact grid):**

| Metric | Description |
|--------|-------------|
| Total | Total matching entries count |
| Errors | ERR-level entries in current page |
| Warnings | WRN-level entries in current page |
| CIDs | Distinct CorrelationId count in current page |

**Sub-tabs:**

| Tab | Content | Data Source |
|-----|---------|-------------|
| **Results** | Paginated log table (Time, Service, Level badge, CorrelationId link, Message + Exception). 100 entries/page. Clickable CorrelationId switches to Flow tab. | `LogReaderService.SearchJsonLogs()` |
| **Errors** | Top error templates grouped by MessageTemplate + ServiceName, sorted by occurrence count. Shows Count, Service, Template, Last Seen. | `LogAnalyzerService.GetTopErrors()` |
| **Dashboard** | Horizontal stacked bar chart per service. Segments: ERR (red), WRN (yellow), INF/DBG (blue). Total count label. | `LogAnalyzerService.GetServiceHealth()` |
| **Flow** | CorrelationId input + Trace button. Vertical timeline with flow nodes (color-coded by level), service name, timestamp, gap from previous event, message. Summary shows event count and total duration. | `LogAnalyzerService.GetRequestFlow()` |

**JS Interop:**

Reuses existing `citationResize.init()` function with different selectors:
```javascript
citationResize.init(".log-pane", ".log-pane-resize-handle", 300);
```
No new JavaScript code required.

**CSS Classes (light theme, `lp-` prefix):**

All styles use `lp-` prefix to avoid collision with Citation Pane (`citation-`) and Dashboard (`log-`) styles.

---

## 3. Section 9.7 Top Bar — 수정

### 현재 내용 (v4.84)

Top Bar에 Citation Pane 토글 아이콘만 기술됨.

### 추가 내용

Top Bar에 **Log Analyzer 토글 아이콘**을 추가한다. Citation 아이콘 왼쪽에 배치.

| Icon | Position | Action | Active state |
|------|----------|--------|-------------|
| Chart icon (SVG) | Left of Citation icon | Toggle Log Analyzer Pane open/close | `topbar-icon-active` class applied when panel is open |
| Document icon (SVG) | Rightmost | Toggle Citation Pane open/close | `topbar-icon-active` class applied when panel is open |

---

## 4. Chapter 7 Log Analysis Engine — 보완

### 현재 내용 (v4.84)

Chapter 7은 Layer 2 백엔드 로그 분석 엔진 (TimeSeriesAnalyzer, AlarmPatternAnalyzer 등)만 기술.

### 추가 항목 (7.x 신규 절)

#### 7.x WebClient Embedded Log Analyzer UI

In addition to the backend analysis engine in McpLogServer, a lightweight log analysis UI is embedded directly in the WebClient Chat interface as a right-side panel (see Section 9.6.2).

**Scope Distinction:**

| Aspect | McpLogServer Analysis (Ch.7) | WebClient Log Analyzer (9.6.2) |
|--------|------------------------------|-------------------------------|
| Purpose | Backend ML/statistical analysis | Frontend ad-hoc log browsing |
| Components | TimeSeriesAnalyzer, IsolationForest, RulPredictor, FusionEngine | LogReaderService, LogAnalyzerService |
| Data Source | Structured sensor data + alarms | CLEF JSON log files |
| Deployment | Server-side worker service | Embedded in WebClient (Singleton services) |
| User Access | Dashboard (5020) or API | Chat UI right panel (5010) |
| Key Features | CUSUM, 3-sigma, anomaly detection, RUL prediction | Full-text search, error grouping, service health chart, request flow tracing |

The WebClient LogAnalyzerService is a simplified, file-based analysis service that provides immediate log visibility without requiring the full McpLogServer analysis pipeline. It reads CLEF JSON structured logs with shared file access (`FileShare.ReadWrite`) to avoid blocking active log writers.

---

## 5. 변경 파일 요약 (구현 완료)

| # | File | Type | Description |
|---|------|------|-------------|
| 1 | `WebClient/Models/JsonLogEntry.cs` | NEW | CLEF JSON log entry model with DisplayLevel, RenderedMessage, DisplayTime |
| 2 | `WebClient/Models/LogSearchQuery.cs` | NEW | Search query DTO (keyword, service, levels, time range, pagination) |
| 3 | `WebClient/Models/LogSearchResult.cs` | NEW | Paginated search result with TotalPages calculation |
| 4 | `WebClient/Models/LogAnalytics.cs` | NEW | ErrorRateBucket, ErrorTemplateGroup, ServiceHealthBucket |
| 5 | `WebClient/Models/RequestFlowEvent.cs` | NEW | RequestFlow, FlowEvent for cross-service tracing |
| 6 | `WebClient/Services/LogReaderService.cs` | NEW | JSON log file reading, indexing, search (max 10k scan), lazy streaming |
| 7 | `WebClient/Services/LogAnalyzerService.cs` | NEW | Error rates, top errors, request flow, service health analysis |
| 8 | `WebClient/Shared/LogAnalyzerPane.razor` | NEW | 380px sliding right panel with 4 sub-tabs |
| 9 | `WebClient/Pages/Index.razor` | MOD | @inject services, topbar toggle icon, LogAnalyzerPane placement, state/toggle methods |
| 10 | `WebClient/Program.cs` | MOD | DI registration (AddSingleton) |
| 11 | `WebClient/wwwroot/css/app.css` | MOD | .log-pane + lp-* CSS classes + mobile responsive |

---

## 6. docx 반영 체크리스트

- [ ] Revision History 테이블에 v4.85 행 추가
- [ ] Table of Contents에 `9.6.2 Chat Mode: Log Analyzer Pane` 추가
- [ ] Section 9.6.2 신규 작성 (위 내용 복사)
- [ ] Section 9.6.2 기존 → 9.6.3으로 번호 변경
- [ ] Section 9.7 Top Bar에 Log Analyzer 아이콘 설명 추가
- [ ] Chapter 7에 7.x WebClient Embedded Log Analyzer UI 절 추가
- [ ] User Manual 변경 이력에 v1.4 반영 확인

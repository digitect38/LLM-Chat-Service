# v4.85 Phase 3 Design Spec — Chat UI 컬러 개선 + 로그 네비게이션 + UX 최적화

> **Purpose:** v4_84.docx 설계서에 역반영할 Phase 3 변경 내용 초안
> **Date:** 2026-03-07
> **Scope:** Section 9 Chat UI 전반, Log Analyzer 연동, UX 개선

---

## 1. Revision History 추가 항목

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| v4.85-P3 | 2026-03-07 | **Phase 3: Chat UI 컬러 개선 + 로그 네비게이션 + UX 최적화** — (1) 7가지 CSS 컬러 개선: 사용자 메시지 블루 틴트, 탭별 accent 컬러(blue/green/amber/purple), 전송 버튼 블루 그라디언트, 입력 포커스 glow, 힌트 카드 accent, AI 아바타 glow, 코드 블록 언어 라벨 badge (~30 언어). (2) LLM 응답 내 타임스탬프 → 클릭 시 Log Panel 자동 열림 + 스크롤 + 하이라이트 (JS Interop). (3) 로그 인용 블록 ([HH:mm:ss] Service [LEVEL] msg) 클릭 시 Log Panel 검색 + 네비게이션. (4) 대화 목록 더블탭 버그 수정 (모바일). (5) 채팅 메시지 영역 컴팩트 레이아웃, 액션 버튼별 개별 컬러, 아바타 수직 정렬 통일. | |

---

## 2. Feature 1: Chat UI 컬러 개선 (CSS 전용)

### 2.1 사용자 메시지 배경 (Section 9.5 Messages Area)

| Property | Before | After |
|----------|--------|-------|
| `.msg-user-row` background | `#f7f7f8` (회색) | `#eef4ff` (연한 블루 틴트) |

사용자 메시지와 AI 메시지의 시각적 구분을 강화하기 위해 사용자 행에 블루 톤 배경을 적용.

### 2.2 탭별 Accent 컬러 (Section 9.2 Panel Tab Bar)

4-panel 탭 바에 개별 accent 컬러를 적용하여 현재 활성 탭의 시각적 식별성을 향상.

| Tab (nth-child) | Label | Accent Color | Usage |
|-----------------|-------|-------------|-------|
| 1 | 목록 | `#2563eb` (blue) | 하단 라인 + 텍스트 + badge |
| 2 | 채팅 | `#059669` (green) | 하단 라인 + 텍스트 + badge |
| 3 | 로그 분석 | `#d97706` (amber) | 하단 라인 + 텍스트 + badge |
| 4 | 참고 문서 | `#7c3aed` (purple) | 하단 라인 + 텍스트 + badge |

활성 탭에 `box-shadow: 0 -2px 0 {color} inset` 하단 라인, `.tab-badge` 배경색 변경.

### 2.3 전송 버튼 (Section 9.4 Input Area)

| Property | Before | After |
|----------|--------|-------|
| `.send-btn` background | `#1a1a1a` (단색 블랙) | `linear-gradient(135deg, #2563eb, #1d4ed8)` (블루 그라디언트) |
| hover | `#333` | `linear-gradient(135deg, #1d4ed8, #1e40af)` |

### 2.4 입력 포커스 (Section 9.4 Input Area)

| Property | Before | After |
|----------|--------|-------|
| `.chat-input-box:focus-within` border-color | `#bbb` | `#2563eb` |
| box-shadow | none | `0 0 0 3px rgba(37, 99, 235, 0.15)` |

입력창에 포커스 시 블루 glow 효과로 활성 상태 시각적 피드백 제공.

### 2.5 힌트 카드 (Section 9.3 Empty State)

| Property | Before | After |
|----------|--------|-------|
| `.hint-card` border-left | 1px solid #e5e5e5 | `3px solid #2563eb` |
| background | `#fafafa` | `#f8faff` |
| hover background | `#f5f5f5` | `#eef4ff` |
| hover border-color | — | `#bdd4f7` |

빈 채팅 화면의 힌트 카드에 좌측 accent 라인을 추가하여 클릭 유도.

### 2.6 AI 아바타 Glow (Section 9.5 Messages Area)

| Property | Before | After |
|----------|--------|-------|
| `.avatar-assistant` box-shadow | none | `0 0 10px rgba(217,119,87,0.35), 0 0 20px rgba(217,119,87,0.15)` |

AI 아바타(구리/주황 원형)에 은은한 glow 효과 추가.

### 2.7 코드 블록 언어 라벨 Badge (Section 9.5 Rendered Markdown)

```css
.rendered-markdown pre { position: relative; }
code[class*="language-python"]::before {
    content: "Python"; background: rgba(53,114,165,0.85);
}
/* ~30 languages: python, javascript, typescript, csharp, json, bash, ... */
```

마크다운 코드 블록의 우측 상단에 반투명 언어 이름 badge를 표시.

| 속성 | 값 |
|------|-----|
| Position | `absolute; top: 8px; right: 12px` |
| Background | 언어별 시그니처 색상 (반투명 85%) |
| Text | white, 11px, bold |
| Border-radius | 4px |
| Supported | python, javascript, typescript, csharp, java, go, rust, json, yaml, xml, html, css, sql, bash, powershell, dockerfile, markdown, ruby, php, swift, kotlin, scala, lua, r, matlab, latex, plaintext, diff, graphql, protobuf |

---

## 3. Feature 2: 로그 타임스탬프 클릭 → Log Panel 네비게이션

### 3.1 개요

LLM 분석 응답 내 타임스탬프(예: `14:23:45.123`)를 클릭하면 자동으로 Log Panel이 열리고 해당 시간대로 스크롤 + 하이라이트 애니메이션이 실행되는 기능.

### 3.2 Data Flow

```
LLM 응답에 "14:23:45.123" 포함
  → renderMarkdown()에서 regex로 감지
  → <a class="log-timestamp-link" onclick="logTimestampInterop.onClick('14:23:45.123')">
  → JS → Blazor [JSInvokable] OnLogTimestampClick("14:23:45.123")
  → _activePanel = PanelKind.Log
  → _logPaneRef.NavigateToTimestamp("14:23:45.123")
  → 로그 검색 실행 → 해당 행 스크롤 + flash 하이라이트
```

### 3.3 Markdown 렌더러 타임스탬프 변환 (_Layout.cshtml)

`renderMarkdown()` 함수 내 citation 변환 후에 2단계 regex 적용:

```javascript
// 1단계: ISO 8601 (긴 패턴 먼저)
html = html.replace(/(\d{4}-\d{2}-\d{2}T(\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?))/g,
    function(match, full, time) {
        return '<a class="log-timestamp-link" href="#" onclick="logTimestampInterop.onClick(\'' +
               time + '\'); return false;">' + full + '</a>';
    });

// 2단계: 독립 HH:mm:ss[.fff] (이미 <a> 안에 있는 것 제외)
html = html.replace(/(?<![>"'\w\-:\/])(\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?)(?![<\w])/g,
    function(match, time) {
        return '<a class="log-timestamp-link" href="#" onclick="logTimestampInterop.onClick(\'' +
               time + '\'); return false;">' + time + '</a>';
    });
```

### 3.4 JS Interop 객체 (_Layout.cshtml)

```javascript
window.logTimestampInterop = {
    _dotNetRef: null,
    init(dotNetRef) { this._dotNetRef = dotNetRef; },
    onClick(timestamp) {
        this._dotNetRef?.invokeMethodAsync('OnLogTimestampClick', timestamp);
    },
    onCitationClick(timestamp, keyword) {
        this._dotNetRef?.invokeMethodAsync('OnLogCitationClick', timestamp, keyword);
    },
    dispose() { this._dotNetRef = null; }
};
```

### 3.5 스크롤/하이라이트 헬퍼 JS (_Layout.cshtml)

```javascript
window.logTimestampNav = {
    scrollToTime(timestamp) {
        var id = 'lp-time-' + timestamp.replace(/:/g, '-').replace(/\./g, '-');
        var el = document.getElementById(id) ||
                 document.querySelector('[id^="' + id + '"]');
        if (el) {
            el.scrollIntoView({ behavior: 'smooth', block: 'center' });
            el.classList.add('lp-row-flash');
            setTimeout(() => el.classList.remove('lp-row-flash'), 2000);
        }
    },
    scrollToKeyword(keyword) {
        var rows = document.querySelectorAll('.lp-results-table tbody tr');
        for (var i = 0; i < rows.length; i++) {
            if (rows[i].textContent.indexOf(keyword.substring(0, 30)) !== -1) {
                rows[i].scrollIntoView({ behavior: 'smooth', block: 'center' });
                rows[i].classList.add('lp-row-flash');
                setTimeout(function(r) { r.classList.remove('lp-row-flash'); }, 2000, rows[i]);
                break;
            }
        }
    }
};
```

### 3.6 Blazor 콜백 (Index.razor)

```csharp
// LogAnalyzerPane @ref
<LogAnalyzerPane @ref="_logPaneRef" ... />
private LogAnalyzerPane? _logPaneRef;

// OnAfterRenderAsync에서 init
await JS.InvokeVoidAsync("logTimestampInterop.init", _dotNetRef);

// DisposeAsync에서 cleanup
try { await JS.InvokeVoidAsync("logTimestampInterop.dispose"); } catch { }

// JSInvokable 콜백
[JSInvokable]
public async Task OnLogTimestampClick(string timestamp)
{
    _activePanel = PanelKind.Log;
    StateHasChanged();
    await Task.Delay(100);
    if (_logPaneRef != null)
        await _logPaneRef.NavigateToTimestamp(timestamp);
}
```

### 3.7 LogAnalyzerPane 네비게이션 메서드

```csharp
public async Task NavigateToTimestamp(string timestamp)
{
    _highlightedTime = timestamp;
    _timePreset = "24h";
    ApplyTimePreset();
    await ExecuteSearch();
    _subTab = "results";
    StateHasChanged();
    await Task.Delay(150);
    await JS.InvokeVoidAsync("logTimestampNav.scrollToTime", timestamp);
}
```

### 3.8 CSS 스타일

```css
/* 타임스탬프 링크 (amber, 로그탭 accent 통일) */
.log-timestamp-link {
    color: #d97706;
    background: #fffbeb;
    font-family: var(--code-font);
    padding: 1px 4px;
    border-radius: 3px;
    border-bottom: 1px dashed #d97706;
    text-decoration: none;
    cursor: pointer;
}

/* 행 하이라이트 + flash 애니메이션 */
.lp-row-highlighted { background: #fef3c7 !important; border-left: 3px solid #d97706; }
.lp-row-flash { animation: flash-highlight 2s ease-out; }
@keyframes flash-highlight {
    0% { background: #fde68a; }
    100% { background: transparent; }
}
```

---

## 4. Feature 3: 로그 인용 블록 클릭 네비게이션

### 4.1 개요

LLM 분석 응답에서 로그 인용 패턴(`[14:23:45] ServiceName [ERR] message...`)을 감지하여 클릭 가능한 블록으로 변환. 클릭 시 Log Panel에서 해당 키워드로 검색하고 결과를 하이라이트.

### 4.2 감지 패턴 (2종)

**패턴 1 — 로그 라인 인용:**
```
[HH:mm:ss(.fff)] ServiceName [ERR|WRN|INF|DBG] message text...
```
→ 타임스탬프 + 키워드(메시지)로 Log Panel 검색

**패턴 2 — 상위 오류 인용:**
```
[N회] ServiceName: error message...
```
→ 키워드(에러 메시지)로 Log Panel 검색

### 4.3 Markdown 렌더러 변환 (_Layout.cshtml)

```javascript
// 패턴 1: [HH:mm:ss] Service [LEVEL] message
html = html.replace(
    /\[(\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?)\]\s*([\w.]+)\s*\[(ERR|WRN|INF|DBG)\]\s*([^<\n]{3,})/g,
    function(match, time, svc, level, msg) {
        var keyword = msg.trim().substring(0, 80);
        var levelLower = level.toLowerCase();
        return '<a class="log-citation-link log-cite-' + levelLower + '" href="#" ' +
               'onclick="logTimestampInterop.onCitationClick(\'' + time + '\', \'' + keyword + '\'); return false;">' +
               '<span class="log-cite-time">' + time + '</span> ' +
               '<span class="log-cite-svc">' + svc + '</span> ' +
               '<span class="lp-level-badge lp-badge-' + levelLower + '">' + level + '</span> ' +
               msg + '</a>';
    });

// 패턴 2: [N회] Service: message
html = html.replace(
    /\[(\d+)회\]\s*([\w.]+):\s*([^<\n]{3,})/g,
    function(match, count, svc, msg) {
        var keyword = msg.trim().substring(0, 80);
        return '<a class="log-citation-link" href="#" ' +
               'onclick="logTimestampInterop.onCitationClick(\'\', \'' + keyword + '\'); return false;">' +
               '<span class="log-cite-count">' + count + '회</span> ' +
               '<span class="log-cite-svc">' + svc + '</span> ' +
               msg + '</a>';
    });
```

### 4.4 Blazor 콜백 (Index.razor)

```csharp
[JSInvokable]
public async Task OnLogCitationClick(string timestamp, string keyword)
{
    _activePanel = PanelKind.Log;
    StateHasChanged();
    await Task.Delay(100);
    if (_logPaneRef != null)
        await _logPaneRef.NavigateToCitation(timestamp, keyword);
}
```

### 4.5 LogAnalyzerPane.NavigateToCitation

```csharp
public async Task NavigateToCitation(string timestamp, string keyword)
{
    if (!string.IsNullOrWhiteSpace(keyword)) _keyword = keyword;
    _highlightedTime = !string.IsNullOrWhiteSpace(timestamp) ? timestamp : null;
    _timePreset = "24h";
    ApplyTimePreset();
    await ExecuteSearch();
    _subTab = "results";
    StateHasChanged();
    await Task.Delay(150);
    if (!string.IsNullOrWhiteSpace(timestamp))
        await JS.InvokeVoidAsync("logTimestampNav.scrollToTime", timestamp);
    else if (!string.IsNullOrWhiteSpace(keyword))
        await JS.InvokeVoidAsync("logTimestampNav.scrollToKeyword", keyword);
}
```

### 4.6 CSS 스타일

```css
.log-citation-link {
    display: block;
    padding: 8px 12px;
    margin: 6px 0;
    border-radius: 6px;
    border-left: 3px solid #d1d5db;
    background: #f9fafb;
    text-decoration: none;
    color: #374151;
    font-size: 13px;
    font-family: var(--code-font);
    line-height: 1.5;
    cursor: pointer;
    transition: background 0.15s, border-color 0.15s;
}
.log-citation-link:hover { background: #f3f4f6; }

/* Level-specific left border colors */
.log-cite-err { border-left-color: #ef4444; }
.log-cite-wrn { border-left-color: #f59e0b; }
.log-cite-inf { border-left-color: #3b82f6; }
.log-cite-dbg { border-left-color: #9ca3af; }

/* Sub-element styles */
.log-cite-time { color: #d97706; font-weight: 600; }
.log-cite-svc  { color: #2563eb; font-weight: 600; }
.log-cite-count { color: #dc2626; font-weight: 700; }
```

---

## 5. Bug Fix: 대화 목록 더블탭 문제

### 5.1 증상

모바일에서 대화 목록 항목을 선택하면 채팅 패널로 즉시 전환되지 않고, 한 번 더 터치해야 비로소 채팅이 표시됨.

### 5.2 원인 분석

| # | 원인 | 영향 |
|---|------|------|
| 1 | `SetActivePanel(PanelKind.Chat)`이 `await SwitchConversation()` **이후**에 호출됨 | Redis 로딩 지연 시 패널 전환이 늦어짐 |
| 2 | `SwitchConversation` 내 same-conversation 체크에서 early return 시 `SetActivePanel` 미도달 | 같은 대화 재선택 시 패널 전환 불가 |
| 3 | Redis 로딩 중 예외 발생 시 `SetActivePanel`까지 도달 불가 | 네트워크 오류 시 UI 무응답 |

### 5.3 수정

```csharp
// Before (inline lambda)
@onclick="async () => { await SwitchConversation(conv.Id); SetActivePanel(PanelKind.Chat); }"

// After (dedicated method)
@onclick="() => SelectConversation(conv.Id)"

private async Task SelectConversation(string convId)
{
    // 1. 패널 전환을 먼저 실행 (즉각 시각적 피드백)
    _activePanel = PanelKind.Chat;
    StateHasChanged();

    // 2. 데이터 로딩은 비동기로 후속 처리
    await SwitchConversation(convId);
}
```

추가로 `SwitchConversation` 내 Redis 로딩에 try/catch 추가:
```csharp
try {
    var detail = await ChatService.GetConversationAsync(EquipmentId, convId);
    // ... load messages
} catch (Exception ex) {
    _connectionError = $"대화 로딩 실패: {ex.Message}";
}
```

---

## 6. Feature 4: 채팅 메시지 영역 컴팩트 + 개별 컬러 + 수직 정렬

### 6.1 컴팩트 레이아웃

| Property | Before | After | 변화 |
|----------|--------|-------|------|
| `.msg-row` padding | `24px 0` | `14px 0` | -42% |
| `.msg-wrapper` gap | `16px` | `10px` | -38% |
| `.msg-wrapper` padding | `0 24px` | `0 20px` | -17% |
| `.msg-sender` font-size | `14px` | `13px` | -7% |
| `.msg-sender` margin-bottom | `6px` | `2px` | -67% |
| `.msg-actions` margin-top | `8px` | `4px` | -50% |
| `.msg-actions` gap | `4px` | `2px` | -50% |
| `.action-btn` size | `30×30px` | `28×28px` | -7% |
| `.msg-avatar` padding-top | `2px` | `0` | removed |
| `.msg-error` margin-top | `8px` | `4px` | -50% |

전체적으로 메시지 간 여백을 40~50% 축소하여 한 화면에 더 많은 대화 내용을 표시.

### 6.2 다양한 컬러 (Sender + Action Buttons)

**Sender Name 컬러:**

| 요소 | 색상 | 의미 |
|------|------|------|
| `.msg-user-row .msg-sender` | `#2563eb` (blue) | 사용자 발화 |
| `.msg-assistant-row .msg-sender` | `#c2693d` (copper) | AI 응답 (아바타 색상과 통일) |

**Action Button 개별 컬러 (hover 시):**

| 버튼 | CSS Class | Hover 색상 | Hover 배경 | Active 색상 |
|------|-----------|-----------|-----------|------------|
| 편집 | `.action-btn-edit` | `#2563eb` (blue) | `#eef4ff` | — |
| 복사 | `.action-btn-copy` | `#059669` (green) | `#ecfdf5` | `#22c55e` (copied) |
| 좋아요 | `.action-btn-like` | `#2563eb` (blue) | `#eef4ff` | `#2563eb` |
| 싫어요 | `.action-btn-dislike` | `#dc2626` (red) | `#fef2f2` | `#dc2626` |
| TTS | `.action-btn-tts` | `#7c3aed` (purple) | `#f5f3ff` | `#7c3aed` |
| 참조 | `.action-btn-cite` | `#d97706` (amber) | `#fffbeb` | — |
| 재생성 | `.action-btn-regen` | `#059669` (green) | `#ecfdf5` | — |

### 6.3 수직 정렬 (Avatar 통일)

| 요소 | Before | After |
|------|--------|-------|
| `.avatar` (기본) | `28×28px` | `28×28px` (유지) |
| `.avatar-assistant` | `32×32px` (비대칭) | `28×28px` (통일) |
| `.avatar-assistant svg` | `24×24px` | `18×18px` (CSS 강제 축소) |
| `.msg-avatar` padding-top | `2px` | `0` (제거) |

사용자/AI 아바타를 동일한 28px 원형으로 통일하여 메시지 왼쪽 정렬 라인을 깔끔하게 유지.

---

## 7. 변경 파일 요약

| # | File | Type | Description |
|---|------|------|-------------|
| 1 | `wwwroot/css/app.css` | MOD | 7가지 컬러 개선 + 타임스탬프/인용 링크 CSS + 컴팩트 레이아웃 + 액션 버튼 개별 컬러 + 아바타 통일 |
| 2 | `Pages/_Layout.cshtml` | MOD | 타임스탬프 regex 2종 + 로그 인용 regex 2종 + logTimestampInterop JS + logTimestampNav JS |
| 3 | `Pages/Index.razor` | MOD | @ref LogAnalyzerPane + JSInvokable 콜백 2종 + init/dispose + SelectConversation 더블탭 수정 + 액션 버튼 CSS 클래스 추가 |
| 4 | `Shared/LogAnalyzerPane.razor` | MOD | NavigateToTimestamp + NavigateToCitation + 행 ID/하이라이트 + _highlightedTime 필드 |

---

## 8. 검증 체크리스트

- [x] `dotnet build` — 0 errors, 0 warnings
- [ ] 사용자 메시지 행: 연한 블루(`#eef4ff`) 배경
- [ ] 탭 바: 탭별 개별 accent 컬러 (blue/green/amber/purple)
- [ ] 전송 버튼: 블루 그라디언트
- [ ] 입력 포커스: 블루 glow
- [ ] 힌트 카드: 좌측 블루 accent 라인
- [ ] AI 아바타: 구리색 glow
- [ ] 코드 블록: 우측 상단 언어 라벨 badge
- [ ] LLM 응답 내 타임스탬프: amber 링크, 클릭 → Log Panel 열림 + 스크롤
- [ ] 로그 인용 블록: 클릭 → Log Panel 검색 + 하이라이트
- [ ] 대화 목록 선택: 한 번 탭으로 즉시 채팅 패널 전환
- [ ] 메시지 영역: 컴팩트 간격, sender name 컬러 구분
- [ ] 액션 버튼: hover 시 개별 컬러 (copy=green, like=blue, dislike=red, tts=purple, cite=amber, regen=green)
- [ ] 아바타: 사용자/AI 모두 28px 원형, 수직 정렬 통일
- [ ] 데스크톱/모바일 양쪽 정상 동작
- [ ] 기존 citation, KaTeX, 코드 하이라이팅 regression 없음

---

## 9. docx 반영 체크리스트

- [ ] Revision History 테이블에 v4.85-P3 행 추가
- [ ] Section 9.2 Panel Tab Bar에 탭별 accent 컬러 설명 추가
- [ ] Section 9.3 Empty State에 힌트 카드 accent 라인 추가
- [ ] Section 9.4 Input Area에 전송 버튼 그라디언트, 포커스 glow 설명 추가
- [ ] Section 9.5 Messages Area에 컴팩트 레이아웃, sender 컬러, 액션 버튼 개별 컬러, 아바타 통일 설명 추가
- [ ] Section 9.5에 코드 블록 언어 라벨 badge 설명 추가
- [ ] Section 9.6.2 Log Analyzer Pane에 타임스탬프 네비게이션, 인용 블록 네비게이션 설명 추가
- [ ] Section 9.x에 타임스탬프/인용 클릭 → Log Panel 연동 JS Interop 아키텍처 설명 추가

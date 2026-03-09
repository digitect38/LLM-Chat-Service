# v4.86 Design Spec — FabWise Rebranding + Chat UI Modernization

> **Purpose:** FabCopilot → FabWise 리브랜딩 및 ChatGPT/Claude 스타일 채팅 UI 현대화
> **Date:** 2026-03-10
> **Scope:** WebClient 전체 브랜딩, Chat UI 레이아웃, VisualFactoryHome 홈페이지 헤더

---

## 1. Revision History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| v4.86 | 2026-03-10 | **FabWise Rebranding + Chat UI Modernization** — (1) Fab Copilot → FabWise 전체 리브랜딩 (타이틀, 파비콘, 웰컴 로고, 채팅 발신자명, 면책 문구). (2) Eagle 로고 도입 (FabTrust 통일). (3) ChatGPT/Claude 스타일 채팅 버블 UI (사용자: 우측 정렬 라운드 버블, AI: 좌측 투명 배경). (4) 아바타/발신자명 제거. (5) VisualFactoryHome 헤더 확대 + AI Copilot → FabWise 메뉴 변경 + 국기/데모버튼 푸터 이동. (6) CSS 캐시 무효화 개선. | |

---

## 2. FabWise Rebranding

### 2.1 브랜딩 변경 요약

| Element | Before | After |
|---------|--------|-------|
| 페이지 타이틀 `<title>` | Fab Copilot | FabWise |
| 파비콘 | `favicon.ico` (기본) | `images/eagle_32x32.png` (Eagle) |
| 웰컴 로고 | SVG 별 모양 (48px, 주황 배경) | Eagle PNG (128px, 투명 배경, Samsung Blue 필터) |
| 채팅 발신자명 | "나" / "Fab Copilot" | 제거 (버블 UI로 구분) |
| AI 아바타 | SVG 별 (주황 그라디언트 배경) | 제거 (버블 UI) |
| 면책 문구 | "Fab Copilot은..." | "FabWise은..." |

### 2.2 Eagle 로고 에셋

| File | Size | Usage |
|------|------|-------|
| `wwwroot/images/eagle_32x32.png` | 32×32 | 파비콘 |
| `wwwroot/images/eagle_64x64.png` | 64×64 | (보관용) |
| `wwwroot/images/eagle_256x256.png` | 256×256 | 웰컴 화면 로고 |

### 2.3 웰컴 로고 스타일

```css
.empty-logo {
    width: 128px; height: 128px;
    background: transparent;
}
.welcome-eagle {
    width: 128px; height: 128px;
    filter: brightness(0) saturate(100%) invert(12%) sepia(89%)
            saturate(5765%) hue-rotate(230deg) brightness(68%) contrast(120%);
    /* Samsung Blue #1428A0 */
}
```

---

## 3. Chat UI Modernization (ChatGPT/Claude 스타일)

### 3.1 레이아웃 변경

| Element | Before | After |
|---------|--------|-------|
| 사용자 메시지 | 좌측 정렬, 아바타+이름 포함, 블루 틴트 배경 행 | **우측 정렬**, 라운드 버블, 아바타/이름 제거 |
| AI 응답 | 좌측 정렬, 아바타+이름 포함, 흰색 배경 행 | **좌측 정렬**, 투명 배경, 아바타/이름 제거 |
| 메시지 행 배경 | 사용자: `#eef4ff`, AI: `#ffffff` | 양쪽 모두 `transparent` |

### 3.2 사용자 메시지 버블

```css
.msg-user-row .msg-wrapper { justify-content: flex-end; }
.msg-user-row .user-text {
    background: #e8eaf6;        /* 연한 라벤더 */
    color: #1a1a1a;
    padding: 10px 16px;
    border-radius: 18px 18px 4px 18px;  /* 우하단 꼬리 */
}
```

### 3.3 AI 응답 (투명)

```css
.msg-assistant-row .msg-wrapper { justify-content: flex-start; }
.msg-assistant-row .msg-content {
    background: transparent;
    padding: 4px 0;
}
```

### 3.4 제거된 UI 요소

- `.msg-avatar` 영역 (HTML에서 제거)
- `.msg-sender` 표시 (HTML에서 제거)
- `.avatar-user`, `.avatar-assistant` (CSS 유지, 미사용)

---

## 4. 힌트 카드 스타일 변경

| Property | Before | After |
|----------|--------|-------|
| `border-left` | `3px solid #2563eb` | 제거 |
| `.empty-logo box-shadow` | `0 4px 20px rgba(217,119,87,0.3)` | `none` |

---

## 5. VisualFactoryHome 홈페이지 변경

### 5.1 헤더 확대

| Element | Before | After |
|---------|--------|-------|
| `.nav-row` min-height | 72px | 72px (조정 완료) |
| `.brand-text` | 1.25rem | 2.8rem |
| `.brand-mark-wrap` | 44×44px | 80×40px (가로 유지, 높이 클리핑) |
| `.brand-mark` | 34×34px | 77×77px (120% 확대) |
| Logo 배경 | 그라디언트 | transparent |
| Logo 테두리 | 1px solid | none |
| `.nav-links a` | 0.9rem / 500 | 1.5rem / 600 |
| `.nav-links` gap | 32px | 48px |
| `.btn-small` | 10px 20px / 0.85rem | 16px 32px / 1.3rem |

### 5.2 메뉴 변경

| Item | Before | After |
|------|--------|-------|
| 마지막 메뉴 항목 | "AI Copilot" (4개 언어) | "FabWise" (모든 언어 통일) |

### 5.3 푸터로 이동한 요소

- 국기 언어 전환기 (`.floating-lang`): 헤더 → 푸터 하단
- "Request Demo" 버튼 (`.btn-small`): 헤더 → 푸터 (국기 옆)

```css
.footer-lang {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 20px;
    margin-top: 40px;
    padding-top: 24px;
    border-top: 1px solid var(--glass-border);
}
```

### 5.4 반응형 (모바일)

| Breakpoint | Element | Value |
|------------|---------|-------|
| 980px | `.nav-row` | min-height: 60px |
| 980px | `.brand-text` | 2rem |
| 980px | `.nav-links a` | 1.25rem |
| 620px | `.brand-text` | 2.2rem |
| 620px | `.nav-links a` | 1.1rem / 600 |
| 620px | `.brand-mark-wrap` | 60×30px |

### 5.5 i18n (script.js)

`nav.copilot` 키: 4개 언어 모두 "FabWise"로 통일 (en, ko, ja, zh)

### 5.6 캐시 무효화

- `styles.css?v=20260310c`
- `script.js?v=20260310b`

---

## 6. CSS 캐시 개선 (WebClient)

`Program.cs`에 `StaticFileOptions` 추가:
```csharp
app.UseStaticFiles(new StaticFileOptions {
    OnPrepareResponse = ctx => {
        ctx.Context.Response.Headers["Cache-Control"] = "no-cache";
    }
});
```
기존 `asp-append-version="true"` (파일 해시 기반)에 추가로, 브라우저가 항상 ETag 기반 재검증을 수행하도록 함.

---

## 7. 변경 파일 요약

### WebClient (LLM-Chat-Service)

| # | File | Description |
|---|------|-------------|
| 1 | `Pages/_Layout.cshtml` | `<title>` FabWise, eagle 파비콘, 스와이프 핑거트래킹, 텍스트 선택 가드 |
| 2 | `Pages/Index.razor` | 아바타/발신자명 제거, eagle 로고, "FabWise" 리브랜딩 |
| 3 | `wwwroot/css/app.css` | 버블 UI, 투명 AI 배경, 힌트/로고 그림자 제거, Samsung Blue eagle |
| 4 | `Program.cs` | Cache-Control: no-cache 헤더 |
| 5 | `wwwroot/images/eagle_*.png` | Eagle 로고 에셋 (32, 64, 256px) |

### VisualFactoryHome

| # | File | Description |
|---|------|-------------|
| 1 | `index.html` | AI Copilot → FabWise, 국기/데모 푸터 이동, 캐시 버전 업 |
| 2 | `styles.css` | 헤더 확대, 로고 투명/클리핑, 푸터 언어 전환기 |
| 3 | `script.js` | i18n nav.copilot → "FabWise" (4개 언어) |

---

## 8. 검증

1. `dotnet build` — 0 errors
2. 웰컴 화면: 128px Samsung Blue eagle, "FabWise" 타이틀
3. 채팅: 사용자 우측 라벤더 버블, AI 좌측 투명 배경
4. 아바타/발신자명 미표시
5. 브라우저 탭: "FabWise" + eagle 파비콘
6. VisualFactoryHome: 확대된 헤더, "FabWise" 메뉴, 푸터 국기+데모
7. 모바일: 반응형 헤더, 스와이프 정상
8. 기존 기능 regression 없음 (citation, KaTeX, TTS, 로그 분석)

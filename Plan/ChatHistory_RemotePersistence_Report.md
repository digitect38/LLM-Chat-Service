# Chat History Remote Storage Persistence - Implementation Report

**Feature:** Remote Storage Persistence for Chat History
**Date:** 2026-02-21
**Status:** Implementation Complete, Build Verified
**Files Changed:** 3

---

## 1. Problem Statement

The WebClient sidebar chat history was purely in-memory - every page refresh or browser navigation wiped the conversation list entirely. Despite this, the backend **already persisted every conversation to Redis** through two independent write paths:

- **ConnectionManager** (ChatGateway) saves user messages via `IConversationStore.AppendMessageAsync()`
- **ChatStreamRelayService** (ChatGateway) saves assistant responses via `IConversationStore.AppendMessageAsync()`

The data existed in Redis but the WebClient had no retrieval mechanism. This implementation bridges that gap by adding a REST API layer and wiring the UI to load on startup.

---

## 2. Architecture Overview

### 2.1 Existing Infrastructure (Unchanged)

```
Redis Storage Layer
  Key: fabcopilot:conv:{conversationId}        -> Full Conversation JSON
  Key: fabcopilot:equip:{equipmentId}:conversations -> Sorted Set (score = LastUpdatedAt)
```

**Domain Models** (in `FabCopilot.Contracts`):

| Model | Location | Fields |
|-------|----------|--------|
| `Conversation` | `Contracts/Models/Conversation.cs` | `ConversationId`, `EquipmentId`, `CreatedAt`, `LastUpdatedAt`, `Messages[]` |
| `ChatMessage` | `Contracts/Models/ChatMessage.cs` | `Role` (enum), `Text`, `Timestamp`, `Attachments?`, `ToolResultRefs?` |
| `MessageRole` | `Contracts/Enums/MessageRole.cs` | `User=0`, `Assistant=1`, `System=2`, `Tool=3` |

**Store Interface** (in `FabCopilot.Redis`):

```csharp
public interface IConversationStore
{
    Task<Conversation?> GetAsync(string conversationId, CancellationToken ct = default);
    Task SaveAsync(Conversation conversation, CancellationToken ct = default);
    Task AppendMessageAsync(string conversationId, ChatMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<Conversation>> GetByEquipmentAsync(string equipmentId, int limit = 20, CancellationToken ct = default);
}
```

`IConversationStore` was already registered in ChatGateway's DI container via `AddFabRedis(configuration)`.

### 2.2 New Data Flow

```
┌─────────────┐         ┌──────────────────┐         ┌───────┐
│  WebClient   │  HTTP   │   ChatGateway    │  Redis   │ Redis │
│  (Blazor)    │────────>│   (REST API)     │────────>│       │
│              │<────────│                  │<────────│       │
│  Index.razor │  JSON   │  Program.cs      │  IConv   │       │
│  ChatService │         │  MapGet(...)     │  Store   │       │
└─────────────┘         └──────────────────┘         └───────┘
```

**Page Load Flow:**
1. `Index.razor.OnInitializedAsync()` calls `ChatService.GetConversationListAsync(equipmentId)`
2. ChatService issues `GET /api/conversations/{equipmentId}` to ChatGateway
3. ChatGateway calls `IConversationStore.GetByEquipmentAsync(equipmentId, limit: 30)`
4. Redis returns up to 30 conversations ordered by `LastUpdatedAt` descending
5. Gateway maps each to a lightweight summary `{conversationId, title, lastUpdated, messageCount}`
6. WebClient populates the sidebar `_conversationHistory` list

**Conversation Click Flow:**
1. User clicks a sidebar item, triggering `SwitchConversation(convId)`
2. If messages are already cached in-memory, display immediately (no network call)
3. Otherwise, `ChatService.GetConversationAsync(equipmentId, convId)` issues `GET /api/conversations/{equipmentId}/{conversationId}`
4. ChatGateway calls `IConversationStore.GetAsync(conversationId)`, validates `EquipmentId` match
5. Full `Conversation` with all messages returned as JSON
6. WebClient renders messages, caches them in the sidebar item for subsequent clicks

**Equipment Switch Flow:**
1. User changes the equipment dropdown, triggering the `EquipmentId` property setter
2. `OnEquipmentChangedAsync()` saves current conversation, clears messages, disconnects WebSocket
3. Calls `LoadConversationHistoryAsync()` for the new equipment
4. Sidebar repopulates with the new equipment's conversation history

---

## 3. Changes by File

### 3.1 ChatGateway - `Program.cs`

**File:** `src/Services/FabCopilot.ChatGateway/Program.cs`
**Lines Added:** 127-153 (2 new endpoints) + 1 using directive

#### Endpoint 1: List Conversations (Lines 128-144)

```
GET /api/conversations/{equipmentId}
```

| Aspect | Detail |
|--------|--------|
| DI Injection | `IConversationStore store` (auto-resolved from DI) |
| Store Call | `GetByEquipmentAsync(equipmentId, limit: 30)` |
| Title Logic | First message with `Role == MessageRole.User`, truncated to 30 characters |
| Fallback Title | `"새 대화"` if no user message exists |
| Response | `200 OK` with JSON array |

Response shape:
```json
[
  {
    "conversationId": "abc123",
    "title": "CMP 패드 교체 주기는 어떻게 되나요?...",
    "lastUpdated": "2026-02-21T09:30:00Z",
    "messageCount": 4
  }
]
```

Design decision: The list endpoint intentionally returns **only summaries** (no message bodies). This keeps the sidebar load fast even with 30 conversations, each potentially containing many long messages.

#### Endpoint 2: Get Full Conversation (Lines 146-153)

```
GET /api/conversations/{equipmentId}/{conversationId}
```

| Aspect | Detail |
|--------|--------|
| DI Injection | `IConversationStore store` |
| Store Call | `GetAsync(conversationId)` |
| Validation | Verifies `conversation.EquipmentId` matches `equipmentId` (case-insensitive) |
| Not Found | Returns `404` if conversation is null or equipment mismatch |
| Response | `200 OK` with full `Conversation` JSON including all messages |

The equipment ID cross-check prevents a user from loading conversations belonging to a different equipment by guessing conversation IDs.

#### Added Using Directive

```csharp
using FabCopilot.Contracts.Enums;  // for MessageRole.User in title extraction
```

---

### 3.2 ChatService - `ChatService.cs`

**File:** `src/Client/FabCopilot.WebClient/Services/ChatService.cs`
**Lines Added:** 213-291 (2 methods + 3 DTO classes)

#### Method 1: `GetConversationListAsync` (Lines 213-228)

```csharp
public async Task<List<ConversationSummary>> GetConversationListAsync(string equipmentId)
```

- Calls `GET /api/conversations/{equipmentId}` via the existing `_httpClient`
- Uses `Uri.EscapeDataString()` for URL-safe equipment IDs
- Returns empty list on any failure (logged at Warning level)
- Graceful degradation: if ChatGateway is unreachable, sidebar simply shows "대화 기록 없음"

#### Method 2: `GetConversationAsync` (Lines 230-245)

```csharp
public async Task<ConversationDetail?> GetConversationAsync(string equipmentId, string conversationId)
```

- Calls `GET /api/conversations/{equipmentId}/{conversationId}`
- Returns `null` on non-success HTTP status (including 404)
- Returns `null` on any exception (logged at Warning level)
- Both parameters URL-escaped

#### DTO Classes (Lines 254-291)

Three DTOs added in the `FabCopilot.WebClient.Services` namespace:

| Class | Purpose | Fields |
|-------|---------|--------|
| `ConversationSummary` | Sidebar listing item | `ConversationId`, `Title`, `LastUpdated`, `MessageCount` |
| `ConversationDetail` | Full conversation load | `ConversationId`, `EquipmentId`, `Messages[]` |
| `ConversationDetailMessage` | Individual message | `Role` (int), `Text`, `Timestamp` |

All properties use `[JsonPropertyName]` attributes for explicit JSON mapping, avoiding reliance on naming policies.

**JSON Serialization Note:** `ConversationDetailMessage.Role` is typed as `int` (not `MessageRole` enum) because the WebClient project doesn't reference the Contracts enum types directly for deserialization. The integer mapping is:
- `0` = User
- `1` = Assistant
- `2` = System
- `3` = Tool

This matches `System.Text.Json`'s default enum-as-integer serialization used throughout the codebase (no `JsonStringEnumConverter` is registered anywhere).

---

### 3.3 Index.razor

**File:** `src/Client/FabCopilot.WebClient/Pages/Index.razor`
**Changes:** Lifecycle method upgrade, 3 new methods, 1 property refactor, 1 field addition, sidebar loading state

#### 3.3.1 Lifecycle: `OnInitialized` -> `OnInitializedAsync` (Lines 353-368)

**Before:**
```csharp
protected override void OnInitialized()
{
    // ... sync setup only
}
```

**After:**
```csharp
protected override async Task OnInitializedAsync()
{
    // ... same sync setup ...
    _equipmentId = /* from config */;  // backing field, not property (avoids triggering change handler)
    await LoadConversationHistoryAsync();
}
```

Key detail: The init sets `_equipmentId` (backing field) instead of `EquipmentId` (property) to avoid triggering `OnEquipmentChangedAsync()` during startup, since `LoadConversationHistoryAsync()` is called explicitly right after.

#### 3.3.2 Equipment Change Detection (Lines 295-305, 499-507)

The `EquipmentId` auto-property was replaced with a manually-implemented property with change detection:

```csharp
private string _equipmentId = "CMP01";
private string EquipmentId
{
    get => _equipmentId;
    set
    {
        if (_equipmentId == value) return;
        _equipmentId = value;
        _ = OnEquipmentChangedAsync();  // fire-and-forget (standard Blazor pattern)
    }
}
```

`OnEquipmentChangedAsync()` (Line 499):
1. Saves current conversation to sidebar (in-memory)
2. Clears the message area
3. Generates a new conversation ID
4. Disconnects the WebSocket (old equipment)
5. Reloads conversation history for the new equipment

The fire-and-forget `_ = OnEquipmentChangedAsync()` is the standard Blazor pattern for property setters that can't be async. The method calls `StateHasChanged()` internally via `LoadConversationHistoryAsync`.

#### 3.3.3 `LoadConversationHistoryAsync` (Lines 473-497)

```csharp
private async Task LoadConversationHistoryAsync()
```

- Sets `_isLoadingHistory = true` and calls `StateHasChanged()` to show loading indicator
- Calls `ChatService.GetConversationListAsync(EquipmentId)`
- Clears existing `_conversationHistory` and rebuilds from summaries
- Messages initialized as empty `[]` (lazy-loaded on click)
- Wrapped in try/catch/finally to ensure `_isLoadingHistory` is always reset

#### 3.3.4 `SwitchConversation` Upgrade (Lines 394-443)

**Before:** Synchronous, only read from in-memory cache.
**After:** Async with two-tier loading strategy:

1. **Cache hit path** (fast): If `target.Messages.Count > 0`, messages are already in memory from a previous load or from `SaveCurrentToHistory()`. Display immediately with no network call.

2. **Cache miss path** (lazy load): Call `ChatService.GetConversationAsync()`, then:
   - Map each `ConversationDetailMessage` to `ChatMessageViewModel`
   - Render Markdown for assistant messages via `RenderMarkdownAsync()`
   - Cache the loaded messages back into `target.Messages` for future clicks
   - Scroll to bottom after rendering

#### 3.3.5 Sidebar Loading State (Lines 25-28)

Added a three-state sidebar display:

```razor
@if (_isLoadingHistory)
    "기록 로딩 중..."       // Loading state
else if (_conversationHistory.Count == 0)
    "대화 기록 없음"        // Empty state
else
    @foreach (var conv in _conversationHistory) ...  // List state
```

---

## 4. JSON Serialization Compatibility Analysis

A critical cross-cutting concern is that the ChatGateway (server) and WebClient (client) must agree on JSON format. Verified across the entire serialization chain:

### 4.1 Serialization Chain

```
Redis (stored JSON) -> RedisConversationStore (deserialize) -> Conversation object
  -> ASP.NET Minimal API Results.Ok() (serialize) -> HTTP JSON response
  -> ChatService HttpClient (deserialize) -> ConversationDetail/ConversationSummary
```

### 4.2 Property Name Matching

| Server Property | Server JSON Name | Client DTO Property | Client JSON Attribute | Match |
|----------------|-----------------|---------------------|----------------------|-------|
| `Conversation.ConversationId` | `"conversationId"` | `ConversationDetail.ConversationId` | `"conversationId"` | OK |
| `Conversation.EquipmentId` | `"equipmentId"` | `ConversationDetail.EquipmentId` | `"equipmentId"` | OK |
| `Conversation.Messages` | `"messages"` | `ConversationDetail.Messages` | `"messages"` | OK |
| `ChatMessage.Role` | `0, 1, 2, 3` (int) | `ConversationDetailMessage.Role` | `int` | OK |
| `ChatMessage.Text` | `"text"` | `ConversationDetailMessage.Text` | `"text"` | OK |
| `ChatMessage.Timestamp` | `"timestamp"` | `ConversationDetailMessage.Timestamp` | `"timestamp"` | OK |
| Anonymous `.conversationId` | `"conversationId"` | `ConversationSummary.ConversationId` | `"conversationId"` | OK |
| Anonymous `.title` | `"title"` | `ConversationSummary.Title` | `"title"` | OK |
| Anonymous `.lastUpdated` | `"lastUpdated"` | `ConversationSummary.LastUpdated` | `"lastUpdated"` | OK |
| Anonymous `.messageCount` | `"messageCount"` | `ConversationSummary.MessageCount` | `"messageCount"` | OK |

### 4.3 Enum Serialization

- `MessageRole` enum has no `[JsonConverter]` attribute
- No `JsonStringEnumConverter` is registered in any `JsonSerializerOptions` across the codebase
- `System.Text.Json` default: enums serialize as integers
- `RedisConversationStore.JsonOptions`: `CamelCase` naming, no enum converter
- `ConnectionManager.JsonOptions`: `CamelCase` naming, no enum converter
- Server sends `"role": 0` for User, `"role": 1` for Assistant
- Client `ConversationDetailMessage.Role` is `int` - direct match

---

## 5. Security Considerations

| Concern | Mitigation | Location |
|---------|-----------|----------|
| Equipment ID injection in URL | `Uri.EscapeDataString()` on both `equipmentId` and `conversationId` | `ChatService.cs:217,234` |
| Cross-equipment data access | Server validates `conversation.EquipmentId.Equals(equipmentId, OrdinalIgnoreCase)` | `Program.cs:149` |
| Conversation ID guessing | Conversation IDs are 32-char hex GUIDs (`Guid.NewGuid().ToString("N")`) | `Index.razor:309` |
| No authentication | Same as existing endpoints (on-prem deployment, no auth layer) | Pre-existing design |

---

## 6. Build & Test Verification

### 6.1 Compilation

```
dotnet build -> 0 CS compilation errors
```

The only build errors are `MSB3021`/`MSB3027` file-lock errors caused by running ChatGateway (PID 60132) and WebClient (PID 44716) processes holding locks on their output EXEs. These are not code errors - they resolve by restarting the services.

### 6.2 Existing Test Suite

```
dotnet test tests/FabCopilot.RagPipeline.Tests/
  Passed: 933  |  Failed: 0  |  Skipped: 0  |  Duration: 169ms
```

All 933 existing tests pass with zero regressions. The test suite covers RAG pipeline functionality; no ChatGateway endpoint tests exist in the current project.

### 6.3 Manual Verification Steps

After restarting the services:

1. **Sidebar loads on page open**: Open WebClient at `http://localhost:5010` - sidebar should show previously stored conversations for the default equipment
2. **Page refresh persistence**: Send a chat message, refresh the page - the conversation should appear in the sidebar
3. **Lazy load on click**: Click a sidebar conversation - messages load from Redis and render with Markdown
4. **Cache on re-click**: Click away then click the same conversation again - messages display instantly from cache (no network call)
5. **Equipment switch**: Change the equipment dropdown - sidebar refreshes with that equipment's conversations
6. **New chat**: Click "+" button - current conversation saves to sidebar, chat area clears
7. **Graceful degradation**: If Redis/ChatGateway is down, sidebar shows "대화 기록 없음" (no crash)

---

## 7. File Inventory

| File | Lines Changed | Type |
|------|--------------|------|
| `src/Services/FabCopilot.ChatGateway/Program.cs` | +28 | 2 REST endpoints + 1 using |
| `src/Client/FabCopilot.WebClient/Services/ChatService.cs` | +73 | 2 HTTP methods + 3 DTOs |
| `src/Client/FabCopilot.WebClient/Pages/Index.razor` | +80 (net) | Lifecycle, lazy-load, equipment change, loading state |

No new files created. No existing infrastructure modified. No configuration changes required.

---

## 8. Dependencies (All Pre-Existing)

| Dependency | Usage | Already Registered |
|-----------|-------|-------------------|
| `IConversationStore` | Redis conversation queries | `AddFabRedis()` in ChatGateway |
| `HttpClient` | REST calls from WebClient | Instance field in ChatService |
| `MessageRole` enum | Title extraction filter | `FabCopilot.Contracts.Enums` |
| `Conversation` model | Serialized response | `FabCopilot.Contracts.Models` |

No new NuGet packages. No new DI registrations. No new configuration keys.

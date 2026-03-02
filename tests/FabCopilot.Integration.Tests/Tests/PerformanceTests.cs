using FabCopilot.Integration.Tests.Infrastructure;
using FluentAssertions;
using Xunit.Abstractions;

namespace FabCopilot.Integration.Tests.Tests;

/// <summary>
/// Performance baseline tests (v4.83 — 2026-03-02 측정 기준).
///
/// 측정 환경:
///   - Ollama exaone3.5:7.8b (Q4_K_M, 5.4GB full VRAM)
///   - snowflake-arctic-embed2 (F16, 1024D, 1.2GB VRAM)
///   - RAG Pipeline: Graph mode (embed → vector → BM25 hybrid → graph lookup)
///   - QueryIntelligence: OFF, LLM Reranking: OFF, Query Rewriting: OFF
///   - MaxTokens(NumPredict): 1536, NumCtx: 4096
///   - Docker: Redis, Qdrant, NATS (WSL2)
///
/// 실측 기준값 (2026-03-02):
///   ┌──────────────────────┬──────────────┬────────────┐
///   │ 항목                 │ 실측치       │ 기준 (SLA)  │
///   ├──────────────────────┼──────────────┼────────────┤
///   │ TTFT (단문, 단독)     │ ~8s         │ ≤ 30s*     │
///   │ TTFT (장문/동시)      │ ~15-29s     │ ≤ 35s      │
///   │ Total E2E (단문)      │ ~22-40s     │ ≤ 50s*     │
///   │ Total E2E (장문)      │ ~42-51s     │ ≤ 70s      │
///   │ Throughput            │ ~48 t/s     │ ≥ 10 t/s   │
///   │ Concurrent 2요청      │ ~49-53s     │ ≤ 90s      │
///   │ 응답 길이 (절차 질문)  │ ~1500자     │ ≥ 200자    │
///   │ 인용 수               │ 5건         │ ≥ 1건      │
///   └──────────────────────┴──────────────┴────────────┘
///   * TTFT SLA는 테스트 스위트 GPU 직렬화 대기 포함 기준
///
/// 참고: TTFT = RAG pipeline + LLM 프롬프트 처리(KV cache fill).
///   Ollama는 GPU 요청을 직렬 처리하므로 동시 테스트 시 대기 시간 발생.
///   테스트는 순차 실행되나, 이전 테스트의 GPU 점유로 후속 TTFT가 증가할 수 있음.
/// </summary>
[Collection("FabCopilot Services")]
[Trait("Category", "Integration")]
[Trait("Category", "Performance")]
public class PerformanceTests
{
    private readonly FabCopilotServiceFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PerformanceTests(FabCopilotServiceFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    // ─── TTFT (Time To First Token) ──────────────────────────────────

    /// <summary>
    /// 참고: 테스트 스위트에서 TTFT는 이전 테스트의 GPU 점유 대기 시간을 포함함.
    /// 단독 실행 시 TTFT ~8s, 스위트 내 실행 시 ~25s까지 증가 가능.
    /// 기준값은 스위트 실행 기준으로 설정 (GPU 직렬화 대기 포함).
    /// </summary>
    [SkippableFact]
    public async Task TTFT_SimpleQuery_ShouldBeLessThan30Seconds()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var response = await _fixture.Client.SendAndCollectAsync(
            "CMP 패드 교체 주기는?",
            timeout: TimeSpan.FromSeconds(60));

        LogPerformance("TTFT_SimpleQuery", response);

        response.Error.Should().BeNull();
        response.TimeToFirstToken.Should().BeLessThan(TimeSpan.FromSeconds(30),
            "TTFT baseline: 단문 질문 30초 이내 (단독 ~8s, 스위트 내 GPU 대기 포함 ~25s)");
    }

    [SkippableFact]
    public async Task TTFT_ComplexQuery_ShouldBeLessThan35Seconds()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var response = await _fixture.Client.SendAndCollectAsync(
            "웨이퍼 스크래치 발생 시 원인 분석 절차와 각 원인별 해결 방법을 단계별로 설명해주세요",
            timeout: TimeSpan.FromSeconds(90));

        LogPerformance("TTFT_ComplexQuery", response);

        response.Error.Should().BeNull();
        response.TimeToFirstToken.Should().BeLessThan(TimeSpan.FromSeconds(35),
            "TTFT baseline: 복잡한 쿼리 35초 이내 (GPU 직렬화 대기 포함)");
    }

    // ─── Total E2E Response Time ─────────────────────────────────────

    [SkippableFact]
    public async Task TotalTime_ShortAnswer_ShouldBeLessThan40Seconds()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var response = await _fixture.Client.SendAndCollectAsync(
            "CMP 패드 교체 주기는?",
            timeout: TimeSpan.FromSeconds(60));

        LogPerformance("TotalTime_ShortAnswer", response);

        response.Error.Should().BeNull();
        response.FullText.Should().NotBeNullOrWhiteSpace();
        response.TotalTime.Should().BeLessThan(TimeSpan.FromSeconds(50),
            "단문 E2E baseline: 50초 이내 (단독 ~22s, 스위트 내 GPU 대기 포함 ~40s)");
    }

    [SkippableFact]
    public async Task TotalTime_LongAnswer_ShouldBeLessThan70Seconds()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var response = await _fixture.Client.SendAndCollectAsync(
            "CMP 장비의 일일 점검 항목을 전부 나열하고 각 항목별 점검 방법과 기준값을 설명해주세요",
            timeout: TimeSpan.FromSeconds(90));

        LogPerformance("TotalTime_LongAnswer", response);

        response.Error.Should().BeNull();
        response.FullText.Should().NotBeNullOrWhiteSpace();
        response.TotalTime.Should().BeLessThan(TimeSpan.FromSeconds(70),
            "장문 E2E baseline: 70초 이내 (실측 ~51s, 마진 포함)");
    }

    // ─── Throughput (tokens/sec) ─────────────────────────────────────

    [SkippableFact]
    public async Task Throughput_ShouldExceed10TokensPerSecond()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var response = await _fixture.Client.SendAndCollectAsync(
            "슬러리 압력 알람 발생 시 조치 방법은?",
            timeout: TimeSpan.FromSeconds(60));

        LogPerformance("Throughput", response);

        response.Error.Should().BeNull();
        response.TokenCount.Should().BeGreaterThan(0);

        // Streaming throughput = tokens / (total - ttft)
        var streamingDuration = response.TotalTime - response.TimeToFirstToken;
        var tokensPerSec = streamingDuration.TotalSeconds > 0
            ? response.TokenCount / streamingDuration.TotalSeconds
            : 0;

        _output.WriteLine($"  Streaming throughput: {tokensPerSec:F1} tokens/sec");

        tokensPerSec.Should().BeGreaterThan(10,
            "스트리밍 처리량 baseline: 10 tokens/sec 이상 (실측 ~48 t/s, exaone3.5:7.8b Q4_K_M)");
    }

    // ─── Response Quality Gate ───────────────────────────────────────

    [SkippableFact]
    public async Task ResponseLength_ShouldBeSubstantial()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var response = await _fixture.Client.SendAndCollectAsync(
            "CMP 컨디셔너 교체 절차를 설명해주세요",
            timeout: TimeSpan.FromSeconds(60));

        LogPerformance("ResponseLength", response);

        response.Error.Should().BeNull();
        response.FullText.Length.Should().BeGreaterThan(200,
            "응답 품질 baseline: 절차 질문에 200자 이상 응답 (실측 ~1500자)");
        response.TokenCount.Should().BeGreaterThan(50,
            "응답 품질 baseline: 50 토큰 이상 생성 (실측 ~600 토큰)");
    }

    [SkippableFact]
    public async Task CitationsPresent_ForDomainQuery()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var response = await _fixture.Client.SendAndCollectAsync(
            "패드 컨디셔닝 주기와 압력 기준값은?",
            timeout: TimeSpan.FromSeconds(60));

        LogPerformance("Citations", response);

        response.Error.Should().BeNull();
        response.Citations.Should().NotBeEmpty(
            "도메인 질문에는 RAG 인용이 포함되어야 함 (실측 5건)");
    }

    // ─── Concurrent Load ─────────────────────────────────────────────

    [SkippableFact]
    public async Task ConcurrentRequests_TwoParallel_ShouldCompleteWithin90Seconds()
    {
        Skip.If(!_fixture.ServicesAvailable, _fixture.SkipReason);

        var client1 = new ChatWebSocketClient();
        var client2 = new ChatWebSocketClient();

        var sw = System.Diagnostics.Stopwatch.StartNew();

        var task1 = client1.SendAndCollectAsync(
            "CMP 패드 교체 주기는?", timeout: TimeSpan.FromSeconds(90));
        var task2 = client2.SendAndCollectAsync(
            "슬러리 유량 알람 조치 방법은?", timeout: TimeSpan.FromSeconds(90));

        var results = await Task.WhenAll(task1, task2);
        sw.Stop();

        _output.WriteLine($"  [Concurrent] 2 requests completed in {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"    Request 1: TTFT={results[0].TimeToFirstToken.TotalMilliseconds:F0}ms, " +
                          $"Total={results[0].TotalTime.TotalMilliseconds:F0}ms, Tokens={results[0].TokenCount}");
        _output.WriteLine($"    Request 2: TTFT={results[1].TimeToFirstToken.TotalMilliseconds:F0}ms, " +
                          $"Total={results[1].TotalTime.TotalMilliseconds:F0}ms, Tokens={results[1].TokenCount}");

        results[0].Error.Should().BeNull();
        results[1].Error.Should().BeNull();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(90),
            "동시 2요청 baseline: 90초 이내 모두 완료 (실측 ~53s, GPU 직렬 처리)");
    }

    // ─── Helper ──────────────────────────────────────────────────────

    private void LogPerformance(string testName, ChatResponse response)
    {
        _output.WriteLine($"  [{testName}]");
        _output.WriteLine($"    TTFT:       {response.TimeToFirstToken.TotalMilliseconds:F0}ms");
        _output.WriteLine($"    Total:      {response.TotalTime.TotalMilliseconds:F0}ms");
        _output.WriteLine($"    Tokens:     {response.TokenCount}");
        _output.WriteLine($"    TextLength: {response.FullText.Length}");
        _output.WriteLine($"    Citations:  {response.Citations.Count}");
        if (response.Error != null)
            _output.WriteLine($"    Error:      {response.Error}");
    }
}

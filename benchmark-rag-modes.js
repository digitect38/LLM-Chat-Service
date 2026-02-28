const WebSocket = require('ws');

const QUERIES = [
  { id: 'Q1', text: 'CMP 패드 교체 시기는?', category: 'Procedure' },
  { id: 'Q2', text: '슬러리 공급 압력 이상 시 알람코드와 대처 방법은?', category: 'Troubleshooting' },
  { id: 'Q3', text: '웨이퍼 스크래치 발생 원인과 해결방법', category: 'Troubleshooting' },
  { id: 'Q4', text: 'CMP 장비의 일일 점검 항목은?', category: 'Procedure' },
  { id: 'Q5', text: 'MRR이 낮아지는 원인은?', category: 'Troubleshooting' },
];

const MODE = process.argv[2] || 'Advanced+Graph';
const WS_URL = 'ws://localhost:5000/ws/chat/CMP-001';
const TIMEOUT_MS = 120000;

function sendQuery(query) {
  return new Promise((resolve) => {
    const ws = new WebSocket(WS_URL);
    const startTime = Date.now();
    let firstTokenTime = null;
    let tokens = [];
    let citations = [];
    let done = false;

    const finish = () => {
      if (done) return;
      done = true;
      const endTime = Date.now();
      const fullText = tokens.join('');

      // Extract citations from response
      const citationMatches = fullText.match(/\(score: [\d.]+\)/g) || [];
      const scores = citationMatches.map(m => parseFloat(m.match(/[\d.]+/)[0]));

      resolve({
        queryId: query.id,
        query: query.text,
        category: query.category,
        totalTimeMs: endTime - startTime,
        ttftMs: firstTokenTime ? firstTokenTime - startTime : null,
        responseLength: fullText.length,
        citationCount: citationMatches.length,
        maxScore: scores.length > 0 ? Math.max(...scores) : 0,
        avgScore: scores.length > 0 ? (scores.reduce((a, b) => a + b, 0) / scores.length) : 0,
        response: fullText,
      });
      try { ws.close(); } catch (e) {}
    };

    ws.on('open', () => {
      ws.send(JSON.stringify({ userMessage: query.text, equipmentId: 'CMP-001' }));
    });

    ws.on('message', (data) => {
      try {
        const msg = JSON.parse(data.toString());
        if (msg.token) {
          if (!firstTokenTime) firstTokenTime = Date.now();
          tokens.push(msg.token);
        }
        if (msg.isComplete) finish();
      } catch (e) {}
    });

    ws.on('error', (err) => {
      console.error(`  [ERROR] ${query.id}: ${err.message}`);
      finish();
    });

    ws.on('close', () => finish());
    setTimeout(finish, TIMEOUT_MS);
  });
}

async function runBenchmark() {
  console.log(`\n${'='.repeat(70)}`);
  console.log(`  RAG Pipeline Benchmark — Mode: ${MODE}`);
  console.log(`${'='.repeat(70)}\n`);

  const results = [];

  for (const query of QUERIES) {
    process.stdout.write(`  ${query.id} [${query.category}] ${query.text.substring(0, 30)}...`);
    const result = await sendQuery(query);
    console.log(` ${result.totalTimeMs}ms, ${result.responseLength}chars, ${result.citationCount}cites`);
    results.push(result);

    // Brief pause between queries
    await new Promise(r => setTimeout(r, 1000));
  }

  // Summary table
  console.log(`\n${'─'.repeat(70)}`);
  console.log(`  SUMMARY — ${MODE}`);
  console.log(`${'─'.repeat(70)}`);
  console.log(`  ${'Query'.padEnd(8)} ${'Category'.padEnd(18)} ${'Total(ms)'.padStart(10)} ${'TTFT(ms)'.padStart(10)} ${'Chars'.padStart(8)} ${'Cites'.padStart(6)} ${'MaxScore'.padStart(9)}`);
  console.log(`  ${'─'.repeat(65)}`);

  let totalTime = 0, totalChars = 0, totalCites = 0, totalScores = [];

  for (const r of results) {
    console.log(`  ${r.queryId.padEnd(8)} ${r.category.padEnd(18)} ${String(r.totalTimeMs).padStart(10)} ${String(r.ttftMs || '-').padStart(10)} ${String(r.responseLength).padStart(8)} ${String(r.citationCount).padStart(6)} ${r.maxScore.toFixed(3).padStart(9)}`);
    totalTime += r.totalTimeMs;
    totalChars += r.responseLength;
    totalCites += r.citationCount;
    if (r.maxScore > 0) totalScores.push(r.maxScore);
  }

  const avgTime = Math.round(totalTime / results.length);
  const avgChars = Math.round(totalChars / results.length);
  const avgCites = (totalCites / results.length).toFixed(1);
  const avgMaxScore = totalScores.length > 0 ? (totalScores.reduce((a, b) => a + b, 0) / totalScores.length).toFixed(3) : '0';

  console.log(`  ${'─'.repeat(65)}`);
  console.log(`  ${'AVG'.padEnd(8)} ${''.padEnd(18)} ${String(avgTime).padStart(10)} ${''.padStart(10)} ${String(avgChars).padStart(8)} ${avgCites.padStart(6)} ${avgMaxScore.padStart(9)}`);
  console.log();

  // Output JSON for comparison
  const summary = {
    mode: MODE,
    timestamp: new Date().toISOString(),
    avgTotalMs: avgTime,
    avgResponseChars: avgChars,
    avgCitations: parseFloat(avgCites),
    avgMaxScore: parseFloat(avgMaxScore),
    queries: results.map(r => ({
      id: r.queryId, category: r.category,
      totalMs: r.totalTimeMs, ttftMs: r.ttftMs,
      chars: r.responseLength, cites: r.citationCount, maxScore: r.maxScore,
    })),
  };
  require('fs').writeFileSync(`benchmark-result-${MODE.replace(/\+/g, '-').toLowerCase()}.json`, JSON.stringify(summary, null, 2));
  console.log(`  Results saved to benchmark-result-${MODE.replace(/\+/g, '-').toLowerCase()}.json\n`);
}

runBenchmark().catch(console.error);

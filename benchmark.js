/**
 * Benchmark: Ollama vs HuggingFace (TGI+TEI) mode comparison.
 * Tests embedding latency, LLM generation speed, and end-to-end pipeline.
 *
 * Usage:
 *   node benchmark.js ollama     -- benchmark Ollama endpoints
 *   node benchmark.js huggingface -- benchmark TGI+TEI endpoints
 *   node benchmark.js e2e-ollama  -- end-to-end via WebSocket (Ollama mode)
 *   node benchmark.js e2e-hf      -- end-to-end via WebSocket (HF mode)
 */
const http = require('http');
const https = require('https');
const WebSocket = require('ws');

const ITERATIONS = 5;
const TEST_TEXTS = [
    "CMP pad replacement procedure",
    "What is chemical mechanical planarization?",
    "Slurry flow rate optimization for oxide CMP process"
];

const CHAT_PROMPTS = [
    { role: "user", content: "What is CMP?" },
];

const CHAT_PROMPTS_KR = [
    { role: "user", content: "CMP 패드 교체 주기를 알려줘" },
];

// ---- HTTP helpers ----
function httpPost(url, body, timeout = 60000) {
    return new Promise((resolve, reject) => {
        const parsed = new URL(url);
        const options = {
            hostname: parsed.hostname,
            port: parsed.port,
            path: parsed.pathname,
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            timeout
        };
        const req = http.request(options, (res) => {
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => resolve({ status: res.statusCode, body: data }));
        });
        req.on('error', reject);
        req.on('timeout', () => { req.destroy(); reject(new Error('timeout')); });
        req.write(JSON.stringify(body));
        req.end();
    });
}

function httpPostStream(url, body, timeout = 120000) {
    return new Promise((resolve, reject) => {
        const parsed = new URL(url);
        const options = {
            hostname: parsed.hostname,
            port: parsed.port,
            path: parsed.pathname,
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            timeout
        };
        const req = http.request(options, (res) => {
            let tokens = [];
            let firstTokenTime = null;
            const startTime = Date.now();
            res.on('data', chunk => {
                const lines = chunk.toString().split('\n');
                for (const line of lines) {
                    if (!line.startsWith('data: ')) continue;
                    const data = line.slice(6);
                    if (data === '[DONE]') continue;
                    try {
                        const parsed = JSON.parse(data);
                        const content = parsed.choices?.[0]?.delta?.content;
                        if (content) {
                            if (!firstTokenTime) firstTokenTime = Date.now();
                            tokens.push(content);
                        }
                    } catch {}
                }
            });
            res.on('end', () => {
                resolve({
                    firstTokenMs: firstTokenTime ? firstTokenTime - startTime : null,
                    totalMs: Date.now() - startTime,
                    tokenCount: tokens.length,
                    text: tokens.join('')
                });
            });
        });
        req.on('error', reject);
        req.on('timeout', () => { req.destroy(); reject(new Error('timeout')); });
        req.write(JSON.stringify(body));
        req.end();
    });
}

// ---- Ollama streaming ----
function ollamaStreamChat(baseUrl, model, messages, timeout = 120000) {
    return new Promise((resolve, reject) => {
        const parsed = new URL(baseUrl + '/api/chat');
        const body = { model, messages, stream: true };
        const options = {
            hostname: parsed.hostname,
            port: parsed.port,
            path: parsed.pathname,
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            timeout
        };
        const req = http.request(options, (res) => {
            let tokens = [];
            let firstTokenTime = null;
            const startTime = Date.now();
            let buffer = '';
            res.on('data', chunk => {
                buffer += chunk.toString();
                const lines = buffer.split('\n');
                buffer = lines.pop(); // keep incomplete line
                for (const line of lines) {
                    if (!line.trim()) continue;
                    try {
                        const parsed = JSON.parse(line);
                        if (parsed.message?.content) {
                            if (!firstTokenTime) firstTokenTime = Date.now();
                            tokens.push(parsed.message.content);
                        }
                    } catch {}
                }
            });
            res.on('end', () => {
                resolve({
                    firstTokenMs: firstTokenTime ? firstTokenTime - startTime : null,
                    totalMs: Date.now() - startTime,
                    tokenCount: tokens.length,
                    text: tokens.join('')
                });
            });
        });
        req.on('error', reject);
        req.on('timeout', () => { req.destroy(); reject(new Error('timeout')); });
        req.write(JSON.stringify(body));
        req.end();
    });
}

// ---- Benchmark functions ----
async function benchmarkEmbedding(name, fn) {
    console.log(`\n--- ${name}: Embedding Benchmark (${ITERATIONS} iterations x ${TEST_TEXTS.length} texts) ---`);
    const latencies = [];
    // warmup
    await fn(TEST_TEXTS[0]);

    for (let i = 0; i < ITERATIONS; i++) {
        for (const text of TEST_TEXTS) {
            const start = Date.now();
            const result = await fn(text);
            const elapsed = Date.now() - start;
            latencies.push(elapsed);
        }
    }
    const avg = latencies.reduce((a, b) => a + b, 0) / latencies.length;
    const min = Math.min(...latencies);
    const max = Math.max(...latencies);
    const p50 = latencies.sort((a, b) => a - b)[Math.floor(latencies.length * 0.5)];
    const p95 = latencies.sort((a, b) => a - b)[Math.floor(latencies.length * 0.95)];
    console.log(`  Avg: ${avg.toFixed(0)}ms | P50: ${p50}ms | P95: ${p95}ms | Min: ${min}ms | Max: ${max}ms`);
    return { avg, p50, p95, min, max };
}

async function benchmarkChat(name, fn) {
    console.log(`\n--- ${name}: Chat Generation Benchmark (${ITERATIONS} iterations) ---`);
    const results = [];
    // warmup
    await fn(CHAT_PROMPTS);

    for (let i = 0; i < ITERATIONS; i++) {
        const r = await fn(CHAT_PROMPTS_KR);
        results.push(r);
        const tps = r.tokenCount / (r.totalMs / 1000);
        console.log(`  [${i+1}] TTFT: ${r.firstTokenMs}ms | Total: ${r.totalMs}ms | Tokens: ${r.tokenCount} | TPS: ${tps.toFixed(1)} | Preview: ${r.text.slice(0, 60).replace(/\n/g, ' ')}...`);
    }
    const avgTTFT = results.reduce((a, b) => a + (b.firstTokenMs || 0), 0) / results.length;
    const avgTotal = results.reduce((a, b) => a + b.totalMs, 0) / results.length;
    const avgTokens = results.reduce((a, b) => a + b.tokenCount, 0) / results.length;
    const avgTPS = avgTokens / (avgTotal / 1000);
    console.log(`  --- Summary ---`);
    console.log(`  Avg TTFT: ${avgTTFT.toFixed(0)}ms | Avg Total: ${avgTotal.toFixed(0)}ms | Avg Tokens: ${avgTokens.toFixed(0)} | Avg TPS: ${avgTPS.toFixed(1)}`);
    return { avgTTFT, avgTotal, avgTokens, avgTPS };
}

async function benchmarkE2E(name, mode) {
    console.log(`\n--- ${name}: End-to-End WebSocket Benchmark (3 iterations) ---`);
    const results = [];

    for (let i = 0; i < 3; i++) {
        const r = await new Promise((resolve, reject) => {
            const ws = new WebSocket('ws://localhost:5000/ws/chat/CMP01');
            const convId = `bench-${mode}-${Date.now()}-${i}`;
            let chunks = [];
            let firstTokenTime = null;
            let startTime = null;

            ws.on('open', () => {
                startTime = Date.now();
                ws.send(JSON.stringify({
                    conversationId: convId,
                    equipmentId: 'CMP01',
                    userMessage: 'CMP pad replacement cycle',
                    searchMode: 'hybrid'
                }));
            });
            ws.on('message', (data) => {
                const chunk = JSON.parse(data.toString());
                chunks.push(chunk);
                if (chunk.token && !firstTokenTime) firstTokenTime = Date.now();
                if (chunk.isComplete) {
                    const totalMs = Date.now() - startTime;
                    const ttft = firstTokenTime ? firstTokenTime - startTime : null;
                    const tokenChunks = chunks.filter(c => c.token).length;
                    ws.close();
                    resolve({ ttft, totalMs, tokenChunks });
                }
            });
            ws.on('error', (e) => reject(e));
            setTimeout(() => { ws.close(); reject(new Error('timeout')); }, 120000);
        });
        results.push(r);
        console.log(`  [${i+1}] TTFT: ${r.ttft}ms | Total: ${r.totalMs}ms | Chunks: ${r.tokenChunks}`);
    }
    const avgTTFT = results.reduce((a, b) => a + (b.ttft || 0), 0) / results.length;
    const avgTotal = results.reduce((a, b) => a + b.totalMs, 0) / results.length;
    console.log(`  --- Summary ---`);
    console.log(`  Avg TTFT: ${avgTTFT.toFixed(0)}ms | Avg Total: ${avgTotal.toFixed(0)}ms`);
    return { avgTTFT, avgTotal };
}

// ---- Main ----
async function main() {
    const mode = process.argv[2] || 'ollama';

    if (mode === 'ollama') {
        // Ollama Embedding
        await benchmarkEmbedding('Ollama (snowflake-arctic-embed2)', async (text) => {
            const r = await httpPost('http://localhost:11434/api/embed', {
                model: 'snowflake-arctic-embed2', input: text
            });
            return JSON.parse(r.body);
        });

        // Ollama Chat
        await benchmarkChat('Ollama (exaone3.5:7.8b)', async (messages) => {
            return ollamaStreamChat('http://localhost:11434', 'exaone3.5:7.8b', messages);
        });
    }
    else if (mode === 'huggingface') {
        // TEI Embedding
        await benchmarkEmbedding('TEI (bge-m3)', async (text) => {
            const r = await httpPost('http://localhost:8080/embed', { inputs: text });
            return JSON.parse(r.body);
        });

        // TGI Chat
        await benchmarkChat('TGI (TinyLlama-1.1B)', async (messages) => {
            return httpPostStream('http://localhost:8000/v1/chat/completions', {
                model: 'default',
                messages,
                stream: true,
                max_tokens: 512,
                temperature: 0.1
            });
        });
    }
    else if (mode === 'e2e-ollama' || mode === 'e2e-hf') {
        await benchmarkE2E(`E2E (${mode})`, mode);
    }
    else {
        console.log('Usage: node benchmark.js [ollama|huggingface|e2e-ollama|e2e-hf]');
    }
}

main().catch(e => { console.error(e); process.exit(1); });

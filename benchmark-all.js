/**
 * Full benchmark: all viable LLM x Embedding combinations.
 * Tests direct API-level performance (not E2E pipeline).
 *
 * Combinations tested:
 *   1. Ollama EXAONE 7.8B  +  Ollama Arctic Embed2
 *   2. Ollama EXAONE 7.8B  +  TEI bge-m3
 *   3. TGI TinyLlama 1.1B  +  Ollama Arctic Embed2
 *   4. TGI TinyLlama 1.1B  +  TEI bge-m3
 *
 * Usage: node benchmark-all.js
 *   Expects: Ollama on :11434, TEI on :8080, TGI on :8000
 */
const http = require('http');

const EMBED_ITERS = 5;
const CHAT_ITERS = 3;

const EMBED_TEXTS = [
    "CMP pad replacement procedure and maintenance schedule",
    "What is chemical mechanical planarization process overview",
    "Slurry flow rate optimization for oxide CMP wafer polishing"
];

const CHAT_MESSAGES = [
    { role: "user", content: "CMP 패드 교체 주기를 알려줘" }
];

// ──── HTTP helpers ────
function post(url, body, timeout = 120000) {
    return new Promise((resolve, reject) => {
        const u = new URL(url);
        const req = http.request({
            hostname: u.hostname, port: u.port, path: u.pathname,
            method: 'POST', headers: { 'Content-Type': 'application/json' }, timeout
        }, res => {
            let d = ''; res.on('data', c => d += c); res.on('end', () => resolve({ status: res.statusCode, body: d }));
        });
        req.on('error', reject);
        req.on('timeout', () => { req.destroy(); reject(new Error('timeout')); });
        req.write(JSON.stringify(body)); req.end();
    });
}

function streamChat(url, body, parseChunk, timeout = 120000) {
    return new Promise((resolve, reject) => {
        const u = new URL(url);
        const req = http.request({
            hostname: u.hostname, port: u.port, path: u.pathname,
            method: 'POST', headers: { 'Content-Type': 'application/json' }, timeout
        }, res => {
            let tokens = [], firstMs = null;
            const t0 = Date.now();
            let buf = '';
            res.on('data', chunk => {
                buf += chunk.toString();
                const lines = buf.split('\n'); buf = lines.pop();
                for (const line of lines) {
                    const tok = parseChunk(line);
                    if (tok) { if (!firstMs) firstMs = Date.now(); tokens.push(tok); }
                }
            });
            res.on('end', () => resolve({
                ttft: firstMs ? firstMs - t0 : null, totalMs: Date.now() - t0,
                tokenCount: tokens.length, text: tokens.join('')
            }));
        });
        req.on('error', reject);
        req.on('timeout', () => { req.destroy(); reject(new Error('timeout')); });
        req.write(JSON.stringify(body)); req.end();
    });
}

// ──── Provider functions ────
async function ollamaEmbed(text) {
    const t0 = Date.now();
    await post('http://localhost:11434/api/embed', { model: 'snowflake-arctic-embed2', input: text });
    return Date.now() - t0;
}

async function teiEmbed(text) {
    const t0 = Date.now();
    await post('http://localhost:8080/embed', { inputs: text });
    return Date.now() - t0;
}

function ollamaChat(messages) {
    return streamChat('http://localhost:11434/api/chat',
        { model: 'exaone3.5:7.8b', messages, stream: true },
        line => {
            if (!line.trim()) return null;
            try { const p = JSON.parse(line); return p.message?.content || null; } catch { return null; }
        });
}

function tgiChat(messages) {
    return streamChat('http://localhost:8000/v1/chat/completions',
        { model: 'default', messages, stream: true, max_tokens: 512, temperature: 0.1 },
        line => {
            if (!line.startsWith('data: ')) return null;
            const d = line.slice(6); if (d === '[DONE]') return null;
            try { const p = JSON.parse(d); return p.choices?.[0]?.delta?.content || null; } catch { return null; }
        });
}

// ──── Benchmark runner ────
async function benchEmbed(name, fn) {
    const lats = [];
    await fn(EMBED_TEXTS[0]); // warmup
    for (let i = 0; i < EMBED_ITERS; i++)
        for (const t of EMBED_TEXTS) lats.push(await fn(t));
    lats.sort((a, b) => a - b);
    const avg = (lats.reduce((a, b) => a + b, 0) / lats.length).toFixed(0);
    const p50 = lats[Math.floor(lats.length * 0.5)];
    const p95 = lats[Math.floor(lats.length * 0.95)];
    return { name, avg: +avg, p50, p95, min: lats[0], max: lats[lats.length - 1] };
}

async function benchChat(name, fn) {
    await fn(CHAT_MESSAGES); // warmup
    const runs = [];
    for (let i = 0; i < CHAT_ITERS; i++) runs.push(await fn(CHAT_MESSAGES));
    const avgTTFT = (runs.reduce((a, r) => a + (r.ttft || 0), 0) / runs.length).toFixed(0);
    const avgTotal = (runs.reduce((a, r) => a + r.totalMs, 0) / runs.length).toFixed(0);
    const avgToks = (runs.reduce((a, r) => a + r.tokenCount, 0) / runs.length).toFixed(0);
    const avgTPS = (+avgToks / (+avgTotal / 1000)).toFixed(1);
    const preview = runs[0].text.slice(0, 60).replace(/\n/g, ' ');
    return { name, avgTTFT: +avgTTFT, avgTotal: +avgTotal, avgTokens: +avgToks, avgTPS: +avgTPS, preview };
}

// ──── Health checks ────
async function isHealthy(url) {
    try { const r = await post(url, {}, 3000).catch(() => null);
        return r && r.status < 500; } catch { return false; }
}

// ──── Main ────
async function main() {
    console.log('=== FabCopilot Benchmark: All Combinations ===\n');

    // Check which services are available
    const ollamaOk = await isHealthy('http://localhost:11434/api/tags');
    const teiOk = await isHealthy('http://localhost:8080/health');
    const tgiOk = await isHealthy('http://localhost:8000/health');

    console.log(`Services: Ollama=${ollamaOk ? 'UP' : 'DOWN'}  TEI=${teiOk ? 'UP' : 'DOWN'}  TGI=${tgiOk ? 'UP' : 'DOWN'}\n`);

    // ── Embedding benchmarks ──
    console.log('━━━ EMBEDDING BENCHMARKS ━━━');
    const embedResults = [];
    if (ollamaOk) embedResults.push(await benchEmbed('Ollama Arctic-Embed2', ollamaEmbed));
    if (teiOk) embedResults.push(await benchEmbed('TEI bge-m3', teiEmbed));

    console.log('');
    console.log('Provider              | Avg(ms) | P50  | P95  | Min  | Max');
    console.log('──────────────────────|─────────|──────|──────|──────|─────');
    for (const r of embedResults) {
        console.log(`${r.name.padEnd(22)}| ${String(r.avg).padStart(7)} | ${String(r.p50).padStart(4)} | ${String(r.p95).padStart(4)} | ${String(r.min).padStart(4)} | ${String(r.max).padStart(4)}`);
    }

    // ── Chat generation benchmarks ──
    console.log('\n━━━ CHAT GENERATION BENCHMARKS ━━━');
    const chatResults = [];
    if (ollamaOk) chatResults.push(await benchChat('Ollama EXAONE-7.8B', ollamaChat));
    if (tgiOk) chatResults.push(await benchChat('TGI TinyLlama-1.1B', tgiChat));

    console.log('');
    console.log('Provider              | TTFT(ms) | Total(ms) | Tokens | TPS   | Preview');
    console.log('──────────────────────|──────────|───────────|────────|───────|────────');
    for (const r of chatResults) {
        console.log(`${r.name.padEnd(22)}| ${String(r.avgTTFT).padStart(8)} | ${String(r.avgTotal).padStart(9)} | ${String(r.avgTokens).padStart(6)} | ${String(r.avgTPS).padStart(5)} | ${r.preview}...`);
    }

    // ── Combination matrix ──
    console.log('\n━━━ COMBINATION SUMMARY ━━━');
    console.log('');
    console.log('#  LLM                  | Embedding              | Embed Avg | Chat TPS | Notes');
    console.log('───────────────────────|────────────────────────|───────────|──────────|──────');

    let combo = 1;
    for (const c of chatResults) {
        for (const e of embedResults) {
            const notes = [];
            if (c.name.includes('EXAONE') && e.name.includes('TEI')) notes.push('Mixed mode');
            if (c.name.includes('TinyLlama') && e.name.includes('Ollama')) notes.push('Mixed mode');
            if (c.name.includes('EXAONE') && e.name.includes('Ollama')) notes.push('Spec: ollama mode');
            if (c.name.includes('TinyLlama') && e.name.includes('TEI')) notes.push('Spec: huggingface mode');
            console.log(`${String(combo++).padStart(1)}  ${c.name.padEnd(22)}| ${e.name.padEnd(22)} | ${String(e.avg + 'ms').padStart(9)} | ${String(c.avgTPS).padStart(8)} | ${notes.join(', ')}`);
        }
    }

    // ── VRAM snapshot ──
    console.log('\n━━━ GPU VRAM ━━━');
    try {
        const { execSync } = require('child_process');
        const vram = execSync('nvidia-smi --query-gpu=memory.used,memory.free,memory.total --format=csv,noheader,nounits').toString().trim();
        const [used, free, total] = vram.split(',').map(s => parseInt(s.trim()));
        console.log(`Used: ${used}MB | Free: ${free}MB | Total: ${total}MB`);
    } catch { console.log('nvidia-smi not available'); }

    console.log('\n=== Benchmark Complete ===');
}

main().catch(e => { console.error('Fatal:', e.message); process.exit(1); });

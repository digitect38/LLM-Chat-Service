/**
 * End-to-end test: TGI + TEI mode verification.
 * Sends a chat request via WebSocket and verifies a streamed response is received.
 *
 * Run: node test-tgi-mode.js
 */
const WebSocket = require('ws');

const ws = new WebSocket('ws://localhost:5000/ws/chat/CMP01');
const convId = 'tgi-test-' + Date.now();
let chunks = [];
let gotComplete = false;

ws.on('open', () => {
    console.log('[TGI-Test] Connected to ChatGateway');
    setTimeout(() => {
        console.log('[TGI-Test] Sending question (convId=' + convId + ')');
        ws.send(JSON.stringify({
            conversationId: convId,
            equipmentId: 'CMP01',
            userMessage: 'Hello, what is CMP?',
            searchMode: 'hybrid'
        }));
    }, 500);
});

ws.on('message', (data) => {
    const chunk = JSON.parse(data.toString());
    chunks.push(chunk);
    if (chunk.token) process.stdout.write(chunk.token);
    if (chunk.isComplete) {
        gotComplete = true;
        console.log('\n\n[TGI-Test] Stream complete (' + chunks.length + ' chunks)');
        const hasTokens = chunks.filter(c => c.token).length;
        console.log('[TGI-Test] Token chunks: ' + hasTokens);
        console.log('[TGI-Test] Result: ' + (hasTokens > 0 ? 'PASS' : 'FAIL'));
        ws.close();
        setTimeout(() => process.exit(hasTokens > 0 ? 0 : 1), 500);
    }
});

ws.on('error', (e) => {
    console.log('[TGI-Test] Error:', e.message);
    process.exit(1);
});

// Timeout after 120s
setTimeout(() => {
    if (!gotComplete) {
        console.log('\n[TGI-Test] Timeout - received ' + chunks.length + ' chunks so far');
        const hasTokens = chunks.filter(c => c.token).length;
        console.log('[TGI-Test] Token chunks: ' + hasTokens);
        console.log('[TGI-Test] Result: TIMEOUT');
        ws.close();
        process.exit(1);
    }
}, 120000);

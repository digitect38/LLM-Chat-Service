/**
 * Multi-client WebSocket routing test.
 * Verifies that two clients connected to the same equipmentId
 * each receive only their own conversation's response chunks.
 *
 * Run: node test-multi-client.js
 */
const WebSocket = require('ws');

const ws1 = new WebSocket('ws://localhost:5000/ws/chat/CMP01');
const ws2 = new WebSocket('ws://localhost:5000/ws/chat/CMP01');

let ws1Msgs = [], ws2Msgs = [];
let ws1Open = false, ws2Open = false;
const conv1 = 'test-conv-' + Date.now() + '-A';
const conv2 = 'test-conv-' + Date.now() + '-B';

ws1.on('open', () => { ws1Open = true; console.log('[Client1] Connected'); checkBothOpen(); });
ws2.on('open', () => { ws2Open = true; console.log('[Client2] Connected'); checkBothOpen(); });

ws1.on('message', (data) => {
    const chunk = JSON.parse(data.toString());
    ws1Msgs.push(chunk);
    if (chunk.token) process.stdout.write('1');
    if (chunk.isComplete) console.log('\n[Client1] Complete (' + ws1Msgs.length + ' chunks)');
});

ws2.on('message', (data) => {
    const chunk = JSON.parse(data.toString());
    ws2Msgs.push(chunk);
    if (chunk.token) process.stdout.write('2');
    if (chunk.isComplete) console.log('\n[Client2] Complete (' + ws2Msgs.length + ' chunks)');
});

ws1.on('error', (e) => console.log('[Client1] Error:', e.message));
ws2.on('error', (e) => console.log('[Client2] Error:', e.message));

let bothComplete = false;

function checkDone() {
    if (bothComplete) return;
    const c1Done = ws1Msgs.some(c => c.isComplete);
    const c2Done = ws2Msgs.some(c => c.isComplete);
    if (c1Done && c2Done) {
        bothComplete = true;
        printResults();
    }
}

// Check periodically
const checkInterval = setInterval(checkDone, 1000);

function checkBothOpen() {
    if (!ws1Open || !ws2Open) return;
    console.log('[Test] Both clients connected to CMP01');

    // Client 1 sends a question
    setTimeout(() => {
        console.log('[Client1] Sending question (conv=' + conv1 + ')');
        ws1.send(JSON.stringify({
            conversationId: conv1,
            equipmentId: 'CMP01',
            userMessage: '안녕',
            searchMode: 'hybrid'
        }));
    }, 500);

    // Client 2 sends a different question after delay
    setTimeout(() => {
        console.log('[Client2] Sending question (conv=' + conv2 + ')');
        ws2.send(JSON.stringify({
            conversationId: conv2,
            equipmentId: 'CMP01',
            userMessage: 'CMP 패드 교체 주기',
            searchMode: 'hybrid'
        }));
    }, 2000);

    // Timeout fallback
    setTimeout(() => {
        if (!bothComplete) {
            console.log('\n[Timeout] Not all responses received');
            printResults();
        }
    }, 180000);
}

function printResults() {
    clearInterval(checkInterval);
    console.log('\n=== RESULTS ===');
    console.log('Client1 received: ' + ws1Msgs.length + ' chunks');
    console.log('Client2 received: ' + ws2Msgs.length + ' chunks');

    const ws1Foreign = ws1Msgs.filter(c => c.conversationId && c.conversationId !== conv1);
    const ws2Foreign = ws2Msgs.filter(c => c.conversationId && c.conversationId !== conv2);

    console.log('Client1 foreign chunks: ' + ws1Foreign.length + (ws1Foreign.length === 0 ? ' (CORRECT)' : ' (BUG!)'));
    console.log('Client2 foreign chunks: ' + ws2Foreign.length + (ws2Foreign.length === 0 ? ' (CORRECT)' : ' (BUG!)'));

    if (ws1Msgs.length > 0 && ws2Msgs.length > 0 && ws1Foreign.length === 0 && ws2Foreign.length === 0) {
        console.log('\n=== PASS: Multi-client routing correct ===');
    } else if (ws1Msgs.length === 0 || ws2Msgs.length === 0) {
        console.log('\n=== INCONCLUSIVE: One client got no response ===');
    } else {
        console.log('\n=== FAIL: Routing issue ===');
    }

    ws1.close();
    ws2.close();
    setTimeout(() => process.exit(0), 1000);
}

/**
 * LaTeX rendering pipeline unit tests.
 * Tests the exact regex extraction + marked.parse logic from _Layout.cshtml.
 *
 * Run: npm install --no-save marked && node tests/FabCopilot.RagPipeline.Tests/latex-render-test.js
 */
var { marked } = require('marked');

var passed = 0;
var failed = 0;

function assert(condition, message) {
    if (condition) {
        passed++;
        console.log('  PASS: ' + message);
    } else {
        failed++;
        console.log('  FAIL: ' + message);
    }
}

function assertEqual(actual, expected, message) {
    assert(actual === expected, message + ' (got ' + actual + ', expected ' + expected + ')');
}

/** Exact same logic as _Layout.cshtml chatRenderer.renderMarkdown */
function extractMath(text) {
    var blockMath = [];
    var inlineMath = [];

    text = text.replace(/\\begin\{(equation|align|aligned|gather|gathered|multline|cases|bmatrix|pmatrix|vmatrix|matrix|array)\*?\}([\s\S]*?)\\end\{\1\*?\}/g, function (m) {
        blockMath.push(m);
        return '%%BLOCKMATH' + (blockMath.length - 1) + '%%';
    });
    text = text.replace(/\$\$([\s\S]*?)\$\$/g, function (m, f) {
        blockMath.push(f.trim());
        return '%%BLOCKMATH' + (blockMath.length - 1) + '%%';
    });
    text = text.replace(/\\\[([\s\S]*?)\\\]/g, function (m, f) {
        blockMath.push(f.trim());
        return '%%BLOCKMATH' + (blockMath.length - 1) + '%%';
    });
    text = text.replace(/\\\(([\s\S]*?)\\\)/g, function (m, f) {
        inlineMath.push(f.trim());
        return '%%INLINEMATH' + (inlineMath.length - 1) + '%%';
    });
    text = text.replace(/\$([^\$\n]+?)\$/g, function (m, f) {
        inlineMath.push(f.trim());
        return '%%INLINEMATH' + (inlineMath.length - 1) + '%%';
    });
    text = text.replace(/\\\[([\s\S]+)$/g, function (m, f) {
        blockMath.push(f.trim());
        return '%%BLOCKMATH' + (blockMath.length - 1) + '%%';
    });
    text = text.replace(/\\\(([\s\S]+)$/g, function (m, f) {
        inlineMath.push(f.trim());
        return '%%INLINEMATH' + (inlineMath.length - 1) + '%%';
    });

    return { text, blockMath, inlineMath };
}

function renderToHtml(text) {
    var r = extractMath(text);
    var html = marked.parse(r.text);
    html = html.replace(/%%BLOCKMATH(\d+)%%/g, function (m, i) {
        return '<span class="katex-block">[BLOCK:' + r.blockMath[parseInt(i)] + ']</span>';
    });
    html = html.replace(/%%INLINEMATH(\d+)%%/g, function (m, i) {
        return '<span class="katex-inline">[INLINE:' + r.inlineMath[parseInt(i)] + ']</span>';
    });
    return { html, blockMath: r.blockMath, inlineMath: r.inlineMath };
}

// ═══════════════════════════════════════════════════════════════
console.log('\n[Test Suite: LaTeX Extraction]');
// ═══════════════════════════════════════════════════════════════

(function testInlineMath_BackslashParen() {
    var r = extractMath('hello \\(x^2\\) world');
    assertEqual(r.inlineMath.length, 1, 'backslash-paren: 1 inline');
    assertEqual(r.inlineMath[0], 'x^2', 'backslash-paren: content');
})();

(function testInlineMath_Dollar() {
    var r = extractMath('hello $x^2$ world');
    assertEqual(r.inlineMath.length, 1, 'dollar: 1 inline');
    assertEqual(r.inlineMath[0], 'x^2', 'dollar: content');
})();

(function testBlockMath_BackslashBracket() {
    var r = extractMath('before\n\\[\nx = 1\n\\]\nafter');
    assertEqual(r.blockMath.length, 1, 'backslash-bracket: 1 block');
    assertEqual(r.blockMath[0], 'x = 1', 'backslash-bracket: content');
})();

(function testBlockMath_DoubleDollar() {
    var r = extractMath('before\n$$x = 1$$\nafter');
    assertEqual(r.blockMath.length, 1, 'double-dollar: 1 block');
    assertEqual(r.blockMath[0], 'x = 1', 'double-dollar: content');
})();

(function testBlockMath_BeginEnd() {
    var r = extractMath('\\begin{equation}E=mc^2\\end{equation}');
    assertEqual(r.blockMath.length, 1, 'begin-end: 1 block');
    assert(r.blockMath[0].includes('E=mc^2'), 'begin-end: content');
})();

(function testOrphanedBlockDelimiter() {
    var r = extractMath('text \\[ x = \\frac{1');
    assertEqual(r.blockMath.length, 1, 'orphaned \\[: 1 block');
    assertEqual(r.blockMath[0], 'x = \\frac{1', 'orphaned \\[: content captured');
})();

(function testOrphanedInlineDelimiter() {
    var r = extractMath('text \\( x^2');
    assertEqual(r.inlineMath.length, 1, 'orphaned \\(: 1 inline');
    assertEqual(r.inlineMath[0], 'x^2', 'orphaned \\(: content captured');
})();

// ═══════════════════════════════════════════════════════════════
console.log('\n[Test Suite: Quadratic Formula Derivation - List Structure]');
// ═══════════════════════════════════════════════════════════════

(function testQuadraticDerivation_ListNotBroken() {
    // Simulates the Korean RAG pipeline response format
    var text = `1. **Step 1**:
   - explanation \\(a\\)
   \\[
   x^2 + \\frac{b}{a}x = 0
   \\]
2. **Step 2**:
   - more text \\(b\\)
   \\[
   m = \\frac{b}{2a}
   \\]
3. **Step 3**:
   - final \\(x\\)
   \\[
   x = \\frac{-b \\pm \\sqrt{b^2 - 4ac}}{2a}
   \\]`;

    var r = renderToHtml(text);

    // List should NOT be fragmented
    var olCount = (r.html.match(/<ol/g) || []).length;
    assertEqual(olCount, 1, 'single <ol> list (not fragmented)');

    // Block math should be INSIDE <li> elements
    var blockInLi = (r.html.match(/<li>[\s\S]*?<span class="katex-block">/g) || []).length;
    assert(blockInLi >= 3, 'block math inside <li> (' + blockInLi + ' found, need >= 3)');

    // No standalone <p> breaks between list items
    var pBreaks = (r.html.match(/<\/ol>[\s\S]*?<span class="katex-block">/g) || []).length;
    assertEqual(pBreaks, 0, 'no block math in standalone <p> outside list');

    // All math extracted
    assertEqual(r.blockMath.length, 3, '3 block math expressions');
    assertEqual(r.inlineMath.length, 3, '3 inline math expressions');
})();

(function testQuadraticDerivation_FullKoreanResponse() {
    var text = `## \uC694\uC57D
\uC774\uCC28 \uBC29\uC815\uC2DD \\(ax^2 + bx + c = 0\\)\uC758 \uADFC\uC758 \uACF5\uC2DD

## \uC0C1\uC138
1. **\uBC29\uC815\uC2DD \uD45C\uC900\uD654**:
   - \uC8FC\uC5B4\uC9C4 \uBC29\uC815\uC2DD\uC744 \\(a\\)\uB85C \uB098\uB204\uC5B4:
   \\[
   x^2 + \\frac{b}{a}x + \\frac{c}{a} = 0
   \\]
2. **\uBCC0\uC218 \uCE58\uD658**:
   - \\(\\frac{b}{a}\\)\uB97C \uBCC0\uC218 \\(m\\)\uC73C\uB85C:
   \\[
   m = \\frac{b}{2a}
   \\]
   - \uBCC0\uD615:
   \\[
   (x + m)^2 = \\frac{c}{a}
   \\]
3. **\uADFC\uC758 \uACF5\uC2DD \uB3C4\uCD9C**:
   \\[
   x = \\frac{-b \\pm \\sqrt{b^2 - 4ac}}{2a}
   \\]`;

    var r = renderToHtml(text);

    // Headers
    assert(r.html.includes('<h2>'), 'has H2 headers');

    // List structure in 상세 section
    var olTags = r.html.match(/<ol/g) || [];
    assertEqual(olTags.length, 1, 'Korean: single <ol> in 상세 section');

    // All math extracted
    assertEqual(r.blockMath.length, 4, 'Korean: 4 block math');
    assertEqual(r.inlineMath.length, 4, 'Korean: 4 inline math');

    // Block math should be inside list items
    var blockInLi = (r.html.match(/<li>[\s\S]*?<span class="katex-block">/g) || []).length;
    assert(blockInLi >= 3, 'Korean: block math inside <li> (' + blockInLi + ')');
})();

(function testEnglishDerivation_AlsoWorks() {
    var text = `1. **Start with the standard form:**
   \\[
   ax^2 + bx + c = 0
   \\]

2. **Move constant term \\( c \\):**
   \\[
   ax^2 + bx = -c
   \\]

3. **Divide by \\( a \\):**
   \\[
   x^2 + \\frac{b}{a}x = -\\frac{c}{a}
   \\]`;

    var r = renderToHtml(text);

    var olCount = (r.html.match(/<ol/g) || []).length;
    assertEqual(olCount, 1, 'English: single <ol> list');
    assertEqual(r.blockMath.length, 3, 'English: 3 block math');
    assertEqual(r.inlineMath.length, 2, 'English: 2 inline math');
})();

(function testNestedSublist_WithBlockMath() {
    var text = `1. **Item**:
   - Sub-item:
     \\[
     E = mc^2
     \\]
   - Another sub-item
2. **Item 2**`;

    var r = renderToHtml(text);
    var olCount = (r.html.match(/<ol/g) || []).length;
    assertEqual(olCount, 1, 'nested sublist: single <ol>');
    assertEqual(r.blockMath.length, 1, 'nested sublist: 1 block math');
})();

// ═══════════════════════════════════════════════════════════════
console.log('\n[Test Suite: Edge Cases]');
// ═══════════════════════════════════════════════════════════════

(function testMixedFormats() {
    var r = extractMath('inline $a$ and \\(b\\), block $$c$$ and \\[d\\]');
    assertEqual(r.inlineMath.length, 2, 'mixed: 2 inline');
    assertEqual(r.blockMath.length, 2, 'mixed: 2 block');
})();

(function testBoxedFormula() {
    var r = extractMath('\\[\n\\boxed{x = \\frac{-b}{2a}}\n\\]');
    assertEqual(r.blockMath.length, 1, 'boxed: 1 block');
    assert(r.blockMath[0].includes('\\boxed'), 'boxed: contains \\boxed');
})();

(function testEmptyMathDelimiters() {
    var r = extractMath('\\(\\) and \\[\\]');
    // Empty delimiters should still be extracted (trimmed to empty)
    assertEqual(r.inlineMath.length, 1, 'empty inline captured');
    assertEqual(r.blockMath.length, 1, 'empty block captured');
})();

(function testConsecutiveBlockMath() {
    var text = `\\[
x = 1
\\]
\\[
y = 2
\\]`;
    var r = extractMath(text);
    assertEqual(r.blockMath.length, 2, 'consecutive: 2 blocks');
})();

// ═══════════════════════════════════════════════════════════════
console.log('\n' + '='.repeat(50));
console.log('Results: ' + passed + ' passed, ' + failed + ' failed');
console.log('='.repeat(50));
process.exit(failed > 0 ? 1 : 0);

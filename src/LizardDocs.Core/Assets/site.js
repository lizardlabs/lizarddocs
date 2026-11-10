/* Minimal, non-semantic code presentation and copy controls. */
(() => {
  const keywords = new Set(('class interface public private protected internal static readonly const var new return void string int bool decimal double float object null true false using namespace async await Task if else switch case default for foreach while do try catch finally throw ' +
    'function let import export from as typeof of extends implements def None True False and or not in with lambda pass yield select where insert into create table join on group by order end action returns require ensure nothing constructor dispose unmanaged needs concurrent all first any run consume for_each take skip repeat until times forever lazy data kind use use_if').toLowerCase().split(/\s+/));
  function highlightTokens(source) {
    const tokens = [];
    const pattern = /\/\/[^\r\n]*|\/\*[\s\S]*?\*\/|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`|\b(?:0[xX][0-9a-fA-F]+|[0-9]+(?:\.[0-9]+)?)\b|[A-Za-z_][A-Za-z0-9_]*/g;
    let position = 0;
    for (const match of source.matchAll(pattern)) {
      if (match.index > position) tokens.push({ text: source.slice(position, match.index), kind: null });
      const text = match[0];
      const kind = text.startsWith('//') || text.startsWith('/*') ? 'comment' : /^["'`]/.test(text) ? 'string' : /^[0-9]/.test(text) ? 'number' : keywords.has(text.toLowerCase()) ? 'keyword' : null;
      tokens.push({ text, kind }); position = match.index + text.length;
    }
    if (position < source.length) tokens.push({ text: source.slice(position), kind: null });
    return tokens;
  }
  function enhanceCodeBlocks(doc, host = globalThis) {
    for (const code of doc.querySelectorAll('pre code')) {
      if (code.dataset.csEnhanced === 'true') continue;
      const source = code.textContent;
      if (!code.classList.contains('language-text') && !code.classList.contains('language-plaintext')) {
        const fragment = doc.createDocumentFragment();
        for (const token of highlightTokens(source)) {
          if (token.kind) { const span = doc.createElement('span'); span.className = 'cs-' + token.kind; span.textContent = token.text; fragment.append(span); }
          else fragment.append(doc.createTextNode(token.text));
        }
        code.replaceChildren(fragment);
      }
      const button = doc.createElement('button'); button.type = 'button'; button.className = 'cs-copy'; button.textContent = 'Copy'; button.setAttribute('aria-label', 'Copy code');
      button.addEventListener('click', async () => {
        try { await host.navigator.clipboard.writeText(source); button.textContent = 'Copied'; }
        catch (_) {
          const selection = host.getSelection?.(); const range = doc.createRange(); range.selectNodeContents(code); selection?.removeAllRanges(); selection?.addRange(range); button.textContent = 'Select and copy';
        }
      });
      code.parentElement.prepend(button); code.dataset.csEnhanced = 'true';
    }
  }
  if (typeof module !== 'undefined' && module.exports) module.exports = { highlightTokens, enhanceCodeBlocks };
  if (typeof document !== 'undefined') enhanceCodeBlocks(document);
})();

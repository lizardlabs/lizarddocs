/* Browser presentation only. Building and validating the site uses .NET. */
(() => {
  function wordsFor(query) {
    return [...new Set(query.trim().toLowerCase().split(/\s+/).filter(Boolean))];
  }

  function escapePattern(text) {
    return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  }

  function compareScores(left, right) {
    for (let i = 0; i < left.length; ++i) {
      if (left[i] !== right[i]) return right[i] - left[i];
    }
    return 0;
  }

  function rankPages(pages, query) {
    const words = wordsFor(query);
    if (!words.length) return [];
    const phrase = query.trim().toLowerCase().replace(/\s+/g, ' ');
    const wordCharacter = /[\p{L}\p{N}\p{M}_]/u;
    const wholePatterns = words.map(word => new RegExp(
      (wordCharacter.test(word[0]) ? '(?<![\\p{L}\\p{N}\\p{M}_])' : '') + escapePattern(word) +
      (wordCharacter.test(word[word.length - 1]) ? '(?![\\p{L}\\p{N}\\p{M}_])' : ''), 'iu'));
    const hits = text => words.filter(word => text.includes(word)).length;
    const wholeHits = text => wholePatterns.filter(pattern => pattern.test(text)).length;
    return pages.map((page, order) => {
      const title = page.title.toLowerCase();
      const headings = page.headings || [];
      const searchable = [title, ...headings.map(heading => heading.text), page.text].join(' ').toLowerCase();
      if (!words.every(word => searchable.includes(word))) return null;
      let heading = null;
      let headingScore = [0, 0, 0, 0];
      for (const candidate of headings) {
        const text = candidate.text.toLowerCase();
        const score = [wholeHits(text), hits(text), Number(text === phrase), Number(text.includes(phrase))];
        if (compareScores(score, headingScore) < 0) {
          heading = candidate;
          headingScore = score;
        }
      }
      // Whole words use title/heading/body priority; fragments remain useful while typing.
      const titleHits = wholeHits(title);
      const score = [titleHits, titleHits ? Number(title === phrase) : 0,
        titleHits ? Number(title.includes(phrase)) : 0, headingScore[0], wholeHits(page.text),
        hits(title), ...headingScore.slice(1)];
      return { page, heading, score, order };
    }).filter(Boolean).sort((left, right) => compareScores(left.score, right.score) || left.order - right.order);
  }

  function highlightParts(text, query) {
    const words = wordsFor(query).sort((left, right) => right.length - left.length);
    if (!words.length) return [{ text, match: false }];
    // Query punctuation is literal, including language operators such as ?? and .
    const pattern = new RegExp(words.map(escapePattern).join('|'), 'giu');
    const parts = [];
    let start = 0;
    for (const match of text.matchAll(pattern)) {
      if (match.index > start) parts.push({ text: text.slice(start, match.index), match: false });
      parts.push({ text: match[0], match: true });
      start = match.index + match[0].length;
    }
    if (start < text.length) parts.push({ text: text.slice(start), match: false });
    return parts;
  }

  function appendHighlighted(element, text, query) {
    for (const part of highlightParts(text, query)) {
      if (part.match) {
        const mark = document.createElement('mark');
        mark.textContent = part.text;
        element.append(mark);
      } else element.append(document.createTextNode(part.text));
    }
  }

  function excerptFor(page, query, heading) {
    const text = page.text.replace(/\s+/g, ' ').trim();
    const lower = text.toLowerCase();
    const headingPosition = heading ? lower.indexOf(heading.text.replace(/\s+/g, ' ').trim().toLowerCase()) : -1;
    const positions = wordsFor(query).map(word => lower.indexOf(word)).filter(position => position >= 0);
    const start = headingPosition >= 0 ? headingPosition : Math.max(0, (positions.length ? Math.min(...positions) : 0) - 35);
    return (start ? '…' : '') + text.slice(start, start + 180) + (start + 180 < text.length ? '…' : '');
  }

  // The same search functions are exercised by the managed .NET test runner.
  if (typeof module !== 'undefined' && module.exports) module.exports = { rankPages, highlightParts, appendHighlighted, excerptFor };
  if (typeof document === 'undefined') return;
  const form = document.querySelector('form[data-search-index]');
  if (!form) return;
  const input = form.querySelector('input[type="search"]');
  const status = form.querySelector('.search-status');
  const results = form.querySelector('.search-results');
  let index;
  let request = 0;
  let timer;

  function loadIndex() {
    if (Array.isArray(globalThis.lizardDocsSearchIndex?.docs)) {
      return Promise.resolve(globalThis.lizardDocsSearchIndex);
    }
    return new Promise((resolve, reject) => {
      // Classic script loading works over file:// as well as HTTP(S).
      const script = document.createElement('script');
      script.src = form.dataset.searchScript;
      script.async = true;
      script.onload = () => {
        script.remove();
        const data = globalThis.lizardDocsSearchIndex;
        if (Array.isArray(data?.docs)) resolve(data);
        else reject(new Error('Invalid search index'));
      };
      script.onerror = () => {
        script.remove();
        reject(new Error('Search index unavailable'));
      };
      document.head.append(script);
    });
  }

  async function search() {
    const current = ++request;
    const query = input.value.trim();
    results.replaceChildren();
    if (!query) { status.textContent = 'Enter words to search for.'; return; }
    status.textContent = 'Searching…';
    try {
      index ??= loadIndex().catch(error => { index = undefined; throw error; });
      const data = await index;
      if (current !== request) return;
      const matches = rankPages(data.docs, query);
      const root = new URL(form.dataset.siteRoot, document.baseURI);
      function safeTarget(location) {
        const target = new URL(location, root);
        return target.origin === root.origin && target.pathname.startsWith(new URL('.', root).pathname) ? target.href : null;
      }
      for (const match of matches.slice(0, 20)) {
        const { page, heading } = match;
        const target = safeTarget(page.location);
        if (!target) continue;
        const item = document.createElement('li');
        const link = document.createElement('a');
        appendHighlighted(link, page.title, query);
        link.href = target;
        item.append(link);
        if (heading && safeTarget(heading.location)) {
          const section = document.createElement('a');
          section.className = 'search-heading';
          section.href = safeTarget(heading.location);
          appendHighlighted(section, heading.text, query);
          item.append(section);
        }
        const excerpt = document.createElement('p');
        appendHighlighted(excerpt, excerptFor(page, query, heading), query);
        item.append(excerpt); results.append(item);
      }
      status.textContent = matches.length ? `${matches.length} matching pages. Showing up to 20.` : 'No matching pages.';
    } catch (_) {
      if (current === request) status.textContent = 'Search unavailable. Keep the search folder with the generated pages and try again.';
    }
  }

  form.addEventListener('submit', event => {
    event.preventDefault();
    clearTimeout(timer);
    search();
  });
  input.addEventListener('input', () => {
    ++request; // A pending result must not replace a newly edited query.
    clearTimeout(timer);
    timer = setTimeout(search, 150);
  });
})();

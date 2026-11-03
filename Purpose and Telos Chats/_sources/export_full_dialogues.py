"""Preserve complete visible dialogue captures and convert them to Markdown."""
import json
import re
import hashlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
helpers = (HERE / 'export_captured_dialogues.py').read_text(encoding='utf-8').split('exports = {}')[0]
exec(helpers)

EXPECTED = {
    '6ab574da-870c-83ea-af0b-6e387fe3ae6f': (7, 15),
    '69a60c75-3374-8326-bb9e-49c801878464': (14, 28),
    '6a97567d-6a48-83e9-8e8e-313db809272a': (11, 22),
    '6aa759e5-2728-83ea-acb7-cd1518328552': (29, 58),
}
docs = {}
for line in SESSION.open(encoding='utf-8'):
    record = json.loads(line)
    p = record.get('payload', {})
    if record.get('type') != 'response_item' or p.get('type') != 'function_call_output':
        continue
    for s in strings(p.get('output')):
        marker = next((m for m in ('FULL_CHAT_CAPTURE:', 'FULL_CHAT_ORDER:') if s.startswith(m)), None)
        if not marker:
            continue
        d = json.JSONDecoder().raw_decode(s[len(marker):])[0]
        if d['id'] not in EXPECTED:
            continue
        doc = docs.setdefault(d['id'], {'id': d['id'], 'title': d.get('title'), 'turns': {}, 'edges': set()})
        ids = [t['id'] if isinstance(t, dict) else t for t in d['turns']]
        doc['edges'].update(zip(ids, ids[1:]))
        if marker == 'FULL_CHAT_CAPTURE:':
            for t in d['turns']:
                old = doc['turns'].setdefault(t['id'], {'id': t['id'], 'messages': {}})
                for m in t['messages']:
                    prev = old['messages'].get(m['id'])
                    if prev is None or len(m.get('html', '')) > len(prev.get('html', '')):
                        old['messages'][m['id']] = m

original_md = md
def md(n):
    if not isinstance(n, str) and n.tag == 'code' and '\n' in plain(n):
        content = plain(n)
        fence = '`' * max(3, max((len(x) + 1 for x in re.findall(r'`+', content)), default=3))
        return '\n\n' + fence + '\n' + content.strip('\n') + '\n' + fence + '\n\n'
    if not isinstance(n, str) and n.tag == 'ol':
        parts = []
        for i, child in enumerate((c for c in n.children if isinstance(c, Node) and c.tag == 'li'), 1):
            body = clean(''.join(md(c) for c in child.children))
            parts.append(str(i) + '. ' + body.replace('\n', '\n   '))
        return '\n\n' + '\n'.join(parts) + '\n\n'
    if not isinstance(n, str) and n.tag == 'hr':
        return '\n\n---\n\n'
    return original_md(n)

manifest = []
archive = []
for doc in docs.values():
    turns = doc['turns']
    edges = doc.pop('edges')
    successors = {a: {b for x, b in edges if x == a} for a in turns}
    predecessors = {b: {a for a, x in edges if x == b} for b in turns}
    assert all(len(v) <= 1 for v in successors.values()), (doc['title'], successors)
    heads = [t for t in turns if not predecessors[t]]
    assert len(heads) == 1, (doc['title'], heads)
    order = []
    at = heads[0]
    while at:
        assert at not in order
        order.append(at)
        at = next(iter(successors[at]), None)
    assert len(order) == len(turns) == EXPECTED[doc['id']][0], (doc['title'], len(order), len(turns))
    messages = []
    for tid in order:
        items = list(turns[tid]['messages'].values())
        assert len(items) == 2 and items[0]['role'] == 'Grant' and items[1]['role'] == 'Nex', (doc['title'], tid, items)
        if tid == '6f15d4d0-b806-4365-8f09-f3d95009cfd9':
            final = json.loads((HERE / 'fission-final-turn.json').read_text(encoding='utf-8'))
            items = [items[0]] + [{'id': x['id'], 'role': 'Nex', 'markdown': x['text']} for x in final['items'] if x['type'] == 'agentMessage']
        for m in items:
            if 'markdown' in m:
                text = m['markdown']
            else:
                tree = Parser(m['html']).root
                text = plain(tree).strip() if m['role'] == 'Grant' else clean(md(tree))
            text = text.replace('\r\n', '\n').replace('\r', '\n')
            assert text and '\ufffd' not in text, (doc['title'], m['id'])
            messages.append({'id': m['id'], 'turn_id': tid, 'role': m['role'], 'text': text})
    assert len(messages) == EXPECTED[doc['id']][1]
    assert len({m['id'] for m in messages}) == len(messages)
    url = 'https://chatgpt.com/c/' + doc['id']
    name = doc['title'] + ' - Full Dialogue.md'
    header = f'# {doc["title"]}\n\nSource: [{doc["title"]}]({url})\n\nExported: October 18, 2026.\n\nCoverage: **Complete captured text dialogue — {len(order)} turns, {len(messages)} messages.**\n\n'
    header += 'Grant is the user; Nex is the assistant. Original wording, spelling, corrections, and historical statements are preserved. The complete visible conversation was recovered from overlapping captures from its first exchange through its final reply. Formatting was converted to Markdown. Images, image galleries, interactive elements, and attached files are not embedded; consult the original chat for those materials and expanded citation badges. This archive does not update or fact-check the original discussion.\n\n'
    if doc['title'] == 'Review Teachings Architecture':
        header += 'Original attachment reference: `The DaVincian Trillium Teachings - The 3 Flowers of Living - 090126.docx`. The document itself is not embedded.\n\n'
    body = '\n\n---\n\n'.join(f'## {i}. {m["role"]}\n\n{m["text"]}' for i, m in enumerate(messages, 1)) + '\n'
    path = ROOT / name
    path.write_text(header + '---\n\n' + body, encoding='utf-8')
    saved = path.read_text(encoding='utf-8')
    assert all(m['text'] in saved for m in messages)
    manifest.append({'title': doc['title'], 'id': doc['id'], 'filename': name, 'turn_count': len(order), 'message_count': len(messages), 'turn_ids': order, 'message_ids': [m['id'] for m in messages], 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    archive.append({'id': doc['id'], 'title': doc['title'], 'url': url, 'ordered_turn_ids': order, 'captured_turns': turns, 'exported_messages': messages})
    print(f'{doc["title"]}: {len(order)} turns, {len(messages)} messages, {path.stat().st_size:,} bytes; first={order[0]}, last={order[-1]}')
assert len(docs) == 4
(HERE / 'full-dialogue-captures.json').write_text(json.dumps(archive, ensure_ascii=False, indent=2), encoding='utf-8')
(HERE / 'full-export-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')

index_path = ROOT / 'INDEX.md'
index = index_path.read_text(encoding='utf-8')
rows = '\n'.join(f'- [{d["title"]} — full dialogue](<{d["filename"]}>) — {d["turn_count"]} turns, {d["message_count"]} messages. [Original chat](https://chatgpt.com/c/{d["id"]}).' for d in manifest)
index = index.replace('## Relevant exchanges from longer chats', rows + '\n\n## Retained relevant excerpts')
start = index.index('Searched the available desktop chat list')
index = index[:start] + 'All six identified conversations now have complete captured text dialogue exports. The four longer conversations were recovered from overlapping browser captures, checked for a continuous first-to-last sequence with no duplicate message IDs, and exported in full. The earlier excerpt files remain available above. Images, interactive graphics, and original attachments are not embedded in these text exports.\n\nThe initial search covered the available desktop chat list, local records, and ChatGPT history search for Telos and Purpose. This is not a guaranteed account-wide inventory of every historical discussion.\n\nOriginal chats and earlier excerpt files remain unchanged. The original short-dialogue/excerpt manifest is `_sources/export-manifest.json`; the new complete exports are recorded in `_sources/full-export-manifest.json`, with reusable captured source text in `_sources/full-dialogue-captures.json`.\n'
index_path.write_text(index, encoding='utf-8')

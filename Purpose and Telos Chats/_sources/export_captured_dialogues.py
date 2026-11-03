"""Convert the browser-read dialogue captures from this export session to Markdown."""
import json
import re
from html.parser import HTMLParser
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SESSION = Path(r'C:\Users\Grant\.codex\sessions\2026\10\18\rollout-2026-10-18T18-59-42-01a1513e-0a28-7072-bd2c-3326d53e6aaf.jsonl')

class Node:
    def __init__(self, tag='', attrs=()):
        self.tag, self.attrs, self.children = tag, dict(attrs), []
    def walk(self):
        yield self
        for child in self.children:
            if isinstance(child, Node):
                yield from child.walk()

class Parser(HTMLParser):
    def __init__(self, html):
        super().__init__(convert_charrefs=True)
        self.root = Node()
        self.stack = [self.root]
        self.feed(html)
    def handle_starttag(self, tag, attrs):
        n = Node(tag, attrs)
        self.stack[-1].children.append(n)
        if tag not in {'img','br','hr','input','meta','link','source','wbr'}:
            self.stack.append(n)
    def handle_endtag(self, tag):
        for i in range(len(self.stack)-1, 0, -1):
            if self.stack[i].tag == tag:
                self.stack = self.stack[:i]
                break
    def handle_data(self, text):
        self.stack[-1].children.append(text)

def plain(n):
    return n if isinstance(n, str) else ''.join(plain(c) for c in n.children)

def md(n):
    if isinstance(n, str): return n
    t, a = n.tag, n.attrs
    if t in {'button','svg','img','script','style'} or 'hidden' in a: return ''
    if t == 'table':
        rows = [[clean(md(c)).replace('\n','<br>').replace('|','\\|') for c in r.children if isinstance(c,Node) and c.tag in {'td','th'}] for r in n.walk() if r.tag == 'tr']
        return '\n\n' + '\n'.join('| '+' | '.join(row)+' |'+ ('\n| '+' | '.join(['---']*len(row))+' |' if i==0 else '') for i,row in enumerate(rows))+'\n\n'
    s = ''.join(md(c) for c in n.children)
    if t == 'a': return '['+s+']('+a.get('href','')+')'
    if t == 'strong' or 'data-d-default-strong' in a: return '**'+s+'**'
    if t == 'em' or a.get('data-d-font-style') == 'italic': return '*'+s+'*'
    if re.fullmatch('h[1-6]',t): return '\n\n'+'#'*int(t[1])+' '+s+'\n\n'
    if t == 'pre': return '\n\n```\n'+plain(n).strip()+'\n```\n\n'
    if t == 'code': return '`'+s+'`'
    if t == 'li':
        return '\n- '+s.strip().replace('\n','\n  ')+'\n'
    if t == 'blockquote': return '\n\n'+'\n'.join('> '+l for l in clean(s).splitlines())+'\n\n'
    if t in {'p','div','ul','ol'} and 'data-d-inline' not in a: return '\n'+s+'\n'
    if t == 'br': return '\n'
    return s

def clean(s):
    return re.sub(r'\n{3,}', '\n\n', s).strip()

def strings(obj):
    if isinstance(obj,str): yield obj
    elif isinstance(obj,list):
        for v in obj: yield from strings(v)
    elif isinstance(obj,dict):
        for v in obj.values(): yield from strings(v)

exports = {}
for line in SESSION.open(encoding='utf-8'):
    d = json.loads(line)
    p = d.get('payload',{})
    if d.get('type') != 'response_item' or p.get('type') != 'function_call_output': continue
    for s in strings(p.get('output')):
        for marker in ('DIALOGUE_RAW_JSON:', 'DIALOGUE_MD_JSON:'):
            if not s.startswith(marker): continue
            doc=json.loads(s[len(marker):])
            if marker == 'DIALOGUE_RAW_JSON:':
                messages=[]
                for turn in doc['turns']:
                    tree=Parser(turn['html']).root
                    for e in tree.walk():
                        key=e.attrs.get('data-chatgpt-search-unit-key')
                        if not key: continue
                        role='Grant' if key.endswith(':user') else 'Nex'
                        bodies=[n for n in e.walk() if ('data-user-message-bubble' in n.attrs if role=='Grant' else 'data-markdown-text-style' in n.attrs)]
                        if not bodies: continue
                        text=plain(bodies[0]).strip() if role=='Grant' else clean(md(bodies[0]))
                        messages.append({'id':e.attrs['data-chatgpt-search-message-ids'].split()[0], 'role':role,'text':text})
                doc['messages']=messages
                doc.pop('turns')
            exports[doc['id']]=doc

full={'6ad53b07-050c-83e9-bea3-71cb2476e252':12,'6abc49fe-40f4-83e9-9750-f4d69f7b9fdd':8}
index=['# Purpose and Telos Chats','','Exported October 18, 2026. Original chats remain unchanged. Grant is the user; Nex is the assistant. These files preserve the captured original wording rather than rewriting the discussions.','','## Full captured dialogues','']
excerpt_rows=[]
manifest=[]
for doc in exports.values():
    is_full=doc['id'] in full
    msgs=doc['messages']
    assert msgs and all(m['text'] for m in msgs)
    assert len({m['id'] for m in msgs})==len(msgs)
    if is_full: assert len(msgs)==full[doc['id']], (doc['title'],len(msgs))
    name=doc['title']+('' if is_full else ' - Relevant Excerpts')+'.md'
    url='https://chatgpt.com/c/'+doc['id']
    status='Full captured dialogue' if is_full else 'Relevant excerpts; not the entire conversation'
    header=f'# {doc["title"]}\n\nSource: [{doc["title"]}]({url})\n\nExported: October 18, 2026.\n\nCoverage: **{status}** — {len(msgs)} messages.\n\n'
    header+='Export note: Speaker labels identify Grant and Nex. Original wording, spelling, and historical terminology are preserved. Formatting was converted from the rendered conversation to Markdown. Images and interactive graphics are not embedded; their available text is retained. Some citation badges retain only their visible labels; consult the original chat for those sources. This archive does not update or fact-check statements made in the original discussion.\n\n---\n\n'
    body='\n\n---\n\n'.join(f'## {i}. {m["role"]}\n\n'+m['text'] for i,m in enumerate(msgs,1))+'\n'
    (ROOT/name).write_text(header+body,encoding='utf-8')
    assert all(m['text'] in (ROOT/name).read_text(encoding='utf-8') for m in msgs)
    row=f'- [{doc["title"]}](<{name}>) — {len(msgs)} messages. [Original chat]({url}).'
    (index if is_full else excerpt_rows).append(row)
    manifest.append({'title':doc['title'],'id':doc['id'],'filename':name,'coverage':status,'message_ids':[m['id'] for m in msgs]})
    print(f'{len(msgs):2} messages | {status} | {name}')
index += ['','## Relevant exchanges from longer chats','']+excerpt_rows
index += ['','## Search and coverage notes','','Searched the available desktop chat list, local records, and ChatGPT history search for Telos and Purpose. The six exports here contain directly relevant discussions. Full captured dialogues contain the beginning and ending of the two short conversations; the four excerpt files preserve complete relevant exchanges captured from longer chats. They are not full exports of those longer conversations. The separate Telos Project thriller-writing schedules, incidental software examples, and an incidental mention in a health discussion were excluded. Search results may omit other historical discussions, so this is not a guaranteed account-wide inventory.','','The source capture manifest is in `_sources/export-manifest.json`. No original conversation was moved, edited, deleted, or published.']
(ROOT/'INDEX.md').write_text('\n'.join(index)+'\n',encoding='utf-8')
(ROOT/'_sources'/'export-manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
assert len(exports)==6,len(exports)

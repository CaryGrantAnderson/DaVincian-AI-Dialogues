const $ = id => document.getElementById(id);
let selected = '', connectionId = '', peer, channel, microphone, ready = false, stopping = false, stopTimer, active = false;
let messages = [], handledCalls = new Set(), toolQueue = Promise.resolve();
let pending = JSON.parse(localStorage.getItem('nex-pending-captions') || '[]'), flushing = false;
let docs = [], notesDirty = false;
async function api(path, body, method = body === undefined ? 'GET' : 'POST') {
  const r = await fetch(path, { method, headers: body === undefined ? {} : {'Content-Type':'application/json'}, body: body === undefined ? undefined : JSON.stringify(body) });
  if (!r.ok) { let message = `Request failed (${r.status})`; try { message = (await r.json()).error || message; } catch {} throw new Error(message); }
  return r.json();
}
function status(text) { $('status').textContent = text; }
function persistPending() { localStorage.setItem('nex-pending-captions', JSON.stringify(pending)); }
async function flush() {
  if (flushing) return;
  flushing = true;
  try {
    while (pending.length) {
      const p = pending[0];
      await api(`/api/conversations/${p.conversationId}/events`, p.event);
      pending.shift(); persistPending();
    }
    $('saveStatus').textContent = 'All received captions saved locally.';
  } catch (e) { $('saveStatus').textContent = `Save pending (${pending.length} fragments): ${e.message}`; }
  finally { flushing = false; }
}
setInterval(flush, 2500);
function receiveCaption(e) {
  const item = {eventId:e.event_id || crypto.randomUUID(),type:e.type,delta:e.delta || '',startMs:e.start_ms || 0,endMs:e.end_ms ?? e.start_ms ?? 0,connectionId};
  messages.push(item);
  pending.push({conversationId:selected,event:item});
  try { persistPending(); } catch { status('Browser recovery storage is full. Keep this tab open while captions save.'); }
  renderTranscript(); flush();
}
function renderTranscript() {
  const area = $('transcript'); area.replaceChildren();
  for (const m of messages) {
    const role = m.type === 'session.input_transcript.delta' ? 'Grant' : 'Nex';
    // Keep speakers separate even when fragments arrive interleaved.
    let last = [...area.children].reverse().find(el => el.dataset.role === role);
    if (!last || last.dataset.connection !== m.connectionId || m.startMs - Number(last.dataset.end) > 4000) {
      last = document.createElement('article'); last.className = `message ${role.toLowerCase()}`; last.dataset.role = role; last.dataset.connection = m.connectionId;
      const label = document.createElement('div'); label.className='speaker'; label.textContent=role.toUpperCase();
      last.append(label, document.createElement('p')); area.append(last);
    }
    last.querySelector('p').textContent += m.delta;
    last.dataset.end = m.endMs;
  }
  if (!messages.length) { const p=document.createElement('p'); p.className='hint'; p.textContent='This conversation is ready. Start voice when you want to begin.'; area.append(p); }
  area.scrollTop=area.scrollHeight;
}
async function openDocument(id) {
  const doc = docs.find(d=>d.id===id);
  const r=await fetch(`/api/document/${id}`); if(!r.ok) throw new Error('Could not read source document.');
  $('readerTitle').textContent=doc?.title || 'Source dialogue'; $('readerText').textContent=await r.text(); $('reader').showModal();
}
function sourceButton(title, detail, id) {
  const b=document.createElement('button'); b.className='source';
  const strong=document.createElement('strong'); strong.textContent=title; const small=document.createElement('small'); small.textContent=detail;
  b.append(strong,small); b.onclick=()=>openDocument(id).catch(e=>status(e.message)); return b;
}
function renderDocs() {
  $('archiveCount').textContent=`${docs.length} full Markdown documents indexed. Earlier excerpts are skipped when a full version is present.`;
  $('documents').replaceChildren(...docs.map(d=>sourceButton(d.title,`${d.lines} lines · ${d.relativePath}`,d.id)));
}
async function load() {
  const s=await api('/api/status'); docs=s.documents; renderDocs();
  $('voice').textContent=`${s.voiceModel} · ${s.voiceId}`; $('key').textContent=s.keyConfigured?'Server key configured (access checked when connecting)':'OPENAI_API_KEY is not set';
  $('root').textContent=s.archiveRoot; $('data').textContent=s.dataFolder; if(!notesDirty) $('notes').value=s.notes;
  const picker=$('conversations'); picker.replaceChildren(new Option('Choose a conversation…',''));
  for(const c of s.conversations) picker.add(new Option(c.title,c.id));
  if(selected) picker.value=selected;
  $('start').disabled=!selected || active || !s.keyConfigured;
  await flush();
  return s;
}
async function select(id) {
  if(active) return;
  selected=id; $('conversations').value=id;
  if(!id) { $('start').disabled=true; return; }
  const detail=await api(`/api/conversations/${id}`); messages=detail.events;
  $('conversationTitle').textContent=detail.info.title;
  $('export').href=`/api/conversations/${id}/markdown`; $('export').hidden=false;
  renderTranscript(); await load(); status('Ready. Voice calls use your OpenAI API account.');
  localStorage.setItem('nex-last-conversation',id);
}
$('new').onclick=async()=>{ try { const c=await api('/api/conversations',{title:$('title').value.trim() || `Conversation · ${new Date().toLocaleDateString()}`}); await select(c.id); $('title').value=''; } catch(e){ status(e.message); } };
$('conversations').onchange=()=>select($('conversations').value).catch(e=>status(e.message));
$('notes').oninput=()=>{notesDirty=true;$('notesStatus').textContent='Unsaved changes.';};
$('saveNotes').onclick=async()=>{try{await api('/api/notes',{text:$('notes').value},'PUT');notesDirty=false;$('notesStatus').textContent='Saved locally. These notes will be included in the next call.';}catch(e){$('notesStatus').textContent=e.message;}};
$('searchForm').onsubmit=async e=>{e.preventDefault();try{const hits=await api(`/api/search?q=${encodeURIComponent($('query').value)}`);$('results').replaceChildren(...hits.map(h=>sourceButton(h.title,`Lines ${h.startLine}–${h.endLine}: ${h.text.slice(0,220)}`,h.documentId)));if(!hits.length)$('results').textContent='No matching passages. Try a distinctive word or phrase.';}catch(err){status(err.message);}};
$('refresh').onclick=async()=>{try{docs=await api('/api/reindex',{});renderDocs();status('Archive refreshed. New files are available for the next call.');}catch(e){status(e.message);}};
$('closeReader').onclick=()=>$('reader').close();
$('setKey').onclick=async()=>{
  $('setKey').disabled=true;$('keyStatus').textContent='Checking key and saved voice access…';
  const key=$('apiKey').value;$('apiKey').value='';
  try{const result=await api('/api/key',{key});$('keyStatus').textContent=result.message;$('key').textContent='Saved voice access verified for this run';$('start').disabled=!selected || active;}
  catch(e){$('keyStatus').textContent=e.message;}
  finally{$('setKey').disabled=false;}
};
function send(event) { if(!ready || channel?.readyState!=='open') throw new Error('Voice session is not ready.'); channel.send(JSON.stringify(event)); }
async function runTool(item) {
  if(handledCalls.has(item.call_id)) return;
  handledCalls.add(item.call_id);
  status('Nex is consulting the archive…');
  let output;
  try {
    output=await api('/api/tool',{name:item.name,arguments:JSON.parse(item.arguments)});
    if(Array.isArray(output) && output[0]) {
      $('results').replaceChildren(...output.map(h=>sourceButton(h.title,`Lines ${h.startLine}–${h.endLine}: ${h.text.slice(0,220)}`,h.documentId)));
    }
  }catch(e){output={error:e.message};}
  send({type:'response.item.create',event_id:crypto.randomUUID(),item:{type:'function_call_output',call_id:item.call_id,output:JSON.stringify(output)}});
  send({type:'response.create',event_id:crypto.randomUUID()});
  status('Connected. Archive result returned to Nex.');
}
function handleEvent(e) {
  if(e.type==='session.started') { ready=true; $('mute').disabled=false; $('stop').disabled=false; status('Connected · microphone on · captions save automatically.'); }
  else if(e.type==='session.input_transcript.delta' || e.type==='session.output_transcript.delta') receiveCaption(e);
  else if(e.type==='response.event' && e.event?.type==='response.output_item.done' && e.event.item?.type==='function_call') {
    toolQueue=toolQueue.then(()=>runTool(e.event.item)).catch(err=>status(`Archive tool error: ${err.message}`));
  }
  else if(e.type==='error') status(`Voice error: ${e.error?.message || e.message || e.error?.code || 'Unknown API error'}`);
  else if(e.type==='session.closed') { status('Call ended. Saving final captions…'); cleanup(); flush().then(()=>{if(!pending.length)status('Call ended. All received captions saved.');}); }
}
function cleanup() {
  clearTimeout(stopTimer); ready=false; active=false; stopping=false;
  microphone?.getTracks().forEach(t=>t.stop()); microphone=null;
  if(channel){channel.onclose=null;channel.close();} channel=null;
  if(peer){peer.onconnectionstatechange=null;peer.close();}peer=null;
  $('audio').srcObject=null; $('start').disabled=!selected; $('stop').disabled=true; $('mute').disabled=true; $('mute').textContent='Mute mic';
  $('conversations').disabled=false;$('new').disabled=false;
}
async function waitForIce(connection) {
  if(connection.iceGatheringState==='complete') return;
  await new Promise((resolve,reject)=>{
    const timer=setTimeout(()=>{connection.removeEventListener('icegatheringstatechange',check);reject(new Error('Microphone connection timed out while gathering network candidates.'));},10000);
    function check(){if(connection.iceGatheringState==='complete'){clearTimeout(timer);connection.removeEventListener('icegatheringstatechange',check);resolve();}}
    connection.addEventListener('icegatheringstatechange',check);check();
  });
}
$('start').onclick=async()=>{
  if(active || !selected) return;
  active=true;stopping=false;connectionId=crypto.randomUUID();handledCalls=new Set();toolQueue=Promise.resolve();
  $('start').disabled=true;$('conversations').disabled=true;$('new').disabled=true;status('Requesting microphone access…');
  try{
    microphone=await navigator.mediaDevices.getUserMedia({audio:{echoCancellation:true,noiseSuppression:true}});
    peer=new RTCPeerConnection();
    peer.ontrack=e=>{ $('audio').srcObject=new MediaStream([e.track]);$('audio').play().catch(()=>status('Select Play in the audio controls to hear Nex.')); };
    peer.onconnectionstatechange=()=>{if(peer?.connectionState==='failed'){status('Voice connection failed. Received captions are preserved.');cleanup();}};
    microphone.getTracks().forEach(t=>peer.addTrack(t,microphone));
    channel=peer.createDataChannel('oai-events');
    channel.onmessage=({data})=>{try{handleEvent(JSON.parse(data));}catch(e){status(`Event error: ${e.message}`);}};
    channel.onclose=()=>{if(active){status('Connection closed without final confirmation. Received captions are preserved.');cleanup();flush();}};
    status('Connecting your saved Nex voice…');
    await peer.setLocalDescription(await peer.createOffer());await waitForIce(peer);
    const result=await api('/api/session',{sdp:peer.localDescription.sdp,conversationId:selected});
    await peer.setRemoteDescription({type:'answer',sdp:result.transport.sdp});
    stopTimer=setTimeout(()=>{if(!ready){status('Voice startup timed out. Check project access and connection.');cleanup();}},20000);
  }catch(e){cleanup();status(e.message);}
};
$('mute').onclick=()=>{const track=microphone?.getAudioTracks()[0];if(track){track.enabled=!track.enabled;$('mute').textContent=track.enabled?'Mute mic':'Unmute mic';status(track.enabled?'Connected · microphone on.':'Connected · microphone muted. Call billing continues.');}};
$('stop').onclick=()=>{
  if(!ready || stopping) return; stopping=true;$('stop').disabled=true;status('Ending call and waiting for final captions…');
  // Stop local capture immediately; keep the peer and event channel until finalization.
  microphone?.getTracks().forEach(t=>t.enabled=false);
  send({type:'session.close'});
  clearTimeout(stopTimer);stopTimer=setTimeout(()=>{status('Call closed locally; final server usage was not confirmed. Received captions are saved.');cleanup();flush();},15000);
};
window.addEventListener('beforeunload',e=>{if(active || pending.length || notesDirty){e.preventDefault();e.returnValue='';}});
load().then(async s=>{const last=localStorage.getItem('nex-last-conversation');if(last && s.conversations.some(c=>c.id===last))await select(last);else status(s.keyConfigured?'Choose or create a conversation to begin.':'Set the server API key before starting a voice call.');}).catch(e=>status(e.message));

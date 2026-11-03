'use strict';
const $=id=>document.getElementById(id);
let context,epoch=0,peer,events,mic,nodes={},sources=[],ready=false,active=false,ending=false,stopTimer;
let beginResolve,answerResolve,answerReject,flushResolve={},savedResolve,sessionId,synthetic=false,bytes={Grant:0,Nex:0};
let meterNode,meterSamples,indicatorFrame,recordingElapsed=0;
function updateIndicators(){
  recordingElapsed=captureMs();const seconds=Math.floor(recordingElapsed/1000);$('elapsed').textContent=[Math.floor(seconds/3600),Math.floor(seconds/60)%60,seconds%60].map(n=>String(n).padStart(2,'0')).join(':');
  let rms=0;if(meterNode){meterNode.getFloatTimeDomainData(meterSamples);for(const sample of meterSamples)rms+=sample*sample;rms=Math.sqrt(rms/meterSamples.length);}
  $('micLevel').value=Math.min(1,rms*4);$('micValue').textContent=mic?.getAudioTracks()[0]?.enabled===false?'Muted':rms<.001?'Silent':`${Math.round(Math.min(1,rms*4)*100)}%`;
  if(synthetic)window.indicatorEvidence={elapsedMs:recordingElapsed,maxLevel:Math.max(window.indicatorEvidence?.maxLevel||0,Number($('micLevel').value)),active:$('recordingPanel').classList.contains('is-recording')};
}
const post=data=>window.chrome.webview.postMessage(data);
const captureMs=()=>context?Math.max(0,context.currentTime*1000-epoch*1000/context.sampleRate):0;
const send=e=>{if(ready&&events?.readyState==='open')events.send(JSON.stringify(e));};
function status(text){$('status').textContent=text;}
function toBase64(buffer){const data=new Uint8Array(buffer);let s='';for(let i=0;i<data.length;i+=4096)s+=String.fromCharCode(...data.subarray(i,i+4096));return btoa(s);}
async function prepare(){
  context=new AudioContext();await context.audioWorklet.addModule('pcm-worklet.js');await context.resume();epoch=Math.ceil(context.currentTime*context.sampleRate);
  for(const speaker of ['Grant','Nex']){
    const node=new AudioWorkletNode(context,'nex-pcm-writer',{numberOfInputs:1,numberOfOutputs:1,outputChannelCount:[1],processorOptions:{speaker,epoch}});
    const quiet=context.createGain();quiet.gain.value=0;node.connect(quiet).connect(context.destination);nodes[speaker]=node;
    node.port.onmessage=({data})=>{
      if(data.type==='flushed'){flushResolve[speaker]?.();return;}
      if(data.type==='pcm'){bytes[speaker]+=data.bytes.byteLength;post({type:'audio',speaker,sequence:data.sequence,startFrame:data.startFrame,pcm:toBase64(data.bytes)});$(speaker.toLowerCase()).textContent=`${speaker}: ${(bytes[speaker]/1024).toFixed(0)} KB captured`;}
    };
  }
  const begun=new Promise(resolve=>beginResolve=resolve);post({type:'begin',sampleRate:context.sampleRate});await begun;
  $('recordingPanel').classList.add('is-recording');$('recordingState').textContent='Recording · connecting';updateIndicators();indicatorFrame=setInterval(updateIndicators,100);
}
function connectSource(stream,speaker){const source=context.createMediaStreamSource(stream);source.connect(nodes[speaker]);if(speaker==='Grant'){meterNode=context.createAnalyser();meterNode.fftSize=1024;meterSamples=new Float32Array(meterNode.fftSize);source.connect(meterNode);}sources.push(source);}
function caption(e){const speaker=e.type==='session.input_transcript.delta'?'Grant':'Nex';post({type:'caption',eventId:e.event_id||crypto.randomUUID(),speaker,delta:e.delta,startMs:e.start_ms,endMs:e.end_ms,receivedCaptureMs:captureMs()});const area=$('captions');let row=[...area.children].reverse().find(r=>r.dataset.speaker===speaker);if(!row || e.start_ms-Number(row.dataset.end)>1800){row=document.createElement('article');row.className='caption';row.dataset.speaker=speaker;const label=document.createElement('strong');label.textContent=speaker;row.append(label,document.createElement('p'));area.append(row);}row.querySelector('p').textContent+=e.delta;row.dataset.end=e.end_ms;area.scrollTop=area.scrollHeight;}
async function finish(confirmed,error=''){
  if(ending)return;ending=true;clearTimeout(stopTimer);$('stop').disabled=true;$('mute').disabled=true;
  try{
    if(confirmed&&!synthetic)await new Promise(resolve=>setTimeout(resolve,350));
    await Promise.all(Object.entries(nodes).map(([speaker,node])=>new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(new Error('Audio capture flush timed out; durable prefixes remain saved.')),8000);flushResolve[speaker]=()=>{clearTimeout(timer);resolve();};node.port.postMessage('flush');})));
    const saved=new Promise(resolve=>savedResolve=resolve);post({type:'complete',confirmed,error});await saved;
    $('saved').textContent='Saved in this Recording: Grant WAV, Nex WAV, original transcript events, and timing metadata.';
    status(confirmed?'Recording complete and saved.':`Interrupted Recording preserved. ${error}`);
  }catch(e){status(e.message);}
  finally{updateIndicators();clearInterval(indicatorFrame);$('recordingPanel').classList.remove('is-recording');$('recordingState').textContent=confirmed?'Recording saved':'Recording interrupted';$('micLevel').value=0;$('micValue').textContent='Stopped';mic?.getTracks().forEach(t=>t.stop());sources.forEach(s=>{try{s.stop?.();s.disconnect();}catch{}});events?.close();peer?.close();await context?.close();active=false;ready=false;}
}
function onEvent(e){
  if(e.type==='session.started'){ready=true;post({type:'providerStarted',id:e.session.id,captureMs:captureMs()});$('recordingState').textContent='Recording · live';$('stop').disabled=false;$('mute').disabled=false;status('Connected · both audio tracks are being captured.');}
  else if(e.type==='session.input_transcript.delta'||e.type==='session.output_transcript.delta')caption(e);
  else if(e.type==='session.closed')finish(true);
  else if(e.type==='error')status(`Voice service: ${e.error?.message||e.message||'Unknown error'}`);
}
async function start(){
  if(active)return;active=true;$('start').disabled=true;status('Requesting microphone…');
  try{
    mic=await navigator.mediaDevices.getUserMedia({audio:{echoCancellation:true,noiseSuppression:true}});
    await prepare();connectSource(mic,'Grant');
    peer=new RTCPeerConnection();peer.ontrack=e=>{const stream=new MediaStream([e.track]);connectSource(stream,'Nex');$('audio').srcObject=stream;$('audio').play().catch(()=>status('Use Play in the audio controls to hear Nex.'));};
    mic.getTracks().forEach(t=>peer.addTrack(t,mic));events=peer.createDataChannel('oai-events');
    events.onmessage=({data})=>{try{onEvent(JSON.parse(data));}catch(e){status(e.message);}};
    events.onclose=()=>{if(!ending)finish(false,'The voice connection closed without final confirmation.');};
    peer.onconnectionstatechange=()=>{if(peer.connectionState==='failed')finish(false,'The voice network connection failed.');};
    await peer.setLocalDescription(await peer.createOffer());
    if(peer.iceGatheringState!=='complete')await new Promise((resolve,reject)=>{const t=setTimeout(()=>reject(new Error('Network candidate gathering timed out.')),10000);peer.addEventListener('icegatheringstatechange',()=>{if(peer.iceGatheringState==='complete'){clearTimeout(t);resolve();}});});
    status('Connecting saved Nex voice…');const answer=new Promise((resolve,reject)=>{answerResolve=resolve;answerReject=reject;});post({type:'offer',sdp:peer.localDescription.sdp});const result=await answer;
    await peer.setRemoteDescription({type:'answer',sdp:result.transport.sdp});
    stopTimer=setTimeout(()=>{if(!ready)finish(false,'Provider startup was not confirmed.');},20000);
  }catch(e){if(context&&sessionId)await finish(false,e.message);else{mic?.getTracks().forEach(t=>t.stop());await context?.close();active=false;status(e.message);}}
}
function stop(){if(!active||ending)return;$('recordingState').textContent='Recording · finishing';status('Ending call and saving audio…');mic?.getAudioTracks().forEach(t=>t.enabled=false);send({type:'session.close'});clearTimeout(stopTimer);stopTimer=setTimeout(()=>finish(false,'Final provider confirmation did not arrive.'),15000);}
$('start').onclick=start;$('stop').onclick=stop;$('mute').onclick=()=>{const t=mic?.getAudioTracks()[0];if(t){t.enabled=!t.enabled;$('mute').textContent=t.enabled?'Mute microphone':'Unmute microphone';}};
window.chrome.webview.addEventListener('message',({data})=>{
  if(data.type==='configure'){$('title').textContent=data.title;synthetic=data.synthetic;$('start').disabled=!data.canStart;}
  else if(data.type==='begun'){sessionId=data.id;beginResolve?.();}
  else if(data.type==='answer')answerResolve?.(data.result);
  else if(data.type==='offerFailed')answerReject?.(new Error(data.message));
  else if(data.type==='saved')savedResolve?.();
  else if(data.type==='stop')stop();
  else if(data.type==='fatal'){if(!ending&&sessionId)finish(false,data.message);else status(data.message);}
});
// Used only by the isolated desktop acceptance harness: no microphone or API call.
window.runSyntheticTest=async()=>{
  if(!synthetic)throw new Error('Synthetic mode is not enabled.');active=true;$('start').disabled=true;await prepare();
  for(const [speaker,hz] of [['Grant',440],['Nex',880]]){const tone=context.createOscillator();tone.frequency.value=hz;const gain=context.createGain();gain.gain.value=.15;tone.connect(gain).connect(nodes[speaker]);if(speaker==='Grant'){meterNode=context.createAnalyser();meterNode.fftSize=1024;meterSamples=new Float32Array(meterNode.fftSize);gain.connect(meterNode);}tone.start();sources.push(tone);}
  post({type:'providerStarted',id:'synthetic-no-api',captureMs:0});
  caption({type:'session.input_transcript.delta',event_id:'test-grant',delta:'Synthetic Grant audio.',start_ms:100,end_ms:600});
  caption({type:'session.output_transcript.delta',event_id:'test-nex',delta:'Synthetic Nex audio.',start_ms:300,end_ms:800});
  await new Promise(resolve=>setTimeout(resolve,1300));await finish(true);
};
post({type:'ready'});

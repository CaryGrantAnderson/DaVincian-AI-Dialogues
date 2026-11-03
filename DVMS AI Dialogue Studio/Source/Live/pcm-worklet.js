class NexPcmWriter extends AudioWorkletProcessor {
  constructor(options) {
    super();this.epoch=options.processorOptions.epoch;this.speaker=options.processorOptions.speaker;
    this.buffer=new Int16Array(8192);this.used=0;this.start=0;this.sequence=0;this.stopped=false;
    this.port.onmessage=e=>{if(e.data==='flush'){this.flush();this.stopped=true;this.port.postMessage({type:'flushed'});}};
  }
  flush(){if(!this.used)return;const bytes=this.buffer.slice(0,this.used).buffer;this.port.postMessage({type:'pcm',speaker:this.speaker,sequence:this.sequence++,startFrame:this.start,bytes},[bytes]);this.used=0;}
  process(inputs,outputs){
    if(this.stopped)return false;
    const count=outputs[0][0].length;const channel=inputs[0]?.[0];
    for(let i=0;i<count;i++){
      if(this.used===0)this.start=currentFrame+i-this.epoch;
      const value=Math.max(-1,Math.min(1,channel?.[i]||0));this.buffer[this.used++]=Math.round(value<0?value*32768:value*32767);
      if(this.used===this.buffer.length)this.flush();
    }
    return true;
  }
}
registerProcessor('nex-pcm-writer',NexPcmWriter);

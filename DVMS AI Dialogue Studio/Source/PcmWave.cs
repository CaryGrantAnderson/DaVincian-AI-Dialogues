using System;
using System.IO;
using System.Text;

namespace DVMS.AIDialogueStudio
{
    // Header is checkpointed and flushed after every block, so a process failure
    // leaves a readable prefix instead of an unfinalized media container.
    public sealed class PcmWave : IDisposable
    {
        readonly FileStream file;
        public int SampleRate { get; private set; }
        public long Frames { get; private set; }
        public PcmWave(string path, int sampleRate)
        {
            if (sampleRate < 8000 || sampleRate > 96000) throw new ArgumentOutOfRangeException("sampleRate");
            SampleRate = sampleRate; file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read); Header();
        }
        public void Append(long startFrame, byte[] pcm)
        {
            if (startFrame < Frames || startFrame > Frames + SampleRate * 30L || pcm.Length % 2 != 0 || pcm.Length > 131072) throw new InvalidDataException("Audio block is out of sequence or has invalid PCM data.");
            PadTo(startFrame); file.Position = 44 + Frames * 2; file.Write(pcm, 0, pcm.Length); Frames += pcm.Length / 2; Header();
        }
        public void PadTo(long frame)
        {
            if (frame < Frames || frame * 2 > uint.MaxValue - 44) throw new InvalidDataException("PCM WAV exceeds the supported Recording size.");
            file.Position = 44 + Frames * 2;
            var zero = new byte[8192]; long bytes = (frame - Frames) * 2;
            while (bytes > 0) { int n = (int)Math.Min(bytes, zero.Length); file.Write(zero, 0, n); bytes -= n; }
            Frames = frame;
        }
        void Header()
        {
            file.Position = 0;
            using (var writer = new BinaryWriter(file, Encoding.ASCII, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write((uint)(36 + Frames * 2)); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(SampleRate); writer.Write(SampleRate * 2); writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write((uint)(Frames * 2));
            }
            file.Flush(true);
        }
        public void Dispose() { Header(); file.Dispose(); }
        public static void RecoverHeader(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            using (var reader = new BinaryReader(stream, Encoding.ASCII, true))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                if (stream.Length < 44 || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new InvalidDataException("A retained capture WAV has an invalid header; original bytes were left in place.");
                long bytes = (stream.Length - 44) / 2 * 2;
                if (bytes > uint.MaxValue - 36) throw new InvalidDataException("Retained WAV is too large to recover.");
                stream.Position = 4; writer.Write((uint)(36 + bytes)); stream.Position = 40; writer.Write((uint)bytes); stream.Flush(true);
            }
        }
    }
}

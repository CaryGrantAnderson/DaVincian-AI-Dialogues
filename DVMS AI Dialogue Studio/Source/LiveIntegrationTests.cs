using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Dapper;
using DVMSDesktop.Framework;
using Microsoft.Web.WebView2.Core;

namespace DVMS.AIDialogueStudio
{
    static class LiveIntegrationTests
    {
        public static int Run()
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestEvidence", "live-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 5)); Directory.CreateDirectory(folder);
            string log = Path.Combine(folder, "results.txt"); int result = 1;
            Action<bool, string> check = (ok, label) => { File.AppendAllText(log, (ok ? "PASS " : "FAIL ") + label + Environment.NewLine); if (!ok) throw new Exception(label); };
            try
            {
                var repo = RecordingRepository.Create(folder, "Synthetic live capture — no microphone or API");
                var legacy = RecordingRepository.Create(folder, "Schema migration fixture"); var legacyId = legacy.Load().Id;
                using (var db = legacy.Open()) db.Execute("DROP TABLE UtteranceTimings; DROP TABLE LiveAudioTracks; DROP TABLE LiveAudioBlocks; DROP TABLE LiveCaptionEvents; DROP TABLE LiveSessions; PRAGMA user_version=1;");
                var upgraded = RecordingRepository.OpenExisting(legacy.DatabasePath);
                check(upgraded.Load().Id == legacyId && upgraded.LiveSessions().Count == 0, "v1 Recording upgrades transactionally without changing its identity or dialogue");
                var before = repo.Load();
                using (var capture = new LiveRecordingCapture(repo, ConnectionSettings.DefaultVoice, 48000))
                {
                    capture.ProviderStarted("fixture-no-api", 300);
                    byte[] grant = Enumerable.Range(0, 4000).SelectMany(i => BitConverter.GetBytes((short)1200)).ToArray();
                    byte[] nex = Enumerable.Range(0, 4000).SelectMany(i => BitConverter.GetBytes((short)-2400)).ToArray();
                    capture.Audio("Grant", 0, 128, grant); capture.Audio("Grant", 0, 128, grant); capture.Audio("Nex", 0, 0, nex);
                    byte[] prefix;
                    using (var input = new FileStream(repo.AudioPath(capture.Id, "Grant"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new BinaryReader(input)) prefix = reader.ReadBytes((int)input.Length);
                    check(System.Text.Encoding.ASCII.GetString(prefix, 0, 4) == "RIFF" && BitConverter.ToUInt32(prefix, 40) == prefix.Length - 44, "WAV header describes the durable audio prefix while recording is still active");
                    var a = new LiveCaption { EventId = "g1", Speaker = "Grant", Delta = "Our Triune", StartMs = 100, EndMs = 200, ReceivedCaptureMs = 800 };
                    capture.Caption(a); capture.Caption(a); capture.Caption(new LiveCaption { EventId = "g2", Speaker = "Grant", Delta = " has three bases.", StartMs = 200, EndMs = 350, ReceivedCaptureMs = 900 });
                    capture.Caption(new LiveCaption { EventId = "n1", Speaker = "Nex", Delta = "An overlapping response.", StartMs = 150, EndMs = 500, ReceivedCaptureMs = 1000 });
                    check(repo.RecoverInterruptedCaptures() == 0, "recovery leaves an active capture protected by its lock");
                    bool gap = false; try { capture.Audio("Grant", 3, 4128, grant); } catch (InvalidDataException) { gap = true; } check(gap, "missing audio sequence is rejected instead of silently losing a block");
                    capture.Complete(true);
                }
                var first = repo.LiveSessions().Single();
                check(first.Status == "Completed" && repo.Load().Generation == before.Generation + 1, "live capture becomes one source contribution and a new Recording revision");
                check(repo.Captions(first.Id).Count == 3, "caption retry is idempotent and exact delta spacing is retained");
                check(repo.Load().Items.Any(i => i.Text == "Our Triune has three bases."), "captions become editable linked dialogue passages");
                var grantPath = repo.AudioPath(first.Id, "Grant"); var nexPath = repo.AudioPath(first.Id, "Nex");
                var grantData = File.ReadAllBytes(grantPath); var nexData = File.ReadAllBytes(nexPath);
                check(grantData.Length == nexData.Length && !grantData.SequenceEqual(nexData), "separate Grant and Nex WAVs have aligned lengths and different source samples");
                check(BitConverter.ToInt16(grantData, 44) == 0 && BitConverter.ToInt16(grantData, 44 + 256) == 1200 && BitConverter.ToInt16(nexData, 44) == -2400, "audio gaps are explicit silence; original per-speaker samples are not mixed");
                using (var db = repo.Open())
                {
                    check(db.ExecuteScalar<int>("SELECT COUNT(*) FROM LiveAudioTracks") == 2, "both original audio tracks are registered as assets with sample metadata");
                    check(db.ExecuteScalar<double>("SELECT EstimatedCaptureStartMs FROM UtteranceTimings WHERE ProviderStartMs=100") == 400, "provider timestamps and estimated local audio alignment are both retained");
                    check(db.ExecuteScalar<string>("PRAGMA integrity_check") == "ok", "Recording database remains valid after live finalization");
                }
                string interrupted = Guid.NewGuid().ToString("N");
                using (var db = repo.Open()) db.Execute("INSERT INTO LiveSessions VALUES (@interrupted,'Capturing',@voice,48000,NULL,NULL,@Now,NULL,NULL,NULL)", new { interrupted, voice = ConnectionSettings.DefaultVoice, Now = DateTime.UtcNow.ToString("O") });
                File.WriteAllText(repo.JournalPath(interrupted), new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new LiveCaption { EventId = "crash-journal-only", Speaker = "Grant", Delta = "Durable journal event.", StartMs = 10, EndMs = 100, ReceivedCaptureMs = 500 }) + "\n");
                foreach (var speaker in new[] { "Grant", "Nex" }) using (var wave = new PcmWave(repo.AudioPath(interrupted, speaker), 48000)) wave.Append(0, new byte[8000]);
                using (var tail = new FileStream(repo.AudioPath(interrupted, "Grant"), FileMode.Append)) tail.Write(new byte[3200], 0, 3200);
                check(repo.RecoverInterruptedCaptures() == 1 && repo.LiveSessions().Last().Status == "Interrupted", "stale capture recovers retained WAV prefixes without claiming provider completion");
                var recoveredWave = File.ReadAllBytes(repo.AudioPath(interrupted, "Grant"));
                check(BitConverter.ToUInt32(recoveredWave, 40) == recoveredWave.Length - 44 && repo.Captions(interrupted).Count == 1, "crash recovery repairs WAV lengths and replays durable journal events missing from SQLite");
                check(repo.RecoverInterruptedCaptures() == 0, "recovery does not append duplicate live contributions");
                var app = new ApplicationServices(new ProductIdentity("StudioLiveTests", "Studio Live Tests"), Path.Combine(folder, "App"), null, true);
                using (var form = new LiveRecordingForm(app, repo, true))
                {
                    form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-5000, -5000);
                    form.Shown += async (s, e) =>
                    {
                        try
                        {
                            if (await Task.WhenAny(form.Ready, Task.Delay(25000)) != form.Ready) throw new TimeoutException("WebView2 did not initialize.");
                            await form.Ready;
                            await form.Web.ExecuteScriptAsync("window.runSyntheticTest().then(()=>window.syntheticDone=true).catch(e=>window.syntheticError=e.message)");
                            if (await Task.WhenAny(form.Completed, Task.Delay(25000)) != form.Completed)
                            { string state = await form.Web.ExecuteScriptAsync("JSON.stringify({status:document.getElementById('status').textContent,error:window.syntheticError})"); throw new TimeoutException("Embedded audio test did not finish: " + state); }
                            check(await form.Completed, "embedded WebView2 finishes a two-track AudioWorklet capture through the actual native bridge");
                            await Task.Delay(300);
                            var live = repo.LiveSessions().Last(); var g = File.ReadAllBytes(repo.AudioPath(live.Id, "Grant")); var n = File.ReadAllBytes(repo.AudioPath(live.Id, "Nex"));
                            check(g.Length > 48000 && n.Length == g.Length && !g.SequenceEqual(n), "synthetic 440 Hz and 880 Hz streams produce distinct aligned WAV files");
                            check(repo.Captions(live.Id).Count == 2 && live.ProviderSessionId == "synthetic-no-api", "embedded caption events retain both speakers and timing without API use");
                            check(await form.Web.ExecuteScriptAsync("!!(window.indicatorEvidence?.active && window.indicatorEvidence.elapsedMs > 1000 && window.indicatorEvidence.maxLevel > 0.05 && document.getElementById('recordingState').textContent==='Recording saved')") == "true", "recording indicator, elapsed clock and level meter respond to captured audio and stop on save");
                            var imported = RecordingRepository.Create(folder, "Imported audio fixture"); imported.ImportAudio(repo.AudioPath(live.Id, "Grant"));
                            using (var importedDb = imported.Open()) check(imported.Load().Items.Count == 0 && importedDb.ExecuteScalar<int>("SELECT COUNT(*) FROM MediaAssets WHERE Kind='ImportedAudio'") == 1 && File.ReadAllBytes(Directory.GetFiles(Path.Combine(imported.Folder,"Media","Imported")).Single()).SequenceEqual(g), "audio import preserves original bytes and registers the asset without inventing a transcript");
                            using (var output = File.Create(Path.Combine(folder, "live-capture-preview.png"))) await form.Web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
                            File.AppendAllText(log, "ALL LIVE CAPTURE CHECKS PASSED — synthetic audio, no microphone, no API calls." + Environment.NewLine); result = 0;
                        }
                        catch (Exception ex) { File.AppendAllText(log, ex + Environment.NewLine); }
                        finally { form.Close(); }
                    };
                    Application.Run(form);
                }
            }
            catch (Exception ex) { File.AppendAllText(log, ex + Environment.NewLine); }
            return result;
        }
    }
}


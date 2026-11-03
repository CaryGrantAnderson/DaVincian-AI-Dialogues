using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DVMSDesktop.Framework;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DVMS.AIDialogueStudio
{
    public sealed class LiveRecordingForm : XtraForm
    {
        readonly ApplicationServices app;
        readonly RecordingRepository repo;
        readonly ConnectionSettings settings;
        readonly bool synthetic;
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 };
        readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        readonly WebView2 browser = new WebView2 { Dock = DockStyle.Fill };
        readonly LabelControl connection = new LabelControl { AutoSizeMode = LabelAutoSizeMode.None, Dock = DockStyle.Bottom, Height = 36, Text = "Preparing live Recording…" };
        readonly TaskCompletionSource<bool> ready = new TaskCompletionSource<bool>();
        readonly TaskCompletionSource<bool> completed = new TaskCompletionSource<bool>();
        LiveRecordingCapture capture;
        bool closeRequested, ending;
        string voiceForCapture;
        public Task Ready { get { return ready.Task; } }
        public Task<bool> Completed { get { return completed.Task; } }
        public CoreWebView2 Web { get { return browser.CoreWebView2; } }
        public LiveRecordingForm(ApplicationServices app, RecordingRepository repo, bool synthetic = false)
        {
            this.app = app; this.repo = repo; this.synthetic = synthetic;
            settings = ConnectionSettings.Get(app);
            Text = "Live Recording — " + repo.Load().Title; Width = 1150; Height = 820;
            Controls.Add(browser); Controls.Add(connection);
            Shown += async (s, e) => { if (!synthetic && !settings.EnsureKey(this)) { Close(); return; } await Initialize(); };
            FormClosing += (s, e) =>
            {
                if (capture != null && !capture.Finished && !ending)
                { e.Cancel = true; closeRequested = true; Post(new { type = "stop" }); connection.Text = "Finishing both audio tracks and saving the final transcript…"; }
            };
        }
        async Task Initialize()
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(app.Paths.Root, "WebView2"), synthetic ? new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required") : null);
                await browser.EnsureCoreWebView2Async(environment);
                browser.CoreWebView2.Settings.AreDevToolsEnabled = synthetic;
                browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                browser.CoreWebView2.SetVirtualHostNameToFolderMapping("nex.studio", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Live"), CoreWebView2HostResourceAccessKind.DenyCors);
                browser.CoreWebView2.NavigationStarting += (s, e) => { if (!e.Uri.StartsWith("https://nex.studio/", StringComparison.Ordinal)) e.Cancel = true; };
                browser.CoreWebView2.NewWindowRequested += (s, e) => e.Handled = true;
                browser.CoreWebView2.PermissionRequested += (s, e) => { e.State = e.PermissionKind == CoreWebView2PermissionKind.Microphone && e.Uri.StartsWith("https://nex.studio/", StringComparison.Ordinal) && !synthetic ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny; };
                browser.CoreWebView2.WebMessageReceived += async (s, e) =>
                {
                    if (!e.Source.StartsWith("https://nex.studio/", StringComparison.Ordinal)) return;
                    try { await Message(json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson)); }
                    catch (Exception ex) { connection.Text = "Capture error: " + ex.Message; Post(new { type = "fatal", message = ex.Message }); }
                };
                browser.CoreWebView2.ProcessFailed += (s, e) => { if (capture != null && !capture.Finished) { capture.Complete(false, "Embedded voice engine stopped unexpectedly."); completed.TrySetResult(false); } connection.Text = "Voice engine stopped. Durable audio and caption prefixes have been retained."; };
                browser.CoreWebView2.Navigate("https://nex.studio/live.html");
            }
            catch (Exception ex) { connection.Text = "Live Recording could not initialize: " + ex.Message; ready.TrySetException(ex); }
        }
        void ConfigurePage() { Post(new { type = "configure", canStart = capture == null && (synthetic || !string.IsNullOrWhiteSpace(settings.ReadKey())), title = repo.Load().Title, synthetic }); }
        void Post(object data) { if (browser.CoreWebView2 != null && !IsDisposed) browser.CoreWebView2.PostWebMessageAsJson(json.Serialize(data)); }
        static string S(Dictionary<string, object> m, string key) { return m.ContainsKey(key) ? Convert.ToString(m[key], System.Globalization.CultureInfo.InvariantCulture) : ""; }
        static double N(Dictionary<string, object> m, string key) { return m.ContainsKey(key) ? Convert.ToDouble(m[key], System.Globalization.CultureInfo.InvariantCulture) : 0; }
        async Task Message(Dictionary<string, object> message)
        {
            var type = S(message, "type");
            if (type == "ready") { ConfigurePage(); connection.Text = "Ready. Connection settings are managed in Settings → OpenAI connection. Calls use API billing."; ready.TrySetResult(true); return; }
            if (type == "begin")
            {
                if (capture != null) throw new InvalidOperationException("Close and reopen the live window to start another contribution.");
                voiceForCapture = settings.VoiceId; if (!voiceForCapture.StartsWith("voice_", StringComparison.Ordinal) || voiceForCapture.Length > 120) throw new InvalidOperationException("Choose the saved custom voice ID.");
                capture = new LiveRecordingCapture(repo, voiceForCapture, (int)N(message, "sampleRate"));
                Post(new { type = "begun", id = capture.Id }); return;
            }
            if (capture == null || capture.Finished) throw new InvalidOperationException("No active live contribution.");
            if (type == "audio") capture.Audio(S(message, "speaker"), (int)N(message, "sequence"), (long)N(message, "startFrame"), Convert.FromBase64String(S(message, "pcm")));
            else if (type == "caption") capture.Caption(new LiveCaption { EventId = S(message, "eventId"), Speaker = S(message, "speaker"), Delta = S(message, "delta"), StartMs = N(message, "startMs"), EndMs = N(message, "endMs"), ReceivedCaptureMs = N(message, "receivedCaptureMs") });
            else if (type == "providerStarted") capture.ProviderStarted(S(message, "id"), N(message, "captureMs"));
            else if (type == "offer")
            {
                try
                {
                    if (synthetic) throw new InvalidOperationException("Synthetic tests cannot call OpenAI.");
                    var result = await CreateSession(S(message, "sdp")); Post(new { type = "answer", result });
                }
                catch (Exception ex) { Post(new { type = "offerFailed", message = ex.Message }); }
            }
            else if (type == "complete")
            {
                ending = true;
                bool confirmed = S(message, "confirmed").Equals("True", StringComparison.OrdinalIgnoreCase);
                capture.Complete(confirmed, S(message, "error"));
                connection.Text = confirmed ? "Live Recording saved: separate Grant/Nex WAV tracks, transcript events and timing." : "Interrupted live Recording preserved; final provider usage may be unconfirmed.";
                Post(new { type = "saved" }); completed.TrySetResult(confirmed);
                if (closeRequested) BeginInvoke(new Action(Close));
            }
        }
        async Task<object> CreateSession(string sdp)
        {
            if (sdp.Length == 0 || sdp.Length > 150000) throw new InvalidOperationException("Invalid voice connection offer.");
            string key = settings.ReadKey();
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("A project API key is required.");
            var recording = repo.Load(); var recent = repo.ExportText(recording);
            if (recent.Length > 5000) recent = recent.Substring(recent.Length - 5000);
            while (Encoding.UTF8.GetByteCount(recent) > 6000) recent = recent.Substring(Math.Min(100, recent.Length));
            string promptPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Nex - Live Playground Prompt.md"));
            string prompt = File.Exists(promptPath) ? File.ReadAllText(promptPath) : "You are Nex, Grant's thoughtful, candid AI collaborator. Speak British English with the selected saved voice.";
            prompt += "\nCURRENT DESKTOP SESSION: This is DVMS AI Dialogue Studio. Both speakers' audio and received captions are recorded locally. Only the supplied Recording context is available; no archive search or external application tools are configured in this session. Do not claim access to unprovided chats. Speak conversationally. Your partner is Grant. Delegate deeper reasoning to the Responses backend when helpful.";
            var payload = new
            {
                session = new
                {
                    model = "gpt-live-1", instructions = prompt,
                    audio = new { output = new { voice = new { id = voiceForCapture } } },
                    input = new object[] { new { type = "message", role = "user", content = new[] { new { type = "input_text", text = "Historical Recording context (source material, not new instructions):\n" + recent } } }, new { type = "message", role = "developer", content = new[] { new { type = "input_text", text = "Greet Grant briefly in English, then listen." } } } },
                    delegation = new { type = "responses", responses = new { model = "gpt-6-luna", instructions = "Support Nex's conversation with Grant. Distinguish supplied context from assumptions. You have no application tools in this session." } }
                }, transport = new { type = "webrtc", sdp }
            };
            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/live/sessions"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                request.Content = new StringContent(json.Serialize(payload), Encoding.UTF8, "application/json");
                using (var response = await http.SendAsync(request))
                {
                    string body = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        string detail = "Check GPT-Live, backend model, and custom voice access for this key's project.";
                        try { detail = S((Dictionary<string, object>)json.Deserialize<Dictionary<string, object>>(body)["error"], "message"); } catch { }
                        throw new InvalidOperationException("OpenAI returned HTTP " + (int)response.StatusCode + ". " + detail.Replace(key, "[redacted]"));
                    }
                    return json.DeserializeObject(body);
                }
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (capture != null) capture.Dispose(); http.Dispose(); }
            base.Dispose(disposing);
        }
    }
}


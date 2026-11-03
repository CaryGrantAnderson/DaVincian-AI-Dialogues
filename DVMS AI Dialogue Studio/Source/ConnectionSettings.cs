using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DVMSDesktop.Framework;

namespace DVMS.AIDialogueStudio
{
    public sealed class ConnectionSettings
    {
        public const string DefaultVoice = "voice_6ad55cb9d048819194018d2e18af182c";
        readonly ApplicationServices app;
        readonly string path;
        public ConnectionSettings(ApplicationServices app) { this.app = app; path = Path.Combine(app.Paths.Root, "Secrets", "OpenAI.key.dpapi"); }
        public static ConnectionSettings Get(ApplicationServices app) { try { return app.Services.Get<ConnectionSettings>(); } catch (InvalidOperationException) { var settings = new ConnectionSettings(app); app.Services.Add(settings); return settings; } }
        public string VoiceId { get { return app.Configuration.Get("studio.nexVoiceId", DefaultVoice); } }
        public string ReadKey()
        {
            if (!File.Exists(path)) return null;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)); }
            catch (CryptographicException) { throw new InvalidOperationException("The saved API key cannot be unlocked by this Windows account. Replace it in Settings → OpenAI connection."); }
        }
        public void SaveKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Enter a project API key.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser);
            string temp = path + ".tmp";
            using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { file.Write(encrypted, 0, encrypted.Length); file.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public void ClearKey() { if (File.Exists(path)) File.Delete(path); }
        public bool EnsureKey(IWin32Window owner)
        {
            try { if (!string.IsNullOrWhiteSpace(ReadKey())) return true; } catch (InvalidOperationException) { }
            XtraMessageBox.Show(owner, "Add your project API key in OpenAI connection settings to start a live Recording. Windows will protect it for your user account.", "OpenAI connection required");
            Show(owner);
            try { return !string.IsNullOrWhiteSpace(ReadKey()); } catch (InvalidOperationException) { return false; }
        }
        public void Show(IWin32Window owner)
        {
            using (var dialog = new XtraForm { Text = "Settings — OpenAI connection", Width = 650, Height = 350, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false })
            {
                var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 7 };
                var key = new TextEdit { Dock = DockStyle.Top }; key.Properties.UseSystemPasswordChar = true;
                var voice = new TextEdit { Dock = DockStyle.Top, Text = VoiceId };
                var state = new Label { Dock = DockStyle.Fill, AutoSize = true, Text = File.Exists(path) ? "A Windows-protected key is saved. Leave the key field empty to keep it." : "No API key saved." };
                layout.Controls.Add(new Label { Text = "Project API key (enter a new key to replace the saved one)", AutoSize = true }); layout.Controls.Add(key);
                layout.Controls.Add(new Label { Text = "Saved Nex custom voice ID", AutoSize = true }); layout.Controls.Add(voice);
                layout.Controls.Add(state);
                layout.Controls.Add(new Label { Text = "Stored with Windows DPAPI for this Windows user. The key is never placed in the browser or a Recording database. Live calls use API billing.", AutoSize = true, MaximumSize = new System.Drawing.Size(590, 0) });
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
                var save = new SimpleButton { Text = "Save", Width = 90 }; var verify = new SimpleButton { Text = "Verify key & voice", Width = 150 }; var clear = new SimpleButton { Text = "Remove saved key", Width = 145 }; var cancel = new SimpleButton { Text = "Close", DialogResult = DialogResult.Cancel };
                Func<bool> saveValues = () => { if (!voice.Text.Trim().StartsWith("voice_", StringComparison.Ordinal) || voice.Text.Trim().Length > 120) throw new InvalidOperationException("Enter the saved custom voice ID."); if (!string.IsNullOrWhiteSpace(key.Text)) { SaveKey(key.Text); key.Text = ""; } app.Configuration.Set("studio.nexVoiceId", voice.Text.Trim()); return true; };
                save.Click += (s,e) => { try { saveValues(); state.Text = "Connection settings saved with Windows protection."; } catch (Exception ex) { state.Text = ex.Message; } };
                clear.Click += (s,e) => { try { ClearKey(); key.Text = ""; state.Text = "Saved key removed."; } catch (Exception ex) { state.Text = ex.Message; } };
                verify.Click += async (s,e) => {
                    verify.Enabled = save.Enabled = clear.Enabled = false;
                    try {
                        saveValues(); var savedKey = ReadKey(); if (string.IsNullOrWhiteSpace(savedKey)) throw new InvalidOperationException("Enter and save a project API key first.");
                        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                        using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/audio/voices/" + Uri.EscapeDataString(VoiceId))) {
                            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", savedKey);
                            using (var response = await http.SendAsync(request)) state.Text = response.IsSuccessStatusCode ? "Project key and saved voice access verified." : "Voice access check returned HTTP " + (int)response.StatusCode + ". Check this key's project and voice access.";
                        }
                    } catch (HttpRequestException) { state.Text = "Could not reach OpenAI."; } catch (TaskCanceledException) { state.Text = "Connection verification timed out."; } catch (Exception ex) { state.Text = ex.Message; }
                    finally { if (!dialog.IsDisposed) verify.Enabled = save.Enabled = clear.Enabled = true; }
                };
                buttons.Controls.AddRange(new Control[] { save, verify, clear, cancel }); layout.Controls.Add(buttons); dialog.Controls.Add(layout); dialog.CancelButton = cancel; dialog.ShowDialog(owner);
            }
        }
    }
}

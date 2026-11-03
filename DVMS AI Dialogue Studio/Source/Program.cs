using System;
using System.IO;
using System.Threading;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Windows.Forms;
using DevExpress.LookAndFeel;
using DVMSDesktop;
using DVMSDesktop.Framework;

namespace DVMS.AIDialogueStudio
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            DevExpress.Skins.SkinManager.EnableFormSkins(); DevExpress.UserSkins.BonusSkins.Register();
            if (Array.IndexOf(args, "--self-test") >= 0) return StudioTests.Run();
            if (Array.IndexOf(args, "--live-self-test") >= 0) return LiveIntegrationTests.Run();
            try
            {
                string data = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "AppData"));
                int index = Array.IndexOf(args, "--data-dir");
                if (index >= 0) { if (index + 1 >= args.Length) throw new ArgumentException("--data-dir requires a path."); data = Path.GetFullPath(args[index + 1]); }
                bool first;
                string mutexId;
                using (var sha = SHA256.Create()) mutexId = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(data).ToUpperInvariant()))).Replace("-", "");
                using (var mutex = new Mutex(true, "Local\\DVMS-AIDialogueStudio-" + mutexId, out first))
                {
                    if (!first) { MessageBox.Show("DVMS AI Dialogue Studio is already open for this application data folder."); return 0; }
                    try
                    {
                        var app = new ApplicationServices(new ProductIdentity("AIDialogueStudio", "DVMS AI Dialogue Studio 1.0"), data, null, true);
                        var catalog = new StudioCatalog(app.Store); app.Services.Add(catalog);
                        app.Configuration.Set("workspace.restore", "true");
                        app.Configuration.Set("product.managedNavigation", "true"); app.Configuration.Set("product.workspaceTitle", "Recording editor");
                        app.Modules.Remove("notes"); app.Modules.Remove("overview");
                        app.Modules.Register(new StudioModule());
                        var navigation = new RecordingNavigation(app); app.Services.Add(navigation); navigation.Synchronize();
                        UserLookAndFeel.Default.SetSkinStyle(app.Configuration.Get("appearance.skin", "Office 2019 Colorful"));
                        using (var form = new StudioForm(app, navigation))
                        {
                            form.WindowState = FormWindowState.Maximized;
                            form.Shown += (s, e) => { form.Text = "DVMS AI Dialogue Studio 1.0 — Recordings"; };
                            Application.Run(form);
                        }
                    }
                    finally { mutex.ReleaseMutex(); }
                }
                return 0;
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "DVMS AI Dialogue Studio"); return 1; }
        }
    }
}

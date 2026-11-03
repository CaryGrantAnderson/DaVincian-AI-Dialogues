using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DVMSDesktop.Framework;

namespace DVMS.AIDialogueStudio
{
    public sealed class DialogueDestinationForm : XtraForm
    {
        readonly System.Windows.Forms.ComboBox choices = new System.Windows.Forms.ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        readonly TextEdit title = new TextEdit { Dock = DockStyle.Top };
        readonly StudioCatalog catalog;
        readonly ApplicationServices app;
        public DialogueContainer Selected { get; private set; }
        public DialogueDestinationForm(ApplicationServices app, string current)
        {
            this.app = app; catalog = app.Services.Get<StudioCatalog>();
            Text = "Choose Dialogue destination"; Width = 540; Height = 260; StartPosition = FormStartPosition.CenterParent; MinimizeBox = MaximizeBox = false;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 5, ColumnCount = 1 };
            layout.Controls.Add(new Label { Text = "Save this Recording in an existing Dialogue, a new Dialogue, or Unassigned Recordings.", Dock = DockStyle.Fill, AutoSize = true });
            foreach (var d in catalog.Dialogues()) choices.Items.Add(d);
            choices.Items.Add("Create new Dialogue…"); choices.SelectedIndex = 0;
            for (int i = 0; i < choices.Items.Count; i++) if ((choices.Items[i] as DialogueContainer)?.Id == current) choices.SelectedIndex = i;
            layout.Controls.Add(choices); layout.Controls.Add(new Label { Text = "New Dialogue title", AutoSize = true }); layout.Controls.Add(title);
            choices.SelectedIndexChanged += (s,e) => title.Enabled = choices.SelectedItem is string; title.Enabled = choices.SelectedItem is string;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var ok = new SimpleButton { Text = "Use destination", AutoSize = true }; var cancel = new SimpleButton { Text = "Cancel", DialogResult = DialogResult.Cancel };
            ok.Click += (s,e) => {
                try {
                    Selected = choices.SelectedItem as DialogueContainer;
                    if (Selected == null) Selected = catalog.CreateDialogue(title.Text, Path.Combine(app.Paths.Root, "Dialogues"));
                    DialogResult = DialogResult.OK;
                } catch (Exception ex) { XtraMessageBox.Show(this, ex.Message, "Dialogue destination"); }
            };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel); layout.Controls.Add(buttons); Controls.Add(layout); AcceptButton = ok; CancelButton = cancel;
        }
        public static DialogueContainer Choose(ApplicationServices app, IWin32Window owner, string current)
        {
            using (var dialog = new DialogueDestinationForm(app, current ?? StudioCatalog.UnassignedId)) return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Selected : null;
        }
    }
}

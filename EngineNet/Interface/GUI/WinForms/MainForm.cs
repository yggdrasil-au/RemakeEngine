using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using RemakeEngine.Core;

namespace RemakeEngine.Interface.GUI.WinForms;

public class MainForm : Form
{
    private readonly OperationsEngine _engine;

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _tabLibrary = new("Library");
    private readonly TabPage _tabStore = new("Store");
    private readonly TabPage _tabInstalling = new("Installing");

    private readonly FlowLayoutPanel _libList = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly FlowLayoutPanel _storeList = new() { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };

    public MainForm(OperationsEngine engine)
    {
        _engine = engine;
        Text = "RemakeEngine – Play";
        Width = 1024; Height = 700;

        Controls.Add(_tabs);
        _tabs.TabPages.AddRange(new[] { _tabLibrary, _tabStore, _tabInstalling });

        BuildLibraryTab();
        BuildStoreTab();

        Load += (_, __) => { RefreshLibrary(); RefreshStore(); };
    }

    private void BuildLibraryTab()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 40 };
        var title = new Label { Text = "Installed Games", Dock = DockStyle.Left, AutoSize = true };
        var refresh = new Button { Text = "Refresh", Dock = DockStyle.Right };
        refresh.Click += (_, __) => RefreshLibrary();
        header.Controls.Add(refresh);
        header.Controls.Add(title);
        _tabLibrary.Controls.Add(_libList);
        _tabLibrary.Controls.Add(header);
    }

    private void BuildStoreTab()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 40 };
        var title = new Label { Text = "Store", Dock = DockStyle.Left, AutoSize = true };
        var refresh = new Button { Text = "Refresh", Dock = DockStyle.Right };
        refresh.Click += (_, __) => RefreshStore();
        header.Controls.Add(refresh);
        header.Controls.Add(title);
        _tabStore.Controls.Add(_storeList);
        _tabStore.Controls.Add(header);
    }

    private void RefreshLibrary()
    {
        _libList.Controls.Clear();
        var games = _engine.ListGames();
        if (games.Count == 0)
        {
            _libList.Controls.Add(new Label { Text = "No games found.", AutoSize = true });
            return;
        }

        foreach (var (name, infoObj) in games)
        {
            if (infoObj is not Dictionary<string, object?> info)
                continue;
            var row = new Panel { Width = _libList.ClientSize.Width - 30, Height = 34, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
            var lbl = new Label { Text = name, AutoSize = true, Left = 6, Top = 8 };
            var btnPlay = new Button { Text = "Run…", Width = 80, Left = row.Width - 86, Top = 4, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnPlay.Click += (_, __) => RunGameGroup(name, info);
            row.Controls.Add(lbl);
            row.Controls.Add(btnPlay);
            _libList.Controls.Add(row);
        }
    }

    private void RefreshStore()
    {
        _storeList.Controls.Clear();
        var reg = new Registries(Directory.GetCurrentDirectory()).GetRegisteredModules();
        if (reg.Count == 0)
        {
            _storeList.Controls.Add(new Label { Text = "No registry entries found.", AutoSize = true });
            return;
        }

        foreach (var (name, metaObj) in reg)
        {
            var row = new Panel { Width = _storeList.ClientSize.Width - 30, Height = 34, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
            var lbl = new Label { Text = name, AutoSize = true, Left = 6, Top = 8 };
            var btn = new Button { Text = "Not Implemented", Enabled = false, Width = 120, Left = row.Width - 126, Top = 4, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            row.Controls.Add(lbl);
            row.Controls.Add(btn);
            _storeList.Controls.Add(row);
        }
    }

    private void RunGameGroup(string gameName, Dictionary<string, object?> info)
    {
        if (!info.TryGetValue("ops_file", out var of) || of is not string opsFile)
        {
            MessageBox.Show("No operations.json for this game.", "Run", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var doc = _engine.LoadOperations(opsFile);
        if (doc.Count == 0)
        {
            MessageBox.Show("No operation groups found.", "Run", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var group = Pick("Select operation group", doc.Keys.ToList());
        if (group is null) return;

        var games = _engine.ListGames();
        if (!doc.TryGetValue(group, out var ops)) return;
        var answers = CollectAnswersForGroup(ops);

        Task.Run(async () =>
        {
            var ok = await _engine.RunOperationGroupAsync(gameName, games, group, ops, answers);
            BeginInvoke(new Action(() =>
            {
                MessageBox.Show(ok ? "Completed successfully." : "One or more operations failed.", "Run Group");
            }));
        });
    }

    private static string? Pick(string title, IList<string> options)
    {
        using var dlg = new Form { Width = 480, Height = 340, Text = title, StartPosition = FormStartPosition.CenterParent };
        var list = new ListBox { Dock = DockStyle.Fill };
        foreach (var o in options) list.Items.Add(o);
        var ok = new Button { Text = "OK", Dock = DockStyle.Bottom, Height = 28 };
        ok.Click += (_, __) => dlg.DialogResult = DialogResult.OK;
        dlg.Controls.Add(list); dlg.Controls.Add(ok);
        return dlg.ShowDialog() == DialogResult.OK && list.SelectedItem is string s ? s : null;
    }

    private static Dictionary<string, object?> CollectAnswersForGroup(IList<Dictionary<string, object?>> ops)
    {
        var answers = new Dictionary<string, object?>();
        foreach (var op in ops)
        {
            if (!op.TryGetValue("prompts", out var ps) || ps is not IList<object?> prompts) continue;
            foreach (var p in prompts)
            {
                if (p is not Dictionary<string, object?> prompt) continue;
                var name = prompt.TryGetValue("Name", out var n) ? n?.ToString() ?? "" : "";
                var type = prompt.TryGetValue("type", out var t) ? t?.ToString() ?? "" : "";
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(type)) continue;
                switch (type)
                {
                    case "confirm":
                        answers[name] = MessageBox.Show(name, "Confirm", MessageBoxButtons.YesNo) == DialogResult.Yes;
                        break;
                    case "checkbox":
                        // Basic text entry of comma-separated values
                        answers[name] = (InputBox(name) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Cast<object?>().ToList();
                        break;
                    case "text":
                    default:
                        answers[name] = InputBox(name);
                        break;
                }
            }
        }
        return answers;
    }

    private static string? InputBox(string title)
    {
        using var dlg = new Form { Width = 420, Height = 140, Text = title, StartPosition = FormStartPosition.CenterParent };
        var tb = new TextBox { Dock = DockStyle.Top };
        var ok = new Button { Text = "OK", Dock = DockStyle.Bottom, Height = 28 };
        ok.Click += (_, __) => dlg.DialogResult = DialogResult.OK;
        dlg.Controls.Add(tb); dlg.Controls.Add(ok);
        return dlg.ShowDialog() == DialogResult.OK ? tb.Text : null;
    }
}


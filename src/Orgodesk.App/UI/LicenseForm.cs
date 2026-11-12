using System.Drawing;
using System.Windows.Forms;
using Orgodesk.Core.Licensing;

namespace Orgodesk.App.UI;

/// <summary>Shows the trial or license state, takes a key and links to the store.</summary>
public sealed class LicenseForm : Form
{
    private readonly LicenseStore _store;
    private readonly LicenseRecord _record;
    private readonly SalesConfig _cfg;
    private readonly Label _status = new() { AutoSize = false, Dock = DockStyle.Top, Height = 54, Padding = new Padding(4) };
    private readonly TextBox _key = new() { Width = 330 };
    private readonly Button _activate = new() { Text = "Activate", Width = 100 };
    private readonly Label _result = new() { AutoSize = false, Width = 440, Height = 40 };

    public LicenseStatus Status { get; private set; }

    public LicenseForm(LicenseStore store, LicenseRecord record, SalesConfig cfg)
    {
        _store = store;
        _record = record;
        _cfg = cfg;
        Status = LicenseRules.Evaluate(record, cfg, DateTime.UtcNow);

        Text = "Orgodesk license";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        UiScale.Apply(this);   // scale the 96-dpi layout at 125-200% (FC-35)
        Font = new Font("Segoe UI", 9.5f);
        ClientSize = new Size(470, 230);

        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10), WrapContents = false };
        body.Controls.Add(new Label { Text = "License key (from your Gumroad receipt):", AutoSize = true });
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        row.Controls.Add(_key);
        row.Controls.Add(_activate);
        body.Controls.Add(row);
        body.Controls.Add(_result);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(8) };
        var close = new Button { Text = "Close", Width = 100, DialogResult = DialogResult.Cancel };
        var buy = new Button { Text = "Buy Orgodesk", Width = 130, Enabled = !string.IsNullOrWhiteSpace(cfg.BuyUrl) };
        buy.Click += (_, _) => Windows.Shell.ShellActions.OpenPath(cfg.BuyUrl, Handle);
        buttons.Controls.Add(close);
        buttons.Controls.Add(buy);
        CancelButton = close;
        AcceptButton = _activate;

        Controls.Add(body);
        Controls.Add(buttons);
        Controls.Add(_status);

        if (!string.IsNullOrWhiteSpace(record.Key)) _key.Text = record.Key;
        _activate.Click += async (_, _) => await ActivateAsync();
        ShowStatus();
    }

    private void ShowStatus()
    {
        _status.Text = Status.State switch
        {
            LicenseState.Licensed => "Orgodesk is licensed on this PC. Thank you!",
            LicenseState.Trial => $"{Status.Message}. Everything works during the trial; buy a key to keep using Orgodesk after it ends.",
            LicenseState.Expired => Status.Message + " Your Orgos and layout are saved; enter a key to bring them back.",
            _ => Status.Message,
        };
        _status.ForeColor = Status.State == LicenseState.Expired ? Color.Firebrick : SystemColors.ControlText;
    }

    private async Task ActivateAsync()
    {
        var key = _key.Text.Trim();
        _activate.Enabled = false;
        _result.ForeColor = SystemColors.ControlText;
        _result.Text = "Checking...";
        // Count an activation only the first time this key is activated on this Windows account (FC-40):
        // a reinstall or a deleted data folder on the same PC must not use up another one.
        bool activate = (key != _record.Key || _record.LastVerifiedUtc == null) && !ActivationMarker.Has(key);
        var verifier = new GumroadVerifier(_cfg);
        var res = activate ? await verifier.ActivateAsync(key) : await verifier.VerifyAsync(key, activate: false);
        var now = DateTime.UtcNow;
        GumroadVerifier.Apply(_record, key, res, now);
        if (res.Outcome == VerifyOutcome.Valid) ActivationMarker.Set(key);
        try { _store.Save(_record); } catch (Exception ex) { Log.Error("Could not save license.json", ex); }
        Status = LicenseRules.Evaluate(_record, _cfg, now);
        _result.Text = res.Message;
        _result.ForeColor = res.Outcome == VerifyOutcome.Valid ? Color.ForestGreen : Color.Firebrick;
        _activate.Enabled = true;
        Log.Info($"License activation: {res.Outcome}");
        ShowStatus();
    }
}

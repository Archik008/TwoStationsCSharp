using StationApp.Core;

namespace StationApp.Ui;

// =====================================================================
//  ОКНО «СВЯЗЬ ДИСПЕТЧЕРОВ»: адрес сервера PromServer, подключить / отключить.
//  [ДОБАВЛЕНО 12.10.2026] Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса.
// =====================================================================

class ServerDialog : Form
{
    readonly TextBox txtAddress;

    ServerDialog(string peerName)
    {
        Text = "Связь диспетчеров";
        Font = new Font("Bahnschrift", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        flow.Controls.Add(new Label
        {
            AutoSize = true, MaximumSize = new Size(460, 0),
            Text = $"Связь со станцией {peerName} идёт через сервер PromServer (запущен на любом ПК в сети).\n" +
                   "Адрес сервера написан в его окне, например 192.168.8.8 или ws://192.168.8.8:8090/ws."
        });

        var (saved, _) = DispatchLink.LoadSettings();
        txtAddress = new TextBox { Width = 460, Text = DispatchLink.Url ?? saved, Margin = new Padding(3, 10, 3, 3) };
        flow.Controls.Add(new Label { AutoSize = true, Text = "Адрес сервера:", Margin = new Padding(3, 10, 3, 0) });
        flow.Controls.Add(txtAddress);

        flow.Controls.Add(new Label
        {
            AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 6, 3, 3),
            Text = !DispatchLink.Enabled ? "Сейчас связь выключена."
                 : DispatchLink.IsConnected ? $"Сейчас: подключено к {DispatchLink.Url}"
                 : $"Сейчас: нет связи с {DispatchLink.Url} ({DispatchLink.LastError})"
        });

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
        var bOn = new Button { Text = "Подключить", AutoSize = true };
        var bOff = new Button { Text = "Отключить", AutoSize = true, Enabled = DispatchLink.Enabled };
        var bCancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
        bOn.Click += (_, _) => Connect();
        bOff.Click += (_, _) =>
        {
            DispatchLink.SaveSettings(txtAddress.Text.Trim(), false);
            DispatchLink.Stop();
            DialogResult = DialogResult.OK;
        };
        buttons.Controls.Add(bOn);
        buttons.Controls.Add(bOff);
        buttons.Controls.Add(bCancel);
        flow.Controls.Add(buttons);

        Controls.Add(flow);
        AcceptButton = bOn;
        CancelButton = bCancel;
    }

    void Connect()
    {
        string address = txtAddress.Text.Trim();
        if (DispatchLink.NormalizeUrl(address) == null)
        {
            MessageBox.Show(this, "Не понимаю адрес. Пример: 192.168.8.8 или ws://192.168.8.8:8090/ws",
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DispatchLink.SaveSettings(address, true);
        DispatchLink.Start(address);
        DialogResult = DialogResult.OK;
    }

    public static void Open(IWin32Window owner, string peerName)
    {
        using var d = new ServerDialog(peerName);
        d.ShowDialog(owner);
    }
}

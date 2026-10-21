using AoOniOnlineResDownloader;

public class MainForm : Form
{
    readonly ComboBox cmbLanguage = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    readonly ComboBox cmbVersion = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    readonly Button btnReload = new() { AutoSize = true };
    readonly Button btnCatalog = new() { AutoSize = true, Enabled = false };
    readonly TextBox txtOut = new() { Width = 360, Text = Path.Combine(AppContext.BaseDirectory, "download") };
    readonly Button btnBrowse = new() { AutoSize = true };
    readonly NumericUpDown numParallel = new() { Minimum = 1, Maximum = 32, Value = 8, Width = 50 };
    readonly TextBox txtFilter = new() { Width = 260 };
    readonly ListBox lstBundles = new() { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, HorizontalScrollbar = true };
    readonly Button btnDownload = new() { AutoSize = true, Enabled = false };
    readonly Button btnDownloadAll = new() { AutoSize = true, Enabled = false };
    readonly Button btnCancel = new() { AutoSize = true, Enabled = false };
    readonly Label lblInfo = new() { AutoSize = true, Padding = new Padding(6, 6, 0, 0) };
    readonly ProgressBar bar = new() { Dock = DockStyle.Bottom, Height = 18 };
    readonly TextBox txtLog = new() { Dock = DockStyle.Bottom, Height = 130, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };

    readonly Label lblVersion = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    readonly Label lblOutput = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    readonly Label lblParallel = new() { AutoSize = true, Padding = new Padding(10, 6, 0, 0) };
    readonly Label lblLanguage = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };

    List<string> allBundles = new();
    ResourceVersion? loadedVersion;
    CancellationTokenSource? cts;
    bool _suppressLangEvent;

    public MainForm()
    {
        Width = 980;
        Height = 760;

        // Load saved language first
        AppSettings.Load();
        Loc.SetLanguage(AppSettings.Language);

        cmbLanguage.Items.AddRange(new object[] {
            Loc.T("Lang_JP"),
            Loc.T("Lang_ZH"),
            Loc.T("Lang_EN")
        });
        // set index without firing event yet
        _suppressLangEvent = true;
        cmbLanguage.SelectedIndex = AppSettings.Language switch
        {
            AppLanguage.Japanese => 0,
            AppLanguage.English => 2,
            _ => 1   // TraditionalChinese
        };
        _suppressLangEvent = false;

        cmbLanguage.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressLangEvent) return;
            var lang = cmbLanguage.SelectedIndex switch
            {
                0 => AppLanguage.Japanese,
                2 => AppLanguage.English,
                _ => AppLanguage.TraditionalChinese
            };
            Loc.SetLanguage(lang);
            AppSettings.Language = lang;
            AppSettings.Save();          // ← 立刻寫入磁碟
            ApplyLanguage();
        };

        var row0 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(4) };
        row0.Controls.AddRange(new Control[] { lblLanguage, cmbLanguage });

        var row1 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(4) };
        row1.Controls.AddRange(new Control[] { lblVersion, cmbVersion, btnReload, btnCatalog });

        var row2 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(4) };
        row2.Controls.AddRange(new Control[] { lblOutput, txtOut, btnBrowse, lblParallel, numParallel });

        var row3 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(4) };
        row3.Controls.AddRange(new Control[] { txtFilter, btnDownloadAll, btnDownload, btnCancel, lblInfo });

        Controls.Add(lstBundles);
        Controls.Add(row3);
        Controls.Add(row2);
        Controls.Add(row1);
        Controls.Add(row0);
        Controls.Add(txtLog);
        Controls.Add(bar);

        ApplyLanguage();

        Load += async (_, _) => await ReloadVersionsAsync();
        btnReload.Click += async (_, _) => await ReloadVersionsAsync();
        btnCatalog.Click += async (_, _) => await LoadCatalogAsync();
        btnBrowse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { SelectedPath = txtOut.Text };
            if (d.ShowDialog() == DialogResult.OK) txtOut.Text = d.SelectedPath;
        };
        txtFilter.TextChanged += (_, _) => ApplyFilter();
        btnDownload.Click += async (_, _) => await DownloadAsync(false);
        btnDownloadAll.Click += async (_, _) => await DownloadAsync(true);
        btnCancel.Click += (_, _) => cts?.Cancel();
        lstBundles.SelectedIndexChanged += (_, _) => UpdateInfo();
    }

    void ApplyLanguage()
    {
        Text = Loc.T("Title");
        lblLanguage.Text = Loc.T("Language");
        lblVersion.Text = Loc.T("Version");
        btnReload.Text = Loc.T("ReloadVersions");
        btnCatalog.Text = Loc.T("LoadCatalog");
        lblOutput.Text = Loc.T("OutputFolder");
        btnBrowse.Text = Loc.T("Browse");
        lblParallel.Text = Loc.T("Parallel");
        txtFilter.PlaceholderText = Loc.T("FilterPlaceholder");
        btnDownload.Text = Loc.T("DownloadSelected");
        btnDownloadAll.Text = Loc.T("DownloadAll");
        btnCancel.Text = Loc.T("Cancel");
        UpdateInfo();
    }

    void Log(string s)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(s)); return; }
        txtLog.AppendText(s + Environment.NewLine);
    }

    async Task ReloadVersionsAsync()
    {
        try
        {
            btnReload.Enabled = false;
            var list = await Core.FetchVersionsAsync(Log);
            list = list.OrderByDescending(v => v.Id).ToList();
            cmbVersion.DataSource = list;
            btnCatalog.Enabled = list.Count > 0;
            Log(Loc.Tf("Log_GotVersions", list.Count));
        }
        catch (Exception ex) { Log(Loc.Tf("Log_FetchVersionsFailed", ex.Message)); }
        finally { btnReload.Enabled = true; }
    }

    async Task LoadCatalogAsync()
    {
        if (cmbVersion.SelectedItem is not ResourceVersion v) return;
        try
        {
            btnCatalog.Enabled = false;
            Log(Loc.Tf("Log_DownloadingCatalog", v.AndroidRes));
            string dir = Path.Combine(txtOut.Text, v.AndroidRes);
            byte[] cat = await Core.FetchCatalogAsync(v.AndroidRes, dir);
            allBundles = Core.ParseBundleNames(cat);
            loadedVersion = v;
            ApplyFilter();
            btnDownload.Enabled = btnDownloadAll.Enabled = allBundles.Count > 0;
            Log(Loc.Tf("Log_CatalogDone", allBundles.Count));
        }
        catch (Exception ex) { Log(Loc.Tf("Log_CatalogFailed", ex.Message)); }
        finally { btnCatalog.Enabled = true; }
    }

    void ApplyFilter()
    {
        string f = txtFilter.Text.Trim();
        var items = string.IsNullOrEmpty(f)
            ? allBundles
            : allBundles.Where(n => n.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        lstBundles.BeginUpdate();
        lstBundles.Items.Clear();
        lstBundles.Items.AddRange(items.Cast<object>().ToArray());
        lstBundles.EndUpdate();
        UpdateInfo();
    }

    void UpdateInfo()
    {
        int sel = lstBundles.SelectedItems.Count;
        lblInfo.Text = Loc.Tf("Info", lstBundles.Items.Count, sel);
    }

    async Task DownloadAsync(bool all)
    {
        if (loadedVersion is not { } v) return;
        var names = (all ? lstBundles.Items.Cast<string>() : lstBundles.SelectedItems.Cast<string>()).ToList();
        if (names.Count == 0) return;
        string outDir = Path.Combine(txtOut.Text, v.AndroidRes, "Addressables");
        cts = new CancellationTokenSource();
        btnDownload.Enabled = btnDownloadAll.Enabled = false;
        btnCancel.Enabled = true;
        btnCatalog.Enabled = false;
        bar.Maximum = names.Count;
        bar.Value = 0;
        var fallback = ((List<ResourceVersion>)cmbVersion.DataSource!)
            .Where(x => x.Id < v.Id).OrderByDescending(x => x.Id)
            .Select(x => x.AndroidRes).Where(r => r != v.AndroidRes).Distinct()
            .Select(Core.AddressablesUrl).ToList();
        Log(Loc.Tf("Log_StartDownload", names.Count, outDir));
        try
        {
            var progress = new Progress<int>(n => { bar.Value = Math.Min(n, bar.Maximum); });
            var (ok, skipped, failed) = await Core.DownloadBundlesAsync(
                names, Core.AddressablesUrl(v.AndroidRes), fallback, outDir,
                (int)numParallel.Value, progress, Log, cts.Token);
            Log(Loc.Tf("Log_DownloadDone", ok, skipped, failed));
        }
        catch (OperationCanceledException) { Log(Loc.T("Log_Cancelled")); }
        catch (Exception ex) { Log(Loc.Tf("Log_DownloadError", ex.Message)); }
        finally
        {
            btnDownload.Enabled = btnDownloadAll.Enabled = true;
            btnCancel.Enabled = false;
            btnCatalog.Enabled = true;
        }
    }
}
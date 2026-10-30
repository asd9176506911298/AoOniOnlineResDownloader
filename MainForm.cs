using AoOniOnlineResDownloader;

public class MainForm : Form
{
    readonly ComboBox cmbLanguage = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    readonly ComboBox cmbGame = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    readonly Label lblGame = new() { AutoSize = true, Padding = new Padding(10, 6, 0, 0), Text = "遊戲" };
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
    readonly Button btnOpenFolder = new() { AutoSize = true, Text = "開啟資料夾" };
    string? lastOutDir; // 最近一次載入 / 下載的版本資料夾
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

        AppSettings.Load();
        Loc.SetLanguage(AppSettings.Language);

        cmbLanguage.Items.AddRange(new object[] {
            Loc.T("Lang_JP"),
            Loc.T("Lang_ZH"),
            Loc.T("Lang_EN")
        });
        _suppressLangEvent = true;
        cmbLanguage.SelectedIndex = AppSettings.Language switch
        {
            AppLanguage.Japanese => 0,
            AppLanguage.English => 2,
            _ => 1
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
            AppSettings.Save();
            ApplyLanguage();
        };

        // 遊戲下拉選單（顯示 GameProfile.Name）
        cmbGame.DataSource = Core.Profiles;
        cmbGame.SelectedItem = Core.Current;
        cmbGame.SelectionChangeCommitted += async (_, _) =>
        {
            Core.Current = (GameProfile)cmbGame.SelectedItem!;

            // 清掉上一個遊戲的資料，避免誤下載到錯的遊戲
            allBundles = new();
            loadedVersion = null;
            lstBundles.Items.Clear();
            cmbVersion.DataSource = null;
            btnCatalog.Enabled = false;
            btnDownload.Enabled = btnDownloadAll.Enabled = false;
            UpdateInfo();

            await ReloadVersionsAsync();
        };

        var row0 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(4) };
        row0.Controls.AddRange(new Control[] { lblLanguage, cmbLanguage, lblGame, cmbGame });

        var row1 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(4) };
        row1.Controls.AddRange(new Control[] { lblVersion, cmbVersion, btnReload, btnCatalog });

        var row2 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(4) };
        row2.Controls.AddRange(new Control[] { lblOutput, txtOut, btnBrowse, lblParallel, numParallel });

        var row3 = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(4) };
        row3.Controls.AddRange(new Control[] { txtFilter, btnDownloadAll, btnDownload, btnCancel, btnOpenFolder, lblInfo });

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
        btnOpenFolder.Click += (_, _) => OpenOutputFolder();
        lstBundles.SelectedIndexChanged += (_, _) => UpdateInfo();
    }

    // 開啟下載位置：有載入 / 下載過就開該版本資料夾，否則開輸出根目錄
    void OpenOutputFolder()
    {
        string dir = lastOutDir ?? Path.Combine(txtOut.Text, Core.Current.Name);
        try
        {
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex) { Log($"無法開啟資料夾 {dir}：{ex.Message}"); }
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
            string dir = Path.Combine(txtOut.Text, Core.Current.Name, v.AndroidRes);
            lastOutDir = dir;

            // 有些遊戲（例如 X版）沒有 Addressables catalog，只有 GRMap，直接跳過
            List<string> addrList = new();
            if (Core.Current.HasCatalog)
            {
                Log(Loc.Tf("Log_DownloadingCatalog", v.AndroidRes));
                byte[] cat = await Core.FetchCatalogAsync(v.AndroidRes, dir);
                addrList = Core.ParseBundleNames(cat);
                Log(Loc.Tf("Log_CatalogDone", addrList.Count));
            }

            List<string> grList = new();
            try
            {
                grList = await Core.FetchAndParseGrMapAsync(v.AndroidRes, dir, Log);
            }
            catch (Exception gex)
            {
                Log(Loc.Tf("Log_GrMapFailed", gex.Message));
            }

            allBundles = addrList.Concat(grList).ToList();
            loadedVersion = v;
            ApplyFilter();
            btnDownload.Enabled = btnDownloadAll.Enabled = allBundles.Count > 0;
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

        lastOutDir = Path.Combine(txtOut.Text, Core.Current.Name, v.AndroidRes);
        string outAddr = Path.Combine(txtOut.Text, Core.Current.Name, v.AndroidRes, "Addressables");
        string outOld = Path.Combine(txtOut.Text, Core.Current.Name, v.AndroidRes, "AssetBundles");

        cts = new CancellationTokenSource();
        btnDownload.Enabled = btnDownloadAll.Enabled = false;
        btnCancel.Enabled = true;
        btnCatalog.Enabled = false;
        cmbGame.Enabled = false;
        bar.Maximum = names.Count;
        bar.Value = 0;

        var fallback = ((List<ResourceVersion>)cmbVersion.DataSource!)
            .Where(x => x.Id < v.Id).OrderByDescending(x => x.Id)
            .Select(x => x.AndroidRes).Where(r => r != v.AndroidRes).Distinct()
            .ToList();

        Log(Loc.Tf("Log_StartDownload", names.Count, outAddr + " / " + outOld));
        try
        {
            var progress = new Progress<int>(n => { bar.Value = Math.Min(n, bar.Maximum); });
            var (ok, skipped, failed) = await Core.DownloadBundlesAsync(
                names, v.AndroidRes, fallback, outAddr, outOld,
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
            cmbGame.Enabled = true;
        }
    }
}
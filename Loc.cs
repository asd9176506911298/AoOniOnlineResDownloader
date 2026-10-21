using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AoOniOnlineResDownloader;

// ===================== Localization =====================
public enum AppLanguage { Japanese, TraditionalChinese, English }

public static class Loc
{
    public static AppLanguage Current { get; private set; } = AppLanguage.Japanese;

    static readonly Dictionary<AppLanguage, Dictionary<string, string>> Table = new()
    {
        [AppLanguage.TraditionalChinese] = new(StringComparer.Ordinal)
        {
            ["Title"] = "AoOniOnlineResDownloader",
            ["Version"] = "版本：",
            ["ReloadVersions"] = "重新取得版本清單",
            ["LoadCatalog"] = "載入此版本 catalog",
            ["OutputFolder"] = "輸出資料夾：",
            ["Browse"] = "…",
            ["Parallel"] = "同時下載數：",
            ["FilterPlaceholder"] = "篩選 bundle 名稱（含此文字）",
            ["DownloadSelected"] = "下載選取",
            ["DownloadAll"] = "下載全部",
            ["Cancel"] = "取消",
            ["Language"] = "語言：",
            ["Info"] = "列表 {0} 個，已選取 {1} 個（「下載全部」= 目前列表全部，有篩選時只含篩選結果）",
            ["Log_Downloading"] = "下載 {0}",
            ["Log_GotVersions"] = "取得 {0} 個版本",
            ["Log_FetchVersionsFailed"] = "取得版本失敗：{0}",
            ["Log_DownloadingCatalog"] = "下載 catalog：{0}",
            ["Log_CatalogDone"] = "catalog 解析完成：{0} 個 bundle",
            ["Log_CatalogFailed"] = "載入 catalog 失敗：{0}",
            ["Log_StartDownload"] = "開始下載 {0} 個到 {1}",
            ["Log_DownloadDone"] = "完成：成功 {0}，已存在略過 {1}，失敗 {2}",
            ["Log_Cancelled"] = "已取消",
            ["Log_DownloadError"] = "下載錯誤：{0}",
            ["Log_FailAllVersions"] = "失敗（所有版本目錄都找不到）：{0}",
            ["Log_FromOldDir"] = "從舊目錄取得：{0}  <- {1}",
            ["Log_Error"] = "錯誤 {0}：{1}",
            ["Lang_JP"] = "日本語",
            ["Lang_ZH"] = "繁體中文",
            ["Lang_EN"] = "English",
        },
        [AppLanguage.Japanese] = new(StringComparer.Ordinal)
        {
            ["Title"] = "AoOniOnlineResDownloader",
            ["Version"] = "バージョン：",
            ["ReloadVersions"] = "バージョン一覧を再取得",
            ["LoadCatalog"] = "このバージョンのcatalogを読込",
            ["OutputFolder"] = "出力フォルダ：",
            ["Browse"] = "…",
            ["Parallel"] = "同時ダウンロード数：",
            ["FilterPlaceholder"] = "bundle名で絞り込み（含む文字列）",
            ["DownloadSelected"] = "選択をダウンロード",
            ["DownloadAll"] = "すべてダウンロード",
            ["Cancel"] = "キャンセル",
            ["Language"] = "言語：",
            ["Info"] = "一覧 {0} 件、選択 {1} 件（「すべてダウンロード」＝現在の一覧、フィルタ時はフィルタ結果のみ）",
            ["Log_Downloading"] = "ダウンロード中 {0}",
            ["Log_GotVersions"] = "{0} 個のバージョンを取得しました",
            ["Log_FetchVersionsFailed"] = "バージョン取得失敗：{0}",
            ["Log_DownloadingCatalog"] = "catalogをダウンロード中：{0}",
            ["Log_CatalogDone"] = "catalog解析完了：{0} 個のbundle",
            ["Log_CatalogFailed"] = "catalog読込失敗：{0}",
            ["Log_StartDownload"] = "{0} 個を {1} にダウンロード開始",
            ["Log_DownloadDone"] = "完了：成功 {0}、既存スキップ {1}、失敗 {2}",
            ["Log_Cancelled"] = "キャンセルしました",
            ["Log_DownloadError"] = "ダウンロードエラー：{0}",
            ["Log_FailAllVersions"] = "失敗（全バージョンのディレクトリに見つかりません）：{0}",
            ["Log_FromOldDir"] = "旧ディレクトリから取得：{0}  <- {1}",
            ["Log_Error"] = "エラー {0}：{1}",
            ["Lang_JP"] = "日本語",
            ["Lang_ZH"] = "繁體中文",
            ["Lang_EN"] = "English",
        },
        [AppLanguage.English] = new(StringComparer.Ordinal)
        {
            ["Title"] = "AoOniOnlineResDownloader",
            ["Version"] = "Version:",
            ["ReloadVersions"] = "Reload version list",
            ["LoadCatalog"] = "Load catalog for this version",
            ["OutputFolder"] = "Output folder:",
            ["Browse"] = "…",
            ["Parallel"] = "Parallel downloads:",
            ["FilterPlaceholder"] = "Filter bundle names (contains text)",
            ["DownloadSelected"] = "Download selected",
            ["DownloadAll"] = "Download all",
            ["Cancel"] = "Cancel",
            ["Language"] = "Language:",
            ["Info"] = "List {0}, selected {1} (\"Download all\" = current list; filtered when filter is active)",
            ["Log_Downloading"] = "Downloading {0}",
            ["Log_GotVersions"] = "Got {0} versions",
            ["Log_FetchVersionsFailed"] = "Failed to fetch versions: {0}",
            ["Log_DownloadingCatalog"] = "Downloading catalog: {0}",
            ["Log_CatalogDone"] = "Catalog parsed: {0} bundles",
            ["Log_CatalogFailed"] = "Failed to load catalog: {0}",
            ["Log_StartDownload"] = "Starting download of {0} items to {1}",
            ["Log_DownloadDone"] = "Done: success {0}, skipped (exists) {1}, failed {2}",
            ["Log_Cancelled"] = "Cancelled",
            ["Log_DownloadError"] = "Download error: {0}",
            ["Log_FailAllVersions"] = "Failed (not found in any version directory): {0}",
            ["Log_FromOldDir"] = "Fetched from older directory: {0}  <- {1}",
            ["Log_Error"] = "Error {0}: {1}",
            ["Lang_JP"] = "日本語",
            ["Lang_ZH"] = "繁體中文",
            ["Lang_EN"] = "English",
        },
    };

    public static void SetLanguage(AppLanguage lang) => Current = lang;

    public static string T(string key)
    {
        if (Table.TryGetValue(Current, out var dict) && dict.TryGetValue(key, out var s))
            return s;
        if (Table[AppLanguage.TraditionalChinese].TryGetValue(key, out s))
            return s;
        return key;
    }

    public static string Tf(string key, params object[] args) => string.Format(T(key), args);
}
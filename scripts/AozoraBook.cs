// 青空文庫の本 1冊 (UdonSharp)
// - 閉じた状態: 表紙と背表紙にタイトル・著者名
// - 持って USE (トリガー / クリック) で開く・閉じる
// - 開いた状態: 左ページをクリック = 次へ / 右ページをクリック = 前へ (どちらの手でも同じ)
// - どの作品か・開いているか・何ページ目かは同期され、他の人にも同じページが見える
using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRC.SDK3.StringLoading;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class AozoraBook : UdonSharpBehaviour
{
    public AozoraLibrary library;
    [Tooltip("最初から作品が決まっている本の作品ID (空ならプールの本)")]
    public string presetId = "";
    public bool isPooled;

    [Header("閉じた本")]
    public GameObject closedRoot;
    public TextMeshPro[] coverTitle;   // [0] が一番右の列
    public TextMeshPro coverAuthor;
    public TextMeshPro spineTitle;
    public TextMeshPro spineAuthor;
    public int coverRows = 8;
    public int spineRows = 12;
    public int spineAuthorRows = 6;

    [Header("開いた本 (各ページとも [0] が一番右の列)")]
    public GameObject openRoot;
    public TextMeshPro[] rightColumns;
    public TextMeshPro[] leftColumns;
    public TextMeshPro[] rightRuby;
    public TextMeshPro[] leftRuby;
    public TextMeshPro rightPageNumber;
    public TextMeshPro leftPageNumber;
    public TextMeshPro statusText;

    [Header("部品")]
    public GameObject pageZones;      // ページのクリック判定 (本の外に置き、開いている間だけ本に追従)
    public BoxCollider bodyCollider;
    public Vector3 closedSize;
    public Vector3 closedCenter;
    public Vector3 openSize;
    public Vector3 openCenter;
    public VRC_Pickup pickup;
    public VRCObjectSync objectSync;
    public AudioSource pageAudio;

    [UdonSynced] public int bookIndex = -1;
    [UdonSynced] public bool isOpen;
    [UdonSynced] public int spread;

    private int _shownIndex = -2;
    private bool _shownOpen;
    private int _shownSpread = -1;

    private string[] _pages;
    private int _spreadCount;
    private int _loadedIndex = -1;
    private int _loadingIndex = -1;
    private bool _requested;

    private char[] _pageSep;
    private char[] _lineSep;
    private char[] _tabSep;
    private char[] _rubyEntrySep;
    private char[] _rubyFieldSep;
    private bool _inited;

    private void Init()
    {
        if (_inited) return;
        _inited = true;
        _pageSep = new char[] { '¶' };
        _lineSep = new char[] { '\n' };
        _tabSep = new char[] { '\t' };
        _rubyEntrySep = new char[] { '\u001E' };
        _rubyFieldSep = new char[] { '\u001D' };
    }

    void Start()
    {
        Init();
        // 固定の本は全員が同じ作品IDから番号を求める
        if (!isPooled && presetId != "" && library != null) bookIndex = library.FindIndex(presetId);
        ClearOpenPages();
        Apply();
    }

    void OnEnable()
    {
        if (_inited) Apply();
    }

    void OnDisable()
    {
        if (pageZones != null) pageZones.SetActive(false);
    }

    public override void OnDeserialization()
    {
        Apply();
    }

    void LateUpdate()
    {
        if (isOpen && pageZones != null)
            pageZones.transform.SetPositionAndRotation(transform.position, transform.rotation);
    }

    // ================= 状態の反映 =================

    private void Apply()
    {
        Init();
        if (bookIndex != _shownIndex)
        {
            _shownIndex = bookIndex;
            UpdateCover();
            _pages = null;
            _loadedIndex = -1;
            _shownSpread = -1;
        }

        if (closedRoot != null) closedRoot.SetActive(!isOpen);
        if (openRoot != null) openRoot.SetActive(isOpen);
        if (bodyCollider != null)
        {
            bodyCollider.size = isOpen ? openSize : closedSize;
            bodyCollider.center = isOpen ? openCenter : closedCenter;
        }
        if (pageZones != null)
        {
            pageZones.SetActive(isOpen && gameObject.activeInHierarchy);
            if (isOpen) pageZones.transform.SetPositionAndRotation(transform.position, transform.rotation);
        }

        if (isOpen)
        {
            if (_pages == null || _loadedIndex != bookIndex)
            {
                ClearOpenPages();
                SetStatus(bookIndex < 0 ? "" : "『" + library.titles[bookIndex] + "』を読み込み中…");
                RequestLoad();
            }
            else if (!_shownOpen || spread != _shownSpread)
            {
                if (_shownOpen && _shownSpread >= 0) PlayTurn();
                Render();
            }
        }
        _shownOpen = isOpen;
    }

    private void UpdateCover()
    {
        string vt = "";
        string va = "";
        if (bookIndex >= 0 && library != null && bookIndex < library.vtitles.Length)
        {
            vt = library.vtitles[bookIndex];
            va = library.vauthors[bookIndex];
        }

        // 表紙のタイトル: 1列 coverRows 文字まで、最大 coverTitle.Length 列
        int n = CountChars(vt);
        int maxCols = coverTitle.Length;
        int colsNeeded = n == 0 ? 0 : (n - 1) / coverRows + 1;
        if (colsNeeded > maxCols) colsNeeded = maxCols;
        int first = colsNeeded == 1 ? maxCols / 2 : 0; // 1列なら中央の列を使う
        for (int c = 0; c < maxCols; c++) coverTitle[c].text = "";
        for (int c = 0; c < colsNeeded; c++)
        {
            string part = SliceChars(vt, c * coverRows, coverRows);
            if (c == colsNeeded - 1 && n > (c + 1) * coverRows) part = SliceChars(part, 0, coverRows - 1) + "";
            coverTitle[first + c].text = Verticalize(part);
        }
        if (coverAuthor != null) coverAuthor.text = Verticalize(SliceChars(va, 0, 14));
        if (spineTitle != null)
        {
            string s = n > spineRows ? SliceChars(vt, 0, spineRows - 1) + "" : vt;
            spineTitle.text = Verticalize(s);
        }
        if (spineAuthor != null) spineAuthor.text = Verticalize(SliceChars(va, 0, spineAuthorRows));
    }

    // ================= 操作 =================

    public override void OnPickupUseDown()
    {
        if (!Networking.IsOwner(gameObject)) return;
        if (bookIndex < 0) return;
        isOpen = !isOpen;
        Apply();
    }

    public void NextPage()
    {
        if (!isOpen || _pages == null || spread >= _spreadCount - 1) return;
        if (!TakeControl()) return;
        spread++;
        SaveBookmark();
        Apply();
    }

    public void PrevPage()
    {
        if (!isOpen || _pages == null || spread <= 0) return;
        if (!TakeControl()) return;
        spread--;
        SaveBookmark();
        Apply();
    }

    private bool TakeControl()
    {
        if (Networking.IsOwner(gameObject)) return true;
        if (pickup != null && pickup.IsHeld) return false; // 他の人が持っている本は操作しない
        Networking.SetOwner(Networking.LocalPlayer, gameObject);
        return true;
    }

    private void SaveBookmark()
    {
        if (library != null && bookIndex >= 0) library._SetBookmark(bookIndex, spread);
    }

    // 貸出機から呼ばれる (呼ぶ側が所有者になっていること)
    public void _AssignBook(int index, Transform at)
    {
        bookIndex = index;
        isOpen = false;
        spread = library != null ? library._GetBookmark(index) : 0;
        if (at != null)
        {
            if (objectSync != null) objectSync.FlagDiscontinuity();
            transform.SetPositionAndRotation(at.position, at.rotation);
        }
        Apply();
    }

    // 返却箱から呼ばれる (呼ぶ側が所有者になっていること)
    public void _Clear()
    {
        isOpen = false;
        spread = 0;
        if (isPooled) bookIndex = -1;
        Apply();
    }

    public void _Respawn()
    {
        _Clear();
        if (objectSync != null) objectSync.Respawn();
    }

    public void _RefreshSettings()
    {
        if (isOpen && _pages != null && _loadedIndex == bookIndex)
        {
            _shownSpread = -1;
            Render();
        }
    }

    // ================= 読み込み =================

    private void RequestLoad()
    {
        if (_requested || bookIndex < 0 || library == null) return;
        _requested = true;
        library._RequestLoad(this);
    }

    public void _StartLoad()
    {
        if (bookIndex < 0 || bookIndex >= library.urls.Length) { _requested = false; return; }
        _loadingIndex = bookIndex;
        VRCStringDownloader.LoadUrl(library.urls[bookIndex], (IUdonEventReceiver)this);
    }

    public override void OnStringLoadSuccess(IVRCStringDownload result)
    {
        _requested = false;
        if (_loadingIndex != bookIndex)
        {
            if (isOpen) RequestLoad(); // 読み込み中に作品が変わった
            return;
        }
        string s = result.Result;
        int nl = s.IndexOf('\n');
        if (nl < 0) { SetStatus("データの形式が違います"); return; }
        string[] h = s.Substring(0, nl).Split(_tabSep);
        if (h.Length < 6 || h[0] != "AOZORA1") { SetStatus("データの形式が違います"); return; }
        int cols = int.Parse(h[3]);
        if (cols > rightColumns.Length)
        {
            SetStatus("列数が合いません (データ " + cols + "列 / 本 " + rightColumns.Length + "列)");
            return;
        }
        _pages = s.Substring(nl + 1).Split(_pageSep);
        _spreadCount = (_pages.Length + 1) / 2;
        _loadedIndex = _loadingIndex;
        _shownSpread = -1;
        if (isOpen) Render();
    }

    public override void OnStringLoadError(IVRCStringDownload result)
    {
        _requested = false;
        SetStatus("読み込みに失敗しました (" + result.ErrorCode + ")\n「Allow Untrusted URLs」を確認してください");
    }

    // ================= 描画 =================

    private void Render()
    {
        if (_pages == null) return;
        int s = spread;
        if (s < 0) s = 0;
        if (s > _spreadCount - 1) s = _spreadCount - 1;
        int r = s * 2;
        bool ruby = library == null || library.showRuby;
        FillPage(rightColumns, rightRuby, r, ruby);
        FillPage(leftColumns, leftRuby, r + 1, ruby);
        if (rightPageNumber != null) rightPageNumber.text = (r > 0 && r < _pages.Length) ? r.ToString() : "";
        if (leftPageNumber != null) leftPageNumber.text = (r + 1 < _pages.Length) ? (r + 1).ToString() : "";
        SetStatus("『" + library.titles[bookIndex] + "』" + library.authors[bookIndex] + "　" + (s + 1) + " / " + _spreadCount);
        _shownSpread = spread;
    }

    private void FillPage(TextMeshPro[] columns, TextMeshPro[] rubies, int pageIndex, bool showRuby)
    {
        string[] lines = null;
        if (pageIndex < _pages.Length) lines = _pages[pageIndex].Split(_lineSep);
        int rubyCount = rubies == null ? 0 : rubies.Length;
        for (int c = 0; c < columns.Length; c++)
        {
            string body = "";
            string ruby = "";
            if (lines != null && c < lines.Length)
            {
                string line = lines[c];
                int k = line.IndexOf('\u001F');
                if (k >= 0)
                {
                    body = line.Substring(0, k);
                    if (showRuby && c < rubyCount) ruby = BuildRuby(line.Substring(k + 1));
                }
                else
                {
                    body = line;
                }
            }
            columns[c].text = Verticalize(body);
            if (c < rubyCount) rubies[c].text = ruby;
        }
    }

    // ルビの列: ルビ1文字 = 本文の半分の高さの「枠」。本文1文字に2枠が対応する。
    // 親文字の範囲の中央にそろえ、前のルビと重なる場合は後ろへずらす。
    private string BuildRuby(string spec)
    {
        string[] entries = spec.Split(_rubyEntrySep);
        string o = "";
        int cur = 0;
        int next = 0;
        for (int e = 0; e < entries.Length; e++)
        {
            string[] f = entries[e].Split(_rubyFieldSep);
            if (f.Length < 3) continue;
            int start = int.Parse(f[0]);
            int len = int.Parse(f[1]);
            string r = f[2];
            int t = start * 2 + (len * 2 - CountChars(r)) / 2;
            if (t < next) t = next;
            int n = r.Length;
            for (int i = 0; i < n; i++)
            {
                while (cur < t) { o += "\n"; cur++; }
                int ch = r[i];
                if (ch >= 0xD800 && ch <= 0xDBFF && i + 1 < n)
                {
                    o += r.Substring(i, 2);
                    i++;
                }
                else
                {
                    o += r.Substring(i, 1);
                }
                t++;
                next = t;
            }
        }
        return o;
    }

    private void ClearOpenPages()
    {
        for (int c = 0; c < rightColumns.Length; c++) rightColumns[c].text = "";
        for (int c = 0; c < leftColumns.Length; c++) leftColumns[c].text = "";
        if (rightRuby != null) for (int c = 0; c < rightRuby.Length; c++) rightRuby[c].text = "";
        if (leftRuby != null) for (int c = 0; c < leftRuby.Length; c++) leftRuby[c].text = "";
        if (rightPageNumber != null) rightPageNumber.text = "";
        if (leftPageNumber != null) leftPageNumber.text = "";
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }

    private void PlayTurn()
    {
        if (pageAudio == null || pageAudio.clip == null) return;
        float v = library != null ? library.volume : 0.2f;
        if (v <= 0f) return;
        pageAudio.volume = v;
        pageAudio.Stop();
        pageAudio.Play();
    }

    // ================= 文字列の補助 =================

    // 1文字ごとに改行 (サロゲートペアは分けない)
    private string Verticalize(string col)
    {
        int n = col.Length;
        if (n == 0) return "";
        string o = "";
        for (int i = 0; i < n; i++)
        {
            int c = col[i];
            if (i > 0) o += "\n";
            if (c >= 0xD800 && c <= 0xDBFF && i + 1 < n)
            {
                o += col.Substring(i, 2);
                i++;
            }
            else
            {
                o += col.Substring(i, 1);
            }
        }
        return o;
    }

    private int CountChars(string s)
    {
        int count = 0;
        for (int i = 0; i < s.Length; i++)
        {
            int ch = s[i];
            if (ch >= 0xDC00 && ch <= 0xDFFF) continue;
            count++;
        }
        return count;
    }

    // 文字数 (サロゲートペアを1文字と数える) で切り出す
    private string SliceChars(string s, int start, int count)
    {
        string o = "";
        int idx = 0;
        int n = s.Length;
        for (int i = 0; i < n && idx < start + count; i++)
        {
            int ch = s[i];
            int w = (ch >= 0xD800 && ch <= 0xDBFF && i + 1 < n) ? 2 : 1;
            if (idx >= start) o += s.Substring(i, w);
            i += w - 1;
            idx++;
        }
        return o;
    }
}

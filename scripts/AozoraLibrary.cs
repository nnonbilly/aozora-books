// 図書館 (UdonSharp)
// - 作品カタログ (エディタ拡張で埋め込み)
// - 貸出機: 作品を検索して選ぶと、プールから本を1冊取り出す
// - 本文の読み込み順の管理 (String Loading は5秒に1回まで)
// - 音量・ルビ表示の設定 (自分だけ)
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Components;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class AozoraLibrary : UdonSharpBehaviour
{
    [Header("貸出機のメニュー")]
    public InputField searchField;
    public TextMeshProUGUI[] resultLabels;
    public GameObject[] resultButtons;
    public TextMeshProUGUI resultInfo;
    public TextMeshProUGUI statusLabel;
    public Slider volumeSlider;
    public TextMeshProUGUI volumeLabel;
    public TextMeshProUGUI rubyButtonLabel;

    [Header("本の取り出し")]
    public VRCObjectPool pool;
    public Transform spawnPoint;
    [Tooltip("プールの本と固定の本 (設定の変更を伝えるため)")]
    public AozoraBook[] books;

    [Header("設定 (自分だけ)")]
    [Range(0f, 1f)] public float defaultVolume = 0.2f;
    [HideInInspector] public float volume = 0.2f;
    [HideInInspector] public bool showRuby = true;

    [Header("作品カタログ (Tools > Aozora Reader で自動生成)")]
    public string catalogInfo = "";
    [HideInInspector] public string[] ids;
    [HideInInspector] public string[] titles;
    [HideInInspector] public string[] authors;
    [HideInInspector] public string[] vtitles;   // 縦書き用の字形にした作品名 (表紙・背表紙用)
    [HideInInspector] public string[] vauthors;
    [HideInInspector] public VRCUrl[] urls;

    private AozoraBook[] _queue;
    private int _queueCount;
    private float _lastRequest = -100f;
    private const float RequestInterval = 5.5f;

    private int[] _bookmarks;
    private int[] _hits;
    private int _hitCount;
    private int _resultPage;
    private bool _inited;

    private void Init()
    {
        if (_inited) return;
        _inited = true;
        int n = titles == null ? 0 : titles.Length;
        _hits = new int[n];
        _bookmarks = new int[n];
        _queue = new AozoraBook[64];
        volume = defaultVolume;
    }

    void Start()
    {
        Init();
        if (volumeSlider != null) volumeSlider.value = defaultVolume;
        ApplyVolumeLabel();
        UpdateRubyButton();
        ShowAll();
        SetStatus(titles == null || titles.Length == 0
            ? "作品カタログが空です (Tools > Aozora Reader > 作品カタログを更新)"
            : "読みたい作品を選ぶと、本が出てきます");
    }

    // ================= カタログ =================

    public int FindIndex(string id)
    {
        if (ids == null) return -1;
        for (int i = 0; i < ids.Length; i++) if (ids[i] == id) return i;
        return -1;
    }

    public int _GetBookmark(int index)
    {
        Init();
        return (index >= 0 && index < _bookmarks.Length) ? _bookmarks[index] : 0;
    }

    public void _SetBookmark(int index, int spread)
    {
        Init();
        if (index >= 0 && index < _bookmarks.Length) _bookmarks[index] = spread;
    }

    // ================= 読み込み順 =================

    public void _RequestLoad(AozoraBook b)
    {
        Init();
        for (int i = 0; i < _queueCount; i++) if (_queue[i] == b) return;
        if (_queueCount < _queue.Length) _queue[_queueCount++] = b;
    }

    void Update()
    {
        if (_queueCount == 0) return;
        if (Time.time - _lastRequest < RequestInterval) return;
        AozoraBook b = _queue[0];
        for (int i = 1; i < _queueCount; i++) _queue[i - 1] = _queue[i];
        _queueCount--;
        _queue[_queueCount] = null;
        if (b == null) return;
        _lastRequest = Time.time;
        b._StartLoad();
    }

    // ================= 検索・一覧 =================

    public void Search()
    {
        Init();
        string q = searchField != null ? searchField.text.Trim() : "";
        if (q.Length == 0) { ShowAll(); return; }
        _hitCount = 0;
        for (int i = 0; i < titles.Length; i++)
        {
            if (titles[i].Contains(q) || authors[i].Contains(q)) _hits[_hitCount++] = i;
        }
        _resultPage = 0;
        RenderResults();
    }

    public void ClearSearch()
    {
        if (searchField != null) searchField.text = "";
        ShowAll();
    }

    private void ShowAll()
    {
        Init();
        _hitCount = titles == null ? 0 : titles.Length;
        for (int i = 0; i < _hitCount; i++) _hits[i] = i;
        _resultPage = 0;
        RenderResults();
    }

    public void NextResults()
    {
        if ((_resultPage + 1) * resultLabels.Length < _hitCount) { _resultPage++; RenderResults(); }
    }

    public void PrevResults()
    {
        if (_resultPage > 0) { _resultPage--; RenderResults(); }
    }

    public void NextResults10()
    {
        int per = resultLabels.Length;
        int last = _hitCount == 0 ? 0 : (_hitCount - 1) / per;
        _resultPage = Mathf.Min(last, _resultPage + 10);
        RenderResults();
    }

    public void PrevResults10()
    {
        _resultPage = Mathf.Max(0, _resultPage - 10);
        RenderResults();
    }

    private void RenderResults()
    {
        if (resultLabels == null) return;
        int per = resultLabels.Length;
        for (int k = 0; k < per; k++)
        {
            int h = _resultPage * per + k;
            bool on = h < _hitCount;
            if (resultButtons[k] != null) resultButtons[k].SetActive(on);
            if (on)
            {
                int i = _hits[h];
                resultLabels[k].text = titles[i] + "　<size=75%>" + authors[i] + "</size>";
            }
        }
        if (resultInfo != null)
        {
            int pagesTotal = _hitCount == 0 ? 1 : (_hitCount - 1) / per + 1;
            resultInfo.text = _hitCount + "件　" + (_resultPage + 1) + " / " + pagesTotal;
        }
    }

    public void Select0() { SelectResult(0); }
    public void Select1() { SelectResult(1); }
    public void Select2() { SelectResult(2); }
    public void Select3() { SelectResult(3); }
    public void Select4() { SelectResult(4); }
    public void Select5() { SelectResult(5); }
    public void Select6() { SelectResult(6); }
    public void Select7() { SelectResult(7); }
    public void Select8() { SelectResult(8); }
    public void Select9() { SelectResult(9); }

    private void SelectResult(int k)
    {
        int h = _resultPage * resultLabels.Length + k;
        if (h < _hitCount) Spawn(_hits[h]);
    }

    // ================= 貸し出し・返却 =================

    private void Spawn(int index)
    {
        if (pool == null) { SetStatus("本のプールがありません"); return; }
        VRCPlayerApi me = Networking.LocalPlayer;
        Networking.SetOwner(me, pool.gameObject);
        GameObject go = pool.TryToSpawn();
        if (go == null)
        {
            SetStatus("貸し出せる本がありません。読み終わった本を返却箱に入れてください");
            return;
        }
        Networking.SetOwner(me, go);
        AozoraBook b = go.GetComponent<AozoraBook>();
        if (b != null) b._AssignBook(index, spawnPoint);
        SetStatus("『" + titles[index] + "』を取り出しました");
    }

    // 返却箱から呼ばれる (本の所有者のみ)
    public void _ReturnBook(AozoraBook b)
    {
        if (b == null) return;
        if (b.pickup != null && b.pickup.IsHeld) b.pickup.Drop();
        if (b.isPooled && pool != null)
        {
            b._Clear();
            Networking.SetOwner(Networking.LocalPlayer, pool.gameObject);
            pool.Return(b.gameObject);
        }
        else
        {
            b._Respawn();
        }
    }

    // ================= 設定 =================

    public void OnVolumeChanged()
    {
        if (volumeSlider != null) volume = volumeSlider.value;
        ApplyVolumeLabel();
    }

    public void ToggleMute()
    {
        if (volumeSlider == null) return;
        volumeSlider.value = volumeSlider.value > 0f ? 0f : defaultVolume;
        volume = volumeSlider.value;
        ApplyVolumeLabel();
    }

    private void ApplyVolumeLabel()
    {
        if (volumeLabel != null) volumeLabel.text = volume <= 0f ? "音: なし" : "音: " + Mathf.RoundToInt(volume * 100f) + "%";
    }

    public void ToggleRuby()
    {
        showRuby = !showRuby;
        UpdateRubyButton();
        if (books == null) return;
        for (int i = 0; i < books.Length; i++)
        {
            if (books[i] != null && books[i].gameObject.activeInHierarchy) books[i]._RefreshSettings();
        }
    }

    private void UpdateRubyButton()
    {
        if (rubyButtonLabel != null) rubyButtonLabel.text = showRuby ? "ルビ:あり" : "ルビ:なし";
    }

    private void SetStatus(string msg)
    {
        if (statusLabel != null) statusLabel.text = msg;
    }
}

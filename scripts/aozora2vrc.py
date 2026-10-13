#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
aozora2vrc.py - 青空文庫テキストを VRChat 縦書き見開きギミック用データに変換する

使い方:
  python aozora2vrc.py --work-id 789 1567 --out docs/books      # 作品IDを指定してネットから取得
  python aozora2vrc.py 789_ruby_5639.zip --out docs/books       # ローカルのzip
  python aozora2vrc.py wagahai.txt --id wagahai --out docs/books --cols 16 --rows 18

出力 (--out の下):
  <id>.txt        作品データ (下記フォーマット)
  index.txt       作品一覧 (id<TAB>title<TAB>author<TAB>pages)
  used_chars.txt  全作品で使われている文字の一覧 (TMP Font Asset 作成用)

作品データのフォーマット:
  1行目: AOZORA1<TAB>title<TAB>author<TAB>cols<TAB>rows<TAB>pages
  2行目以降: 各ページを '¶' で区切って並べたもの。
    1ページ = cols 行。1行 = 縦書き1列分の文字 (rows 文字以下)。
    1行目が一番右の列 (右から左へ読む順)。
  ページ0が右、ページ1が左 … と、2ページ1組で見開きになる (右開き)。
"""
import argparse
import csv
import io
import re
import sys
import urllib.request
import unicodedata
import zipfile
from pathlib import Path

PAGE_SEP = "¶"
BREAK = "\f"  # 内部用の改ページマーカー

# ---------- 禁則 ----------
NO_START = set("、。，．・：；？！‥…ー）」』】〕〉》｝］ゝゞヽヾ々ぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ〻")
NO_END = set("（「『【〔〈《｛［")

# ---------- 縦書き字形への置換 (フォントに該当グリフが必要) ----------
VERT_MAP = {
    "、": "︑", "。": "︒",
    "「": "﹁", "」": "﹂", "『": "﹃", "』": "﹄",
    "（": "︵", "）": "︶", "｛": "︷", "｝": "︸",
    "〔": "︹", "〕": "︺", "【": "︻", "】": "︼",
    "《": "︽", "》": "︾", "〈": "︿", "〉": "﹀",
    "［": "﹇", "］": "﹈",
    "ー": "︱", "―": "︱", "─": "︱",
    "…": "︙", "‥": "︰",
}


BASE = "https://www.aozora.gr.jp/"
LIST_URL = BASE + "index_pages/list_person_all_extended_utf8.zip"


def http_get(url: str) -> bytes:
    req = urllib.request.Request(url, headers={"User-Agent": "aozora2vrc"})
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return r.read()
    except Exception as e:
        raise SystemExit(f"取得に失敗しました: {url}\n  {e}")


def fetch_work(work_id: str, cache: Path) -> Path:
    """作品ID(例: 789)から青空文庫公式サイトからzipを取得し、キャッシュに保存。"""
    cache.mkdir(parents=True, exist_ok=True)
    wid = work_id.zfill(6)
    list_zip = cache / "list_person_all_extended_utf8.zip"
    if not list_zip.exists():
        print("作品リストを取得中...")
        list_zip.write_bytes(http_get(LIST_URL))
    with zipfile.ZipFile(list_zip) as z:
        rows = list(csv.DictReader(io.StringIO(z.read(z.namelist()[0]).decode("utf-8-sig"))))
    for r in rows:
        if r["作品ID"] == wid:
            url = r["テキストファイルURL"]
            if not url.endswith(".zip"):
                raise SystemExit(f"作品ID {wid}: テキスト(zip)がありません ({url!r})")
            dest = cache / url.rsplit("/", 1)[1]
            if not dest.exists():
                print(f"ダウンロード: {url}")
                dest.write_bytes(http_get(url))
            return dest
    raise SystemExit(f"作品ID {wid} が見つかりません")


def read_aozora(path: Path) -> str:
    if path.suffix.lower() == ".zip":
        with zipfile.ZipFile(path) as z:
            names = [n for n in z.namelist() if n.lower().endswith(".txt")]
            if not names:
                raise ValueError(f"{path}: zip内にtxtがありません")
            raw = z.read(names[0])
    else:
        raw = path.read_bytes()
    for enc in ("utf-8-sig", "cp932"):
        try:
            return raw.decode(enc)
        except UnicodeDecodeError:
            pass
    return raw.decode("cp932", errors="replace")


def jis_to_char(plane: int, row: int, cell: int) -> str:
    try:
        if plane == 1:
            return bytes([0xA0 + row, 0xA0 + cell]).decode("euc_jisx0213")
        if plane == 2:
            return bytes([0x8F, 0xA0 + row, 0xA0 + cell]).decode("euc_jisx0213")
    except Exception:
        pass
    return "〓"


GAIJI_RE = re.compile(r"※［＃([^］]*)］")


def gaiji_sub(m: re.Match) -> str:
    note = m.group(1)
    u = re.search(r"U\+([0-9A-Fa-f]{4,6})", note)
    if u:
        try:
            return chr(int(u.group(1), 16))
        except ValueError:
            return "〓"
    j = re.search(r"(\d)-(\d{1,2})-(\d{1,2})", note)
    if j:
        return jis_to_char(int(j.group(1)), int(j.group(2)), int(j.group(3)))
    return "〓"


def split_sections(text: str):
    """(title, author, body_lines, colophon_lines)"""
    lines = text.replace("\r\n", "\n").replace("\r", "\n").split("\n")
    title = lines[0].strip() if lines else ""
    author = lines[1].strip() if len(lines) > 1 else ""
    # 記号説明ブロックを除去
    bar = [i for i, l in enumerate(lines) if l.startswith("-------------------------------------------------------")]
    start = 2
    if len(bar) >= 2:
        start = bar[1] + 1
    # 底本以降は奥付
    end = len(lines)
    colo = []
    idx = [i for i, l in enumerate(lines) if l.startswith("底本：")]
    if idx:
        end = idx[-1]
        colo = [l for l in lines[end:]]
    body = lines[start:end]
    return title, author, body, colo


def clean_body(lines):
    """ルビ・注記を処理し、段落(文字列)のリストを返す。BREAK は改ページ。"""
    paras = []
    block_indent = 0
    for line in lines:
        s = line
        s = GAIJI_RE.sub(gaiji_sub, s)
        s = s.replace("｜", "")
        s = re.sub(r"《[^》]*》", "", s)  # ルビは後回し: 今は削除

        # 改ページ系
        if re.search(r"［＃(改ページ|改丁|改見開き|改段)］", s):
            paras.append(BREAK)
            s = re.sub(r"［＃(改ページ|改丁|改見開き|改段)］", "", s)
            if not s.strip():
                continue

        # 字下げ
        m_blk = re.search(r"［＃ここから([０-９0-9]+)字下げ[^］]*］", s)
        if m_blk:
            block_indent = int(unicodedata.normalize("NFKC", m_blk.group(1)))
            s = s.replace(m_blk.group(0), "")
        if re.search(r"［＃ここで字下げ終わり］", s):
            block_indent = 0
            s = s.replace("［＃ここで字下げ終わり］", "")
        indent = block_indent
        m_ln = re.match(r"［＃([０-９0-9]+)字下げ］", s)
        if m_ln:
            indent = int(unicodedata.normalize("NFKC", m_ln.group(1)))
            s = s[m_ln.end():]

        # 残りの注記は全部削除
        s = re.sub(r"［＃[^］]*］", "", s)
        s = s.replace(PAGE_SEP, "").replace("\t", "　")
        # 半角ASCII → 全角 (縦書きで横倒しにならないように)
        s = "".join(
            "　" if c == " " else (chr(ord(c) + 0xFEE0) if "!" <= c <= "~" else c)
            for c in s
        )
        if s.strip("　") == "" :
            if paras and paras[-1] != "" and paras[-1] != BREAK:
                paras.append("")  # 空行は空の列1つ
            continue
        if indent and not s.startswith("　"):
            s = "　" * indent + s
        paras.append(s)
    return paras


def wrap(par: str, rows: int):
    """1段落を rows 文字以内の列に分割 (禁則: 追い出し)。"""
    if par == "":
        return [""]
    cols = []
    s = par
    while s:
        if len(s) <= rows:
            cols.append(s)
            break
        n = rows
        while n > 1 and (s[n] in NO_START or s[n - 1] in NO_END or (s[n] == s[n - 1] and s[n] in "…―")):
            n -= 1
        cols.append(s[:n])
        s = s[n:]
    return cols


def paginate(paras, cols_per_page: int, rows: int):
    pages = []
    cur = []

    def flush():
        nonlocal cur
        if cur:
            pages.append(cur + [""] * (cols_per_page - len(cur)))
            cur = []

    for p in paras:
        if p == BREAK:
            flush()
            continue
        for c in wrap(p, rows):
            if not cur and c == "":
                continue  # ページ先頭の空列は捨てる
            cur.append(c)
            if len(cur) == cols_per_page:
                flush()
    flush()
    return pages


def title_page(title: str, author: str, cols: int, rows: int):
    page = [""] * cols
    t = wrap(title, rows)[:3]
    # 右から: 余白 / タイトル / 余白 / 著者
    pos = 3
    for c in t:
        if pos < cols:
            page[pos] = c
            pos += 1
    pos += 2
    if pos < cols:
        page[pos] = "　" * max(0, rows - len(author) - 2) + author[:rows]
    return page


def to_vertical(s: str, enable: bool) -> str:
    return "".join(VERT_MAP.get(c, c) for c in s) if enable else s


def render_book(text: str, cols: int, rows: int, cover: bool = True, vertical_glyphs: bool = True):
    """青空文庫テキスト → (title, author, pages, 出力文字列)"""
    title, author, body, colo = split_sections(text)
    paras = clean_body(body)
    pages = paginate(paras, cols, rows)

    all_pages = []
    if cover:
        all_pages.append(title_page(title, author, cols, rows))
    all_pages += pages

    # 奥付 (底本・入力者情報) は新しいページから
    if colo:
        colo_paras = ["【底本・入力情報】"] + [l for l in colo if l.strip()]
        colo_paras = [
            "".join("　" if c == " " else (chr(ord(c) + 0xFEE0) if "!" <= c <= "~" else c) for c in re.sub(r"［＃[^］]*］", "", GAIJI_RE.sub(gaiji_sub, p)))
            for p in colo_paras
        ]
        all_pages += paginate(colo_paras, cols, rows)

    if len(all_pages) % 2 == 1:
        all_pages.append([""] * cols)

    title = title.replace("\t", " ")
    author = author.replace("\t", " ")
    header = "\t".join(["AOZORA1", title, author, str(cols), str(rows), str(len(all_pages))])
    body_txt = PAGE_SEP.join(
        "\n".join(to_vertical(c, vertical_glyphs) for c in pg) for pg in all_pages
    )
    return title, author, len(all_pages), header + "\n" + body_txt


def convert(path: Path, args, out: Path):
    title, author, npages, content = render_book(
        read_aozora(path), args.cols, args.rows, not args.no_cover, not args.no_vertical_glyphs)
    all_pages = range(npages)
    book_id = args.id if (args.id and len(args.inputs) == 1) else path.stem
    (out / f"{book_id}.txt").write_text(content, encoding="utf-8", newline="\n")
    print(f"{path.name}: 「{title}」 {author}  {len(all_pages)}ページ ({len(all_pages)//2}見開き)  -> {book_id}.txt")
    return book_id, title, author, len(all_pages)


def update_index(out: Path, entries):
    idx_path = out / "index.txt"
    rows = {}
    if idx_path.exists():
        for l in idx_path.read_text(encoding="utf-8").splitlines():
            p = l.split("\t")
            if len(p) == 4:
                rows[p[0]] = p
    for e in entries:
        rows[e[0]] = [e[0], e[1], e[2], str(e[3])]
    idx_path.write_text("\n".join("\t".join(r) for r in rows.values()) + "\n", encoding="utf-8", newline="\n")


def write_used_chars(out: Path):
    chars = set()
    for f in out.glob("*.txt"):
        if f.name in ("index.txt", "used_chars.txt"):
            continue
        chars |= set(f.read_text(encoding="utf-8"))
    chars -= set("\n\r\t" + PAGE_SEP)
    (out / "used_chars.txt").write_text("".join(sorted(chars)), encoding="utf-8", newline="\n")
    print(f"used_chars.txt: {len(chars)} 文字")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("inputs", nargs="*", help="青空文庫の txt または zip")
    ap.add_argument("--work-id", nargs="+", default=[], help="作品ID(例: 789 = 吾輩は猫である)。ネットから取得する")
    ap.add_argument("--cache", default="aozora_cache", help="ダウンロード保存先")
    ap.add_argument("--out", default="books", help="出力先ディレクトリ")
    ap.add_argument("--cols", type=int, default=16, help="1ページの列数 (既定16)")
    ap.add_argument("--rows", type=int, default=18, help="1列の文字数 (既定18)")
    ap.add_argument("--id", help="作品ID (入力が1つのときのみ)")
    ap.add_argument("--no-cover", action="store_true", help="扉ページを付けない")
    ap.add_argument("--no-vertical-glyphs", action="store_true",
                    help="縦書き用字形(︑︒﹁﹂等)への置換をしない")
    args = ap.parse_args()

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    paths = [Path(p) for p in args.inputs] + [fetch_work(w, Path(args.cache)) for w in args.work_id]
    if not paths:
        ap.error("入力ファイルか --work-id を指定してください")
    args.inputs = paths
    entries = [convert(p, args, out) for p in paths]
    update_index(out, entries)
    write_used_chars(out)


if __name__ == "__main__":
    sys.exit(main())

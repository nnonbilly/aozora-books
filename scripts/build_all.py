#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
build_all.py - aozorabunko_text (作品/<著者>/<作品>.txt) を一括で縦書き見開き用データに変換する

  python scripts/build_all.py --src ../aozorabunko_text --meta list_person_all_extended_utf8.csv --out books

出力 (--out の下):
  <id先頭2文字>/<id>.txt  作品データ (id は元ファイルのパスから作る10桁の英数字。更新しても変わらない)
  index.txt               作品一覧: id<TAB>著者<TAB>作品名<TAB>ページ数   (著者・作品名順)
  used_chars.txt          全作品で使われている文字 (TMP Font Asset 用)
  excluded.txt            著作権ありとして除外した作品
"""
import argparse
import csv
import hashlib
import shutil
import sys
from collections import defaultdict
from multiprocessing import Pool
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import aozora2vrc as A  # noqa: E402

FULLWIDTH = str.maketrans({"/": "／", "\\": "＼", ":": "：", "*": "＊", "?": "？",
                           '"': "”", "<": "＜", ">": "＞", "|": "｜"})


def sanitize(s: str) -> str:
    """aozorabunko_text (Go) のファイル名規則と同じ変換"""
    s = s.translate(FULLWIDTH)
    s = "".join(c for c in s if not (ord(c) < 0x20 or ord(c) == 0x7F))
    s = s.strip().strip(". 　").strip()
    return s[:120]


def load_copyrighted(meta: Path):
    """著作権ありの (著者, 作品名) の集合。作品・人物どちらかにフラグがあれば対象。"""
    works = defaultdict(list)
    flagged = set()
    with meta.open(encoding="utf-8-sig", newline="") as f:
        for r in csv.DictReader(f):
            wid = r.get("作品ID", "").strip()
            name = sanitize(r.get("姓", "").strip() + r.get("名", "").strip())
            title = sanitize(r.get("作品名", "").strip())
            works[wid].append((name, title))
            if r.get("作品著作権フラグ", "").strip() == "あり" or r.get("人物著作権フラグ", "").strip() == "あり":
                flagged.add(wid)
    # 翻訳者だけ著作権ありの場合などに備え、その作品に関わる全員の名前で除外キーを作る
    keys = set()
    for wid in flagged:
        for name, title in works[wid]:
            keys.add((name, title))
    return keys


def title_candidates(stem: str):
    yield stem
    # 同名作品の区別用「作品名（新字新仮名）」などの括弧を外したもの
    if stem.endswith("）") and "（" in stem:
        yield stem[: stem.rindex("（")]


def book_id(rel: str) -> str:
    return hashlib.sha1(rel.encode("utf-8")).hexdigest()[:10]


def work(job):
    src, rel, out, cols, rows = job
    try:
        title, author, pages, content = A.render_book(A.read_aozora(src / rel), cols, rows)
    except Exception as e:  # 1作品の失敗で全体を止めない
        return rel, None, repr(e)
    bid = book_id(rel)
    d = out / bid[:2]
    d.mkdir(parents=True, exist_ok=True)
    (d / f"{bid}.txt").write_text(content, encoding="utf-8", newline="\n")
    p = Path(rel)
    return rel, (bid, p.parent.name, p.stem, pages, set(content)), None


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--src", required=True, help="aozorabunko_text のフォルダ")
    ap.add_argument("--meta", help="list_person_all_extended_utf8.csv (著作権ありの作品を除外するため)")
    ap.add_argument("--out", default="books")
    ap.add_argument("--cols", type=int, default=16)
    ap.add_argument("--rows", type=int, default=18)
    ap.add_argument("--include-copyrighted", action="store_true", help="著作権ありの作品も含める")
    args = ap.parse_args()

    src = Path(args.src)
    out = Path(args.out)
    files = sorted((src / "作品").rglob("*.txt"))
    if not files:
        sys.exit(f"{src / '作品'} に作品がありません")

    keys = set()
    if args.meta and not args.include_copyrighted:
        keys = load_copyrighted(Path(args.meta))
        print(f"著作権ありの (著者, 作品名): {len(keys)} 件")
    elif not args.include_copyrighted:
        print("警告: --meta が無いので著作権ありの作品を除外できません", file=sys.stderr)

    excluded, jobs = [], []
    for f in files:
        rel = f.relative_to(src).as_posix()
        author = f.parent.name
        if any((author, t) in keys for t in title_candidates(f.stem)):
            excluded.append(rel)
            continue
        jobs.append((src, rel, out, args.cols, args.rows))

    # 出力を作り直す (消えた作品のファイルを残さないため)。git が差分だけを記録する。
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)

    with Pool() as pool:
        results = pool.map(work, jobs, chunksize=50)

    index, chars, failed = [], set(), []
    for rel, info, err in results:
        if err:
            failed.append(f"{rel}\t{err}")
            continue
        bid, author, title, pages, cs = info
        index.append((author, title, bid, pages))
        chars |= cs
    index.sort()

    (out / "index.txt").write_text(
        "".join(f"{bid}\t{a}\t{t}\t{p}\n" for a, t, bid, p in index), encoding="utf-8", newline="\n")
    chars -= set("\n\r\t" + A.PAGE_SEP)
    for a, t, _, _ in index:
        chars |= set(a + t)
    (out / "used_chars.txt").write_text("".join(sorted(chars)), encoding="utf-8", newline="\n")
    (out / "excluded.txt").write_text("".join(r + "\n" for r in excluded), encoding="utf-8", newline="\n")
    if failed:
        (out / "failed.txt").write_text("\n".join(failed) + "\n", encoding="utf-8", newline="\n")

    print(f"変換 {len(index)} 件 / 除外 {len(excluded)} 件 / 失敗 {len(failed)} 件 / 使用文字 {len(chars)} 字")


if __name__ == "__main__":
    main()

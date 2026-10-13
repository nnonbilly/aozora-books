# aozora-books

VRChat の縦書き見開きリーダー用に、青空文庫の作品を変換したデータです。

- 元データ: [nnonbilly/aozorabunko_text](https://github.com/nnonbilly/aozorabunko_text)（[P4suta/aozorabunko_text](https://github.com/P4suta/aozorabunko_text) のフォーク）
- 出典: [青空文庫](https://www.aozora.gr.jp/)。各作品の最後のページに、底本と入力・校正者の情報が入っています。
- 著作権が存続している作品は、青空文庫の作品リストの著作権フラグに基づいて除外しています（`books/excluded.txt`）。

## 構成

| パス | 内容 |
|---|---|
| `books/index.txt` | 作品一覧（`id<TAB>著者<TAB>作品名<TAB>ページ数`） |
| `books/<idの先頭2文字>/<id>.txt` | 作品データ |
| `books/used_chars.txt` | 使われている全文字（フォント作成用） |
| `scripts/` | 変換スクリプト |
| `.github/workflows/build.yml` | 毎日の自動変換 |

## 作品データの形式

- 1行目: `AOZORA1<TAB>作品名<TAB>著者<TAB>列数<TAB>1列の文字数<TAB>ページ数`
- 2行目以降: ページを `¶` で区切って並べたもの。1ページは「列数」行で、1行が縦書き1列分です。先頭の行が一番右の列になります。
- ページ0が右、ページ1が左というように、2ページで1見開きです（右開き）。

## 読み込みURL

```
https://raw.githubusercontent.com/nnonbilly/aozora-books/main/books/index.txt
https://raw.githubusercontent.com/nnonbilly/aozora-books/main/books/<idの先頭2文字>/<id>.txt
```

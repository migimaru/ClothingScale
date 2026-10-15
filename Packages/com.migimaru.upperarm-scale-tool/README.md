# Clothing Scale

衣装全体とボーンのScaleを、プロファイルでまとめて設定するUnity Editorツールです。
複数の衣装に同じ設定を適用でき、アバターやPrefabを置き直した場合も保存した設定を使えます。

## 主な機能

- プロファイルの追加・名前変更・削除・自動保存
- 衣装全体とボーンごとのXYZ設定
- XYZの比率を保つ連動ボタン
- 左右のボーンをセットで設定
- 複数衣装・非表示の衣装への一括適用
- ボーン名の表記違いに対応（例：`UpperArm.L`と`UpperArm_L`）
- Undo / Redo、Prefab Overrideに対応

## 必要環境

- Unity 2022.3

Modular Avatarは必須ではありません。Setup Outfit前の衣装も読み込めます。

## インストール

### VCCから導入

1. [migimaru VPMをVCCに追加](https://migimaru.github.io/vpm/add/)します。
2. VCCで対象プロジェクトの`Manage Project`を開きます。
3. `Avatar Clothing Scale Tool`を追加します。

手動でリポジトリを追加する場合のRepository URL：

`https://migimaru.github.io/vpm/index.json`

### UnityPackageから導入

[GitHub Releases](https://github.com/migimaru/ClothingScale/releases/latest)から`.unitypackage`をダウンロードし、
Unityの`Assets → Import Package → Custom Package...`からImportします。

## 使い方

### 1. プロファイルを作成

`Tools → Clothing Scale`を開き、「追加」でプロファイルを作成します。
「名前変更」で名前を変更でき、作成したプロファイルはプルダウンから切り替えられます。

### 2. Scaleを設定

「＋ ボーンを追加」からアバターを指定し、調整するボーンを選びます。
衣装全体を変更したい場合は「衣装全体」を追加します。

各行でXYZを設定します。左側の鎖ボタンをオンにすると、XYZの比率を保って連動します。
「左右セット」をオンにすると、左右に同じ値を適用します。

### 3. 衣装を読み込んで適用

Hierarchyで衣装のルートを選び、「選択中の衣装を読み込む」を押します。
複数の衣装を選択することもできます。

現在値と適用値を確認し、変更する項目にチェックして適用します。
結果を残すにはシーンを保存してください。適用はUndo / Redoで戻せます。

## プロファイルとScale

プロファイルはプロジェクト内に自動保存されます。
作成元のアバターやPrefabに固定されないため、対応するボーン構造であれば同じ設定を使えます。

Scaleは指定した値への変更です。繰り返し適用しても倍率は累積しません。
衣装全体とボーンのScaleは別々に設定できます。
全体の倍率が異なる衣装では、プロファイルを分けてください。

プロファイルを削除しても、衣装に適用済みのScaleは元に戻りません。

## 対応できない場合

ボーンの対応が曖昧な場合や、左右セットの片方が見つからない場合は未適用になります。
アーマチュア名の違いには対応しますが、任意のボーン名やすべての階層構造に対応するものではありません。

MA Merge Armatureの双方向固定が有効な対象は、素体への同期を避けるため適用できません。
衣装のフィットや貫通、ウェイト、BlendShapeは適用後に見た目で確認してください。

## ライセンス

[MIT License](LICENSE)

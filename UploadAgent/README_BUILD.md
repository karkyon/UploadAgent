# UploadAgent v1.1.0 ビルド・配布手順

## 変更点 (v1.0.0 → v1.1.0)

| 機能 | 内容 |
|------|------|
| カスタムアイコン | `.ico` ファイル不要（GDI+で動的生成）。カスタム指定も可 |
| アイコン色変化 | 正常=緑、エラー=赤でトレイアイコンが自動変化 |
| 設定画面 | トレイ右クリック「設定...」またはダブルクリックで開く |
| ログビューア | 設定画面内にリアルタイムログ表示（色分け付き） |
| 統計カウンタ | 本日の移動件数・エラー件数を表示（日付変わりで自動リセット） |
| MachCoreサーバ疎通確認 | 設定画面のステータスタブからワンクリックで確認 |
| Trash管理 | 全ドライブのTrashサイズ表示・ファイル一覧・一括クリア |
| 自動起動ON/OFF | 設定画面から切り替え可能（レジストリを自動更新） |
| ポート変更 | 設定画面から変更可能（保存後サーバ自動再起動） |
| appsettings.json | `%APPDATA%\MachCore\UploadAgent\appsettings.json` に設定永続化 |

## ビルド環境

| 項目 | 要件 |
|------|------|
| IDE | Visual Studio 2017以降 |
| .NET Framework | 4.7.2（Windows 7 SP1以降に標準搭載） |

## ビルド手順

1. `UploadAgent.csproj` をVisual Studioで開く
2. 構成を `Release / Any CPU` に設定
3. `Ctrl+Shift+B` でビルド
4. `bin\Release\UploadAgent.exe` が生成される（単体配布可能）

> ⚠️ **app.ico は不要です** - v1.1.0ではアイコンをGDI+で動的生成するため `.ico` ファイルは不要

## カスタムアイコンの設定方法

1. UploadAgent を起動
2. トレイアイコンを右クリック → **設定...**
3. 「設定」タブ → 「カスタムアイコン」の「参照...」ボタン
4. `.ico` ファイルを選択して「保存して再起動」

## 設定ファイル

```
%APPDATA%\MachCore\UploadAgent\
├── appsettings.json    ← 設定（ポート・URL・自動起動・アイコンパスなど）
├── agent.log           ← 操作ログ（10MBローテーション）
└── agent_error.log     ← エラーログ
```

### appsettings.json の内容

```json
{
  "port": 57300,
  "machCoreServerUrl": "https://192.168.1.11:8443",
  "autoStart": true,
  "showBalloonNotify": true,
  "verboseLog": false,
  "customIconPath": ""
}
```

## トレイメニュー機能一覧

| メニュー項目 | 機能 |
|------------|------|
| ダブルクリック | 設定画面を開く |
| ⚙ 設定... | 設定画面（設定・ステータス・ログ・Trash管理） |
| 📋 ログフォルダを開く | Explorerでログフォルダを開く |
| 📊 統計情報を表示 | 本日の処理件数をダイアログ表示 |
| 終了 | 確認後にAgentを終了 |

## アンインストール

1. トレイアイコン右クリック → 終了
2. `UploadAgent.exe` を削除
3. `%APPDATA%\MachCore\UploadAgent\` を削除（任意）
4. レジストリ `HKCU\...\Run\MachCoreUploadAgent` は設定画面で「自動起動OFF」にすれば自動削除される

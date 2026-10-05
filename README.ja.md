# Big Walk 三人称視点 Mod

一人称視点のカメラを、キャラクターの後ろから見る三人称視点に切り替える
BepInEx プラグインです。酔い対策として、視点の安定した追従カメラにします。

## 構成（すべて導入済み）

- `F:\SteamLibrary\steamapps\common\Big Walk\` に BepInEx 6 (IL2CPP版, build #788)
  を追加導入（`BepInEx/`, `dotnet/`, `winhttp.dll`, `doorstop_config.ini`）
- 本プラグイン: `BepInEx\plugins\BigWalkThirdPerson.dll`
- ソースコード: `Documents\Codex\BigWalk\ThirdPersonMod\`

## 使い方

ゲーム内（ワールドに入ってから）:

| 操作 | キー |
| --- | --- |
| 三人称 ON/OFF | `F4` |
| ズームイン/アウト | マウスホイール |
| デバッグ情報をログに出力 | `F8` |

- 画面左下に小さく `3rd person [F4]` と表示されている間は三人称が有効です。
- カメラは肩越しオフセット付きでキャラを映します。壁などが挟まる場合は
  自動で手前に寄ります。
- `StartEnabled = true`（初期値）なので、ワールドに入れば自動で三人称になります。

## 設定

`BepInEx\config\dev.sopur.bigwalk.thirdperson.cfg`（ゲームを一度起動すると生成）:

- `Camera.Mode` … `Direct`（初期値・推奨。プレイヤーカメラはリグ
  （CameraUprighter/CameraPivot）の子なので、ワールド位置を描画直前に
  上書きするだけで視点回転・しゃがみ・頭ボブがそのまま効く）/
  `Guide`（実験的。ゲームの cameraGuide 機構を流用）
- `Camera.Distance` … 初期距離 3.2m（`MinDistance`〜`MaxDistance` でホイール範囲）
- `Camera.Height` / `Camera.Shoulder` … ピボットの高さ・肩越しオフセット
- `Camera.Collision` / `CollisionRadius` … カメラの壁衝突回避
- `Camera.Stabilize` / `StabilizeAmount` / `StabilizeSmoothTime` …
  カメラピボットを平滑化して頭ボブを除去（酔い対策の主機能、初期値ON）
- `Camera.LevelHorizon` … `true` でカメラのロールを除去し常に水平維持
- `Body.ShowBody` … 自分の全身（remote用ボディ）を表示
- `Body.HideHead` … 頭が視界を遮る場合に `true`
- `UI.ShowStatus` … 左下のステータス表示
- `Camera.ToggleKey` / `DumpKey` … キー変更（`F4`, `V`, `Keypad0` 等の KeyCode 名）

## 動作の仕組み

- ローカルプレイヤー = Mirror の `isLocalPlayer` で特定（フォールバック:
  `cameraTransform` を持つ個体）。
- `PlayerCamera` は `CameraPivot` リグの子。Direct モードでは毎フレーム
  （LateUpdate + 描画直前の OnPreRender）カメラのワールド位置を
  「ピボット位置 − 視線方向 × 距離 + 高さ/肩オフセット」に上書きします。
  回転は触らないので、マウス視点・しゃがみ・頭ボブは純正のまま効きます。
- 衝突判定は起動時に使用可能な物理クエリ（SphereCast→Raycast）を probing
  して使用。IL2CPPでストリップ済みのAPIは使わず、プレイヤー自身の
  コライダー層は除外します。
- 身体は `PlayerLooks.SetBodyToRemoteMode()` + `SetHideLocalTorso(false)` で
  リモート用の全身モデルを表示。OFFに戻すと一人称用の表示に復元します。

## アンインストール

ゲームフォルダ `F:\SteamLibrary\steamapps\common\Big Walk\` から以下を削除:

- `BepInEx\plugins\BigWalkThirdPerson.dll`（Modだけ外す場合はこれだけ）
- 完全に戻す場合はさらに `BepInEx\`, `dotnet\`, `winhttp.dll`, `doorstop_config.ini`

いずれも追加ファイルのみで、ゲーム本体のファイルは変更していません。
Steam の「ファイルの整合性を確認」でも復旧できます。

## 既知の制約・注意

- マルチプレイ協力ゲームです。カメラ変更はクライアント側の見た目のみで、
  他プレイヤーには影響しませんが、利用は自己責任でお願いします。
- カットシーン・特別なカメラ演出中はガイド差し替えを検出して一時停止しますが、
  演出中に違和感があれば `F4` で一時的に OFF にしてください。
- エイムが必要な場面（アイテム狙い等）では、照準とカメラ位置のズレが出る場合があります。

## トラブルシューティング

症状の確認は `BepInEx\LogOutput.log` と `F8` ダンプで行います。

- **カメラが動かない** → 設定の `Camera.Mode` を `Direct` に変更
- **自分の身体が見えない** → `Body.ShowBody` が `true` か確認。
  それでもダメなら `F8` のダンプを開発者に送ってください
- **頭で視界が塞がれる** → `Body.HideHead = true`、または `Shoulder` を大きく
- **キーが効かない** → ログに `Legacy UnityEngine.Input is unavailable` が
  出ていないか確認（出ていたら報告してください。代替入力に対応します）

## ビルド

```
cd ThirdPersonMod
dotnet build -c Release
cp bin/Release/net6.0/BigWalkThirdPerson.dll "<game>\BepInEx\plugins\"
```

<div align="center">

# ♡ Lilith AI MOD ♡

### 讓莉莉絲不只待在桌面，也能真正聽見你、記得你、回應你。
### Let Lilith hear you, remember you, and answer in her own voice.

🍓 **Unofficial Community MOD · 非官方社群 MOD** 🍓

[繁體中文說明](README_繁體中文.md) · [简体中文说明](README_简体中文.md) · [日本語ガイド](README_日本語.md) · [English Guide](README_EN.md)

- 上游社区 / Upstream community: **[Discord](https://discord.gg/b3eJJhEfAC)**（原作者米米的服务器，不是本分支的支持渠道 / MIMI's server, not support for this fork）

</div>

---

## ✦ 歡迎回來 / Welcome back

這是一款為《The NOexistenceN of Lilith》桌寵製作的非官方 AI 擴充。它保留遊戲原本安靜、帶點哲學氣息的莉莉絲，並讓她能透過文字或麥克風與你自然聊天。

This unofficial AI extension for *The NOexistenceN of Lilith* keeps Lilith's quiet, philosophical charm while letting you talk to her naturally by text or microphone.

> 「今天也要和我說說話嗎？」  
> “Will you stay and talk with me today?”

## ✦ 0.2.2 / This fork

本仓库是 [mimimi6666/Lilith-AI-Mod](https://github.com/mimimi6666/Lilith-AI-Mod) 的个人维护分支，可安装版本为 **0.2.2**。上游 `main` 对着当前游戏无法独立编译；本分支可以。本分支沿用上游条款，不另授许可。补充台词语音包和动态语音运行时仍从原作者的 Release 下载，这里不重新发布那些语音内容。

This repository is a personal fork of [mimimi6666/Lilith-AI-Mod](https://github.com/mimimi6666/Lilith-AI-Mod). The installable release is **0.2.2**. Upstream `main` does not build against the current game; this branch does. It keeps the upstream terms and grants no extra license. The supplemental voice pack and the dynamic voice runtime are still downloaded from the original author's releases and are not republished here.

- 0.2.2：新建聊天或语音连接不再填写 Provider ID，列表显示名称和协议；聊天协议保存在连接上。已有配置不用重新填写密钥 / 0.2.2 no longer asks for a Provider ID on a new chat or speech connection. The list shows the name and protocol, and the chat protocol is stored on the connection. Existing configs keep their keys.
- 游戏设置左侧新增「AI」分页，聊天与语音识别分开配置 / The in-game settings gain an **AI** page. Chat and speech recognition are configured separately.
- 适配当前游戏的设置界面 / The settings UI matches the current game build.
- 补充台词语音默认不安装；只有勾选安装时才会打开补充语音 / The supplemental voice pack is off unless you choose to install it.
- 文字会等语音就绪后再出现，最多等 7 秒；F7 输入框可查看最近对话 / Replies wait for the generated voice for up to 7 seconds, and the F7 field can show recent turns.
- 官方 RC4 安装包里的「Codex 工作状态桥接」勾选没有放进本分支安装器。已经勾过的 Hooks 会保留 / The optional Codex work-status bridge from the official RC4 installer is not included. Hooks already installed are left in place.

## ✦ 上游 0.1.1-RC4 里已经包含的改动 / Already in upstream 0.1.1-RC4

- 啟動階段改為分項隔離；單一非必要 Hook 不相容時，其餘 MOD 功能仍可繼續載入，錯誤會寫入 `BepInEx/LogOutput.log` / Startup stages are isolated so one incompatible optional hook no longer prevents the remaining MOD features from loading
- 安裝器會重試暫時性的檔案鎖定與覆寫失敗，顯示實際問題路徑，並在解壓後驗證 BepInEx 與 MOD DLL / The installer retries transient file locks, reports the exact failing path, and verifies the core installation after extraction
- 修復重啟或開啟設定後語音按鈕與實際語音不同步，以及切到日文後無法切回中文的問題；中文／日文選擇現在會正確保存 / Fixed voice-button desynchronization after restart and being unable to switch back to Chinese after selecting Japanese; both language choices now persist correctly
- 本機語音主機只啟動目前選擇的語言服務，切換中文／日文時會乾淨重啟；相容官方 Build `24275097` 的語音切換回呼 / The local voice host starts only the selected language service, restarts cleanly on language changes, and supports the voice callback in official Build `24275097`
- 千問即時語音辨識會使用設定的即時模型，HTTP 備援仍使用一般 ASR 模型 / Qwen realtime speech recognition now uses the configured realtime model while HTTP fallback keeps the regular ASR model
- 已知舊版亂碼預設值會自動遷移，且不覆蓋玩家自行修改的提示詞與設定 / Known legacy mojibake defaults are migrated without overwriting player-customized prompts or settings

## ♡ 她能陪你做什麼？ / What can she do?

| | 繁體中文 | English |
|---|---|---|
| 💬 | 使用文字或按鍵語音和莉莉絲聊天 | Chat through text or push-to-talk voice input |
| 🎙️ | 中文、日文語音輸出，並記住你的選擇 | Chinese and Japanese voice output with saved preferences |
| 🧠 | 保留有限的對話記憶與角色人格 | Keep lightweight conversation memory and character personality |
| 🌦️ | 查詢時間、天氣與網路資訊 | Check time, weather, and current web information |
| 💌 | 在重要對話後，偶爾收到她的信 | Receive occasional letters after meaningful conversations |
| 🖥️ | 經你開啟後，執行白名單內的電腦操作 | Run reviewed, allowlisted computer actions after you enable them |

<div align="center">

<img src="docs/images/settings-and-voice-language.png" alt="Controls page with F7 text input, F6 push-to-talk, and the advanced PC controls switch">

<sub>「控制」分頁裡的 F7、F6 和高級電腦操作。提供商與模型在左側「AI」分頁，這張圖裡沒有。 · F7, F6, and advanced PC controls on the Controls page. Provider and model setup is on the AI page, which is not shown here.</sub>

</div>

## ✦ AI 服務相容性 / AI provider compatibility

> **目前仍建議優先使用 Gemini。** 角色人格、上下文和工具主要圍繞 Gemini 開發。千問已完成文字對話、語音辨識、聯網搜尋與明確本機指令的接入測試。OpenAI 與 DeepSeek 仍是實驗性文字相容層。語音辨識在「AI」分頁單獨選擇，不再因為聊天用了 OpenAI 或 DeepSeek 就固定走 Gemini。

> **Gemini remains the recommended provider.** Personality, context, and tools were developed primarily around Gemini. Qwen has been tested for chat, speech recognition, web search, and explicit local commands. OpenAI and DeepSeek remain experimental text-chat layers. Speech recognition is chosen on the AI page and is no longer forced to Gemini when chat uses OpenAI or DeepSeek.

| 功能 / Feature | Gemini | Qwen | OpenAI / DeepSeek |
|---|---|---|---|
| 文字對話 / Text chat | ✅ 已測試並完整調整 / Fully tested and tuned | ✅ `qwen3.7-plus` 已測試 / `qwen3.7-plus` tested | 🧪 基本相容，未完成端到端測試 / Basic compatibility only |
| `F6` 語音辨識 / Speech recognition | 在「AI」分頁單獨選擇 / Chosen separately on the AI page | 在「AI」分頁單獨選擇 / Chosen separately on the AI page | 在「AI」分頁單獨選擇 / Chosen separately on the AI page |
| 即時聯網搜尋 / Live web search | ✅ Google Search grounding | ✅ 千問原生聯網 / Qwen native web search | ❌ 尚未接入 / Not integrated |
| 電腦工具 / PC tools | ✅ Function calling 與本機可靠路由 | ✅ 明確指令的本機可靠路由 | ⚠️ 僅部分明確本機指令 |

千問的可用性、免費額度與計費要求依玩家的阿里雲模型服務帳戶及地區而定。OpenAI／DeepSeek 的模型名稱、回應格式、額度與地區限制尚未完整驗證。

Qwen availability, free quotas, and billing requirements depend on the player's Alibaba Cloud Model Studio account and region. OpenAI/DeepSeek model names, response formats, quotas, and regional availability have not been fully validated.

本機 GPT-SoVITS 語音合成可朗讀已成功取得的文字回覆，因此 OpenAI／DeepSeek 的文字聊天若正常回傳，仍可能播放合成語音；但這不代表該供應商的整體流程已完成測試。模型名稱、API 規格、地區限制或服務商更新也可能影響實驗性相容層。

Local GPT-SoVITS can speak a successfully returned text reply, so OpenAI/DeepSeek responses may still produce synthesized speech. This does not mean their complete workflows have been validated. Model names, API behavior, regional availability, or provider updates may also affect the experimental compatibility layer.

## 🍓 一鍵安裝 / One-click setup

### 下載 / Download

玩家只下載這一個檔案，然後雙擊執行：

**[LilithAI-Mod-Setup.exe](https://github.com/Shxiao101/Lilith-AI-Mod-plus/releases/download/v0.2.2/LilithAI-Mod-Setup.exe)**

Players download only this file, then run it:

**[LilithAI-Mod-Setup.exe](https://github.com/Shxiao101/Lilith-AI-Mod-plus/releases/download/v0.2.2/LilithAI-Mod-Setup.exe)**

安裝器會自己下載核心包。補充台詞語音（約 301 MB，預設不勾選）和動態語音運行時（約 1.98 GB）也由安裝器取得，來源是原作者的 Release，不用自己去找，也不在本頁附件裡。

The installer downloads the core package by itself. The supplemental voice pack (~301 MB, unchecked by default) and the dynamic voice runtime (~1.98 GB) are also fetched by the installer from the original author's releases. They are not attachments on this page.

發布頁上其餘檔案都不用下：

- `core.zip`：核心包，安裝器會下載。
- `release-manifest.json`：下載地址和校驗和，安裝器會下載。
- `SHA256SUMS.txt`：只給想自行核對檔案的人。

You do not need to download the other files on the release page:

- `core.zip` is the core package. The installer downloads it.
- `release-manifest.json` holds the download addresses and checksums. The installer downloads it.
- `SHA256SUMS.txt` is only for people who want to verify the files themselves.

> ⚠️ **不要用本頁的 `Code → Download ZIP`。那是原始碼，不能安裝。**  
> ⚠️ **Do not use `Code → Download ZIP` on this page. That archive is source code and cannot install the mod.**

> 官方已更新中文與日文語音並覆蓋絕大多數台詞，補充語音包改為可選項目，安裝程式預設不勾選。
> The official game now ships Chinese and Japanese voiceovers covering almost every line, so the supplemental voice pack is optional and unchecked by default.

1. 執行安裝程式。它會讀取 Steam 與所有 Steam Library，自動尋找遊戲；只有找不到時才需要選擇包含 `Lilith.exe` 的資料夾。  
   Run the installer. It searches Steam and every Steam library, and asks for the folder that contains `Lilith.exe` only when it cannot find the game.
2. 保持需要的元件勾選，點「安裝／更新」。首次安裝動態語音會建立獨立的推理環境，不會改動電腦裡已有的 Python。  
   Keep the components you want, then click Install / Update. The first dynamic-voice install builds its own inference environment and does not modify an existing Python.
3. 啟動遊戲，打開設定左側的「AI」分頁，分別配置聊天和語音識別。F6／F7 仍在「控制」分頁。  
   Launch the game, open the **AI** page in Settings, and configure chat and speech recognition there. F6 and F7 stay on the Controls page.

> ✧ 安裝包不包含作者的 API Key、聊天記錄、玩家名稱或私人資料。  
> ✧ The package contains no author API key, chat history, player name, or private data.

## ✦ 預設操作 / Default controls

- `F7`：開啟文字輸入氣泡 / Open the text input bubble
- 按住 `F6`：錄音；放開後辨識並送出 / Hold to record; release to transcribe and send
- 兩個按鍵皆可在遊戲設定中重新綁定 / Both keys can be reassigned in game settings

## 🖥️ 電腦操作 / PC controls

莉莉絲的電腦操作分成兩層。日常、低風險的功能可以直接使用；可能影響目前桌面狀態的功能，必須由玩家在遊戲設定中手動開啟「進階電腦操作」。這個開關**不會授予 Windows 系統管理員權限**。

Lilith's PC controls have two levels. Routine, low-risk actions are available normally. Actions that can affect the current desktop require the player to manually enable **Advanced Computer Controls** in the game settings. This switch **does not grant Windows administrator privileges**.

### ♡ 一般電腦操作 / Standard controls

不需要開啟進階功能：

Available without the advanced toggle:

- 依照常用名稱開啟或切回已安裝的應用程式與遊戲，例如記事本、計算機、瀏覽器、Steam、Spotify、Discord、VALORANT 等；不接受任意路徑或指令  
  Open or focus recognized applications and games by common name, such as Notepad, Calculator, a browser, Steam, Spotify, Discord, or VALORANT; arbitrary paths and commands are not accepted
- 播放／暫停、上一首、下一首、停止、靜音及調整系統音量  
  Play/pause, previous/next track, stop, mute, and system volume controls
- 查看非個人的本機狀態：電池、記憶體、系統磁碟可用空間與網路連線  
  Report non-personal local status: battery, memory, system-drive free space, and network availability

### ✦ 進階電腦操作 / Advanced controls

只有玩家主動開啟設定後才可使用：

Available only after the player explicitly enables the setting:

| 功能 / Feature | 可以做什麼 / What it can do |
|---|---|
| 📁 常用資料夾 / Known folders | 開啟下載、桌面、文件、圖片、音樂、影片、截圖、MOD 資料夾或資源回收筒；不接受任意檔案路徑 / Open Downloads, Desktop, Documents, Pictures, Music, Videos, Screenshots, the MOD folder, or Recycle Bin; arbitrary paths are not accepted |
| 🪟 視窗 / Windows | 顯示桌面、工作檢視、切換上一個視窗、最小化、最大化、還原，以及靠左／靠右排列 / Show Desktop, Task View, switch to the previous window, minimize, maximize, restore, or snap left/right |
| 📷 截圖 / Screenshots | 擷取所有螢幕並只存到 `圖片\Lilith Screenshots`；圖片不會回傳或上傳給模型 / Capture all monitors to `Pictures\Lilith Screenshots`; the image is not returned or uploaded to the model |
| 📋 複製文字 / Copy text | 只把玩家明確指定、非敏感的文字寫入剪貼簿；無法讀取剪貼簿 / Write only player-specified, non-sensitive text to the clipboard; clipboard reading is unavailable |
| 🔎 瀏覽器搜尋 / Browser search | 只有玩家明確要求時，才用預設瀏覽器開啟 Google 搜尋 / Open a Google search in the default browser only when explicitly requested |
| ⌨️ 安全快捷鍵 / Safe shortcuts | 復原、重做、儲存、全選、尋找、重新整理、全螢幕與 Escape；不支援任意按鍵或任意輸入 / Undo, redo, save, select all, find, refresh, fullscreen, and Escape; arbitrary keys and typing are unavailable |
| ⏲️ 計時器 / Timers | 建立或取消最長 24 小時的本機計時器，時間到由莉莉絲提醒 / Create or cancel local timers up to 24 hours, announced by Lilith |
| 🔒 鎖定與睡眠 / Lock & sleep | 只有在玩家明確要求「鎖定電腦」或「讓電腦睡眠」時才執行，並可在等待期間取消 / Run only after an explicit request to lock or sleep the PC, with cancellation available during the pending period |

## 🖤 溫柔也需要邊界 / Gentle, with boundaries

「進階電腦操作」預設關閉。即使開啟，MOD 也只執行經過檢查的白名單功能，不提供檔案刪除、清空資源回收筒、關機／重新啟動、關閉或強制結束程式、任意 PowerShell／CMD、系統管理員提權、密碼／API Key／OTP 讀取、剪貼簿讀取、任意打字或任意快捷鍵。

“Advanced Computer Controls” are disabled by default. When enabled, only reviewed allowlisted actions are available—never file deletion, emptying the Recycle Bin, shutdown/restart, closing or terminating apps, arbitrary PowerShell/CMD, privilege elevation, password/API key/OTP access, clipboard reading, arbitrary typing, or arbitrary shortcuts.

AI 對話與語音辨識會依玩家選擇傳送至相應服務商；GPT-SoVITS 語音合成則在本機 `127.0.0.1` 運行。

AI chat and speech recognition are sent to the provider selected by the player. GPT-SoVITS voice synthesis runs locally on `127.0.0.1`.

## ✧ 系統需求 / Requirements

- Windows 10/11 x64
- AI 對話與語音辨識需要網路及玩家自己的 API Key  
  AI chat and speech recognition require internet access and the player's own API key
- 動態語音建議 NVIDIA GPU 8 GB VRAM 與 16 GB RAM；沒有相容顯示卡時可使用較慢的 CPU 模式  
  An NVIDIA GPU with 8 GB VRAM and 16 GB RAM is recommended; a slower CPU mode is available

## ♡ 完整說明 / Full guides

- [繁體中文](README_繁體中文.md)
- [简体中文](README_简体中文.md)
- [日本語](README_日本語.md)
- [English](README_EN.md)
- [Third-party licenses](THIRD_PARTY_NOTICES.md)

## ⚠ 非官方聲明 / Unofficial project notice

本 MOD 是玩家獨立製作、免費且非商業的社群作品，未獲遊戲開發商、發行商、角色權利人或原配音員授權、認可或贊助。遊戲、角色、美術、原始台詞與錄音的相關權利均屬其合法權利人。請勿將 AI 生成內容冒充官方內容或原配音員的新錄音。

This is a free, non-commercial fan project. It is not authorized, endorsed, sponsored by, or affiliated with the developer, publisher, character rights holders, or original voice actors. Rights to the game, characters, artwork, original dialogue, and recordings remain with their lawful owners. Do not present AI-generated material as official content or new recordings by the original performers.

---

<div align="center">

### ✦ 「如果你願意，我就再陪你一會。」 ✦

**Version 0.2.2 · Publisher: Shxiao**  
Fork of [mimimi6666/Lilith-AI-Mod](https://github.com/mimimi6666/Lilith-AI-Mod) 0.1.1-RC4 · Contact: shxiao1115@163.com

</div>

